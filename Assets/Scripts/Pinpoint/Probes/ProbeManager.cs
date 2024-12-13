using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BrainAtlas;
using EphysLink;
using Pinpoint.Probes;
using Pinpoint.Probes.ManipulatorBehaviorController;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using Urchin.Utils;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

/// <summary>
/// 3D space control for Neuropixels probes in the Trajectory Planner scene
/// </summary>
public class ProbeManager : MonoBehaviour
{
    #region Webgl only
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void Copy2Clipboard(string str);
#endif
    #endregion

    #region Static fields
    public static readonly List<ProbeManager> Instances = new();
    public static ProbeManager ActiveProbeManager;

    // Static events
    public static readonly UnityEvent<HashSet<ProbeManager>> EphysLinkControlledProbesChangedEvent =
        new();
    public static readonly UnityEvent ActiveProbeUIUpdateEvent = new();
    #endregion

    #region Events

    public UnityEvent UIUpdateEvent;
    public UnityEvent ActivateProbeEvent;
    public UnityEvent EphysLinkControlChangeEvent;

    #endregion


    #region Identifiers
    public string UUID { get; private set; }
    private string _overrideName;
    public string OverrideName
    {
        get => _overrideName;
        set
        {
            _overrideName = value;
            UpdateName();
            UIUpdateEvent.Invoke();
            if (ActiveProbeManager == this)
                ActiveProbeUIUpdateEvent.Invoke();
        }
    }

    #endregion

    // Exposed fields to collect links to other components inside of the Probe prefab
    [FormerlySerializedAs("probeColliders")]
    [SerializeField]
    private List<Collider> _probeColliders;

    [FormerlySerializedAs("probeUIManagers")]
    [SerializeField]
    private List<ProbeUIManager> _probeUIManagers;

    [FormerlySerializedAs("probeRenderer")]
    [SerializeField]
    private Renderer _probeRenderer;

    [SerializeField]
    private RecordingRegion _recRegion;

    private AxisControl _axisControl;
    public ProbeProperties.ProbeType ProbeType;

    [FormerlySerializedAs("probeController")]
    [SerializeField]
    private ProbeController _probeController;

    [SerializeField]
    private Material _lineMaterial;

    [FormerlySerializedAs("ghostMaterial")]
    [SerializeField]
    private Material _ghostMaterial;

    private Dictionary<GameObject, Material> _defaultMaterials;
    private HashSet<Renderer> _activeRenderers;

    #region Channel map
    public string SelectionLayerName { get; private set; }

    /// <summary>
    /// Return the minimum and maximum channel position in the current selection in mm
    /// </summary>
    public (float, float) ChannelMinMaxYCoord
    {
        get { return (_channelMap.MinChannelHeight, _channelMap.MaxChannelHeight); }
    }
    private TaskCompletionSource<bool> _channelMapLoadedSource;
    public Task ChannelMapTask
    {
        get { return _channelMapLoadedSource.Task; }
    }
    private ChannelMap _channelMap;

    /// <summary>
    /// Get the channel map information from all active ProbeManager instances
    /// </summary>
    /// <returns>Array of "name:data" strings, including quotes</returns>
    public static string[] GetAllChannelAnnotationData()
    {
        return Instances.Select(x => $"\"{x.name}:{x.GetChannelAnnotationIDs(false)}\"").ToArray();
    }
    #endregion

    // Probe position data
    private Vector3 _recRegionBaseCoordWorldU;
    private Vector3 _recRegionTopCoordWorldU;

    public (Vector3 tipCoordU, Vector3 endCoordU) RecRegionCoordWorldU
    {
        get { return (_recRegionBaseCoordWorldU, _recRegionTopCoordWorldU); }
    }

    // Text
    private const float minYaw = -180;
    private const float maxYaw = 180f;
    private const float minRoll = -180f;
    private const float maxRoll = 180f;

    // Brain surface position
    private bool _probeInBrain;
    private Vector3 _brainSurfaceCoordT;
    private Vector3 _brainSurfaceWorldU;
    private Vector3 _brainSurfaceWorldT;

    #region Accessors

    private ProbeDisplayType _probeDisplayType;
    public ProbeDisplayType ProbeDisplay
    {
        get { return _probeDisplayType; }
        set
        {
            _probeDisplayType = value;
            SetMaterials();
        }
    }

    public ProbeController ProbeController
    {
        get => _probeController;
        private set => _probeController = value;
    }

    public bool Locked
    {
        get { return _probeController.Locked; }
    }

    public ManipulatorBehaviorController ManipulatorBehaviorController =>
        gameObject.GetComponent<ManipulatorBehaviorController>();

    public bool IsEphysLinkControlled
    {
        get => ManipulatorBehaviorController && ManipulatorBehaviorController.enabled;
        private set
        {
            ManipulatorBehaviorController.enabled = value;
            EphysLinkControlChangeEvent.Invoke();
            EphysLinkControlledProbesChangedEvent.Invoke(
                Instances.Where(manager => manager.IsEphysLinkControlled).ToHashSet()
            );
        }
    }

    public string APITarget { get; set; }

    private Color _color;
    public Color Color
    {
        get => _color;
        set
        {
            // try to return the current color
            ProbeProperties.ReturnColor(_color);

            _color = value;
            _probeRenderer.material.color = _color;

            foreach (ProbeUIManager puiManager in _probeUIManagers)
                puiManager.UpdateColors();

            UIUpdateEvent.Invoke();
            if (ActiveProbeManager == this)
                ActiveProbeUIUpdateEvent.Invoke();
            Debug.Log(_color);
        }
    }

    public void DisableAllColliders()
    {
        foreach (var probeCollider in _probeColliders)
        {
            probeCollider.enabled = false;
        }
    }

    /// <summary>
    /// Return the probe panel UI managers
    /// </summary>
    /// <returns>list of probe panel UI managers</returns>
    public List<ProbeUIManager> GetProbeUIManagers()
    {
        return _probeUIManagers;
    }

    /// <summary>
    /// When true, this Probe will be saved and re-loaded in the scene the next time Pinpoint loads
    /// </summary>
    public bool Saved { get; set; }
    
    #endregion

    #region Unity

    private async void Awake()
    {
        Saved = true;

        UUID = Guid.NewGuid().ToString();
        UpdateName();

        // Set color
        if (_probeRenderer != null)
            _color = ProbeProperties.NextColor;

        // Record default materials
        _defaultMaterials = new Dictionary<GameObject, Material>();
        _activeRenderers = new();

        foreach (var childRenderer in transform.GetComponentsInChildren<Renderer>())
        {
            _defaultMaterials.Add(childRenderer.gameObject, childRenderer.material);

            // If this renderer is NOT attached to a collider gameobject, hold it as active
            if (!_probeColliders.Any(x => x.gameObject.Equals(childRenderer.gameObject)))
                _activeRenderers.Add(childRenderer);
        }

        // Pull the tpmanager object and register this probe
        _probeController.Register(this);

        // Get the channel map and selection layer
        _channelMapLoadedSource = new();
        var cmapHandle = ChannelMapManager.GetChannelMap(ProbeType);
        await cmapHandle;
        _channelMap = cmapHandle.Result;
        _channelMapLoadedSource.SetResult(true);
        SelectionLayerName = "default";
        UpdateSelectionLayer(SelectionLayerName);

        _axisControl = GameObject.Find("AxisControl").GetComponent<AxisControl>();

        _probeController.FinishedMovingEvent.AddListener(UpdateName);
        _probeController.MovedThisFrameEvent.AddListener(ProbeMoved);
    }

    private void Start()
    {
#if UNITY_EDITOR
        Debug.Log($"(ProbeManager) New probe created with UUID: {UUID}");
#endif
        // Force update color
        foreach (ProbeUIManager puiManager in _probeUIManagers)
            puiManager.UpdateColors();
        if (_probeRenderer)
            _probeRenderer.material.color = _color;

        UIUpdateEvent.Invoke();
    }

    /// <summary>
    /// Called by Unity when this object is destroyed.
    /// Unregisters the probe from tpmanager
    /// Removes the probe panels and the position text.
    /// </summary>
    public void Cleanup()
    {
        ProbeProperties.ReturnColor(Color);

        ColliderManager.RemoveProbeColliderInstances(_probeColliders);

        // Force disable Ephys Link
        if (IsEphysLinkControlled)
        {
            IsEphysLinkControlled = false;
        }

        // Delete this gameObject
        foreach (ProbeUIManager puimanager in _probeUIManagers)
            puimanager.Cleanup();
    }

    private void OnDestroy()
    {
        // Destroy instance
        Debug.Log($"Destroying probe: {name}");

        if (ProbeInsertion.Instances.Count == 1)
            ProbeInsertion.Instances.Clear();
        else
            ProbeInsertion.Instances.Remove(ProbeController.Insertion);

        if (Instances.Count == 1)
            Instances.Clear();
        else
            Instances.Remove(this);
    }

    private void OnEnable() => Instances.Add(this);

    #endregion

    /// <summary>
    /// Called by the TPManager when this Probe becomes the active probe
    /// </summary>
    public void SetActive(bool active)
    {
#if UNITY_EDITOR
        Debug.Log($"{name} becoming {(active ? "active" : "inactive")}");
#endif

        ColliderManager.AddProbeColliderInstances(_probeColliders, active);
        GetComponent<ProbeController>().enabled = active;

        UIUpdateEvent.Invoke();
        _probeController.MovedThisFrameEvent.Invoke();
    }

    /// <summary>
    /// Called by ProbeColliders when this probe is clicked on
    /// </summary>
    public void MouseDown()
    {
        ActivateProbeEvent.Invoke();
    }

    public async Task<ChannelMap> GetChannelMap()
    {
        await _channelMapLoadedSource.Task;
        return _channelMap;
    }

    public void Update2ActiveTransform()
    {
        _probeController.SetSpaceTransform(
            BrainAtlasManager.ActiveReferenceAtlas.AtlasSpace,
            BrainAtlasManager.ActiveAtlasTransform
        );
    }

    public void SetUIVisibility(bool state)
    {
        foreach (ProbeUIManager puimanager in _probeUIManagers)
            puimanager.SetProbePanelVisibility(state);
    }

    public void OverrideUUID(string newUUID)
    {
        UUID = newUUID;
        UpdateName();
        UIUpdateEvent.Invoke();
    }

    /// <summary>
    /// Update the name of this probe, when it is in the brain
    /// </summary>
    public void UpdateName()
    {
        if (OverrideName != null)
        {
            name = OverrideName;
        }
        else
        {
            // Check if this probe is in the brain
            name = _probeInBrain ? $"{_probeUIManagers[0].MaxArea}-{UUID[..8]}" : UUID[..8];
        }
    }

    public void ProbeMoved()
    {
        ProbeInsertion insertion = _probeController.Insertion;
        var channelCoords = GetChannelRangemm();

        // Update the world coordinates for the tip position
        Vector3 startCoordWorldT =
            _probeController.ProbeTipT.position
            + -_probeController.ProbeTipT.forward * channelCoords.startPosmm;
        Vector3 endCoordWorldT =
            _probeController.ProbeTipT.position
            + -_probeController.ProbeTipT.forward * channelCoords.endPosmm;
        _recRegionBaseCoordWorldU = BrainAtlasManager.WorldT2WorldU(startCoordWorldT, true);
        _recRegionTopCoordWorldU = BrainAtlasManager.WorldT2WorldU(endCoordWorldT, true);
    }

    #region Channel map
    public (
        float startPosmm,
        float endPosmm,
        float recordingSizemm,
        float fullHeight
    ) GetChannelRangemm()
    {
        (float startPosmm, float endPosmm) = ChannelMinMaxYCoord;
        float recordingSizemm = endPosmm - startPosmm;

        return (startPosmm, endPosmm, recordingSizemm, _channelMap.FullHeight);
    }

    public async void UpdateSelectionLayer(string selectionLayerName)
    {
#if UNITY_EDITOR
        Debug.Log($"Updating selection layer to {selectionLayerName}");
#endif
        SelectionLayerName = selectionLayerName;
        await _channelMapLoadedSource.Task;
        _channelMap.SetSelectionLayer(SelectionLayerName);

        UpdateChannelMap();

        UIUpdateEvent.Invoke();
    }

    /// <summary>
    /// Update the channel map data according to the selected channels
    /// Defaults to the first 384 channels
    ///
    /// Sets channelMinY/channelMaxY in mm
    /// </summary>
    public void UpdateChannelMap()
    {
#if UNITY_EDITOR
        Debug.Log(
            $"(ProbeManager) Minimum channel coordinate {_channelMap.MinChannelHeight} max {_channelMap.MaxChannelHeight}"
        );
#endif

        // see if we can bypass this:

        //foreach (ProbeUIManager puiManager in _probeUIManagers)
        //    puiManager.UpdateChannelMap();

        if (_recRegion != null)
            _recRegion.SetSize(_channelMap.MinChannelHeight, _channelMap.MaxChannelHeight);

        // Update the in-plane slice
        ProbeController.MovedThisFrameEvent.Invoke();
    }

    /// <summary>
    /// Get a serialized representation of the depth information on each shank of this probe
    ///
    /// SpikeGLX format
    /// [probe,shank](bottom um, top um, r, g, b, acronym)
    /// </summary>
    /// <returns>List of strings, each of which has data for one shank in the scene</returns>
    public List<string> GetProbeDepthIDs()
    {
        List<string> depthIDs = new List<string>();
        if (ProbeProperties.FourShank(ProbeType))
        {
            // do something else

            for (int si = 0; si < 4; si++)
                depthIDs.Add(perShankDepthIDs(si));
        }
        else
        {
            depthIDs.Add(perShankDepthIDs(0));
        }
        return depthIDs;
    }

    /// <summary>
    /// Gets the probe anatomy in depth format (SpikeGLX)
    /// </summary>
    /// <param name="shank"></param>
    /// <returns></returns>
    private string perShankDepthIDs(int shank)
    {
        // Create a list of range, acronym color
        List<(int bot, int top, string acronym, Color color)> probeAnnotationData = new();

        ProbeUIManager uiManager = _probeUIManagers[shank];

        Transform electrodeBaseT = uiManager.ShankTipT();
        Vector3 baseCoordWorldT = electrodeBaseT.position;
        Vector3 topCoordWorldT = baseCoordWorldT - electrodeBaseT.forward * _channelMap.FullHeight;
        float height = _channelMap.FullHeight;

        Vector3 baseCoordWorldU = BrainAtlasManager.WorldT2WorldU(baseCoordWorldT, true);
        Vector3 topCoordWorldU = BrainAtlasManager.WorldT2WorldU(topCoordWorldT, true);

        // Lerp between the base and top coordinate in small steps

        int lastID = BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(
            BrainAtlasManager.ActiveReferenceAtlas.World2AtlasIdx(baseCoordWorldU)
        );
        if (lastID < 0)
            lastID = 0;

        float curBottom = 0f;

        for (float perc = 0f; perc < 1f; perc += 0.01f)
        {
            Vector3 coordU = Vector3.Lerp(baseCoordWorldU, topCoordWorldU, perc);

            Debug.Log(BrainAtlasManager.ActiveReferenceAtlas.World2AtlasIdx(coordU));
            int ID = BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(
                BrainAtlasManager.ActiveReferenceAtlas.World2AtlasIdx(coordU)
            );
            if (ID < 0)
                ID = 0;
            Debug.Log(ID);

            if (Settings.UseBeryl)
                ID = BrainAtlasManager.ActiveReferenceAtlas.Ontology.RemapID_NoLayers(ID);

            if (ID != lastID)
            {
                // Save the current step
                float newHeight = perc * height * 1000f;
                probeAnnotationData.Add(
                    (
                        Mathf.RoundToInt(curBottom),
                        Mathf.RoundToInt(newHeight),
                        BrainAtlasManager.ActiveReferenceAtlas.Ontology.ID2Acronym(ID),
                        BrainAtlasManager.ActiveReferenceAtlas.Ontology.ID2Color(ID)
                    )
                );
                curBottom = newHeight;
                lastID = ID;
            }
        }

        // Save the final step
        probeAnnotationData.Add(
            (
                Mathf.RoundToInt(curBottom),
                Mathf.RoundToInt(height * 1000),
                BrainAtlasManager.ActiveReferenceAtlas.Ontology.ID2Acronym(lastID),
                BrainAtlasManager.ActiveReferenceAtlas.Ontology.ID2Color(lastID)
            )
        );

        // Flatten the list data according to the SpikeGLX format
        // [probe, shank](startpos, endpos, r, g, b, name)
        // [0,0](0,1000,200,0,0,cortex)

        string probeStr = $"[{APITarget},{shank}]";

        foreach (var data in probeAnnotationData)
        {
            probeStr +=
                $"({data.bot},{data.top},"
                + $"{Mathf.RoundToInt(data.color.r * 255)},{Mathf.RoundToInt(data.color.g * 255)},{Mathf.RoundToInt(data.color.b * 255)},"
                + $"{data.acronym})";
        }

        return probeStr;
    }

    /// <summary>
    /// Get a serialized representation of the channel ID data
    ///
    /// (OpenEphys format)
    /// </summary>
    /// <returns></returns>
    public string GetChannelAnnotationIDs(bool collapsed = true)
    {
        // Get the channel data
        var channelMapData = _channelMap.GetLayerCoords("all");

        List<(int idx, int ID, string acronym, string color)> channelAnnotationData = new();

        if (ProbeProperties.FourShank(ProbeType))
        {
            //channelStrings = new string[channelMapData.Count * 4];

            for (int si = 0; si < 4; si++)
            {
                ProbeUIManager uiManager = _probeUIManagers[si];
                Vector3 shankTipCoordWorldT = uiManager.ShankTipT().position;

                for (int i = 0; i < channelMapData.Count; i++)
                {
                    // For now we'll ignore x changes and just use the y coordinate, this way we don't need to calculate the forward vector for the probe
                    // note that we're ignoring depth here, this assume the probe tip is on the electrode surface (which it should be)
                    Vector3 channelCoordWorldT =
                        shankTipCoordWorldT
                        - _probeController.ProbeTipT.forward * channelMapData[i].y / 1000f;

                    // Now transform this into WorldU
                    Vector3 channelCoordWorldU = BrainAtlasManager.WorldT2WorldU(
                        channelCoordWorldT,
                        true
                    );

                    int elecIdx = si * channelMapData.Count + i;
                    int ID = BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(
                        BrainAtlasManager.ActiveReferenceAtlas.World2AtlasIdx(channelCoordWorldU)
                    );
                    if (ID < 0)
                        ID = -1;

                    if (Settings.UseBeryl)
                        ID = BrainAtlasManager.ActiveReferenceAtlas.Ontology.RemapID_NoLayers(ID);

                    string acronym = BrainAtlasManager.ActiveReferenceAtlas.Ontology.ID2Acronym(ID);
                    Color color = BrainAtlasManager.ActiveReferenceAtlas.Ontology.ID2Color(ID);

                    channelAnnotationData.Add((elecIdx, ID, acronym, Utils.Color2Hex(color)));
                }
            }
        }
        else
        {
            // Populate the data string
            Vector3 tipCoordWorldT = _probeController.ProbeTipT.position;

            for (int i = 0; i < channelMapData.Count; i++)
            {
                // For now we'll ignore x changes and just use the y coordinate, this way we don't need to calculate the forward vector for the probe
                // note that we're ignoring depth here, this assume the probe tip is on the electrode surface (which it should be)
                Vector3 channelCoordWorldT =
                    tipCoordWorldT
                    - _probeController.ProbeTipT.forward * channelMapData[i].y / 1000f;

                // Now transform this into WorldU
                Vector3 channelCoordWorldU = BrainAtlasManager.WorldT2WorldU(
                    channelCoordWorldT,
                    false
                );

                int ID = BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(
                    BrainAtlasManager.ActiveReferenceAtlas.World2AtlasIdx(channelCoordWorldU)
                );
                if (ID < 0)
                    ID = -1;

                if (Settings.UseBeryl)
                    ID = BrainAtlasManager.ActiveReferenceAtlas.Ontology.RemapID_NoLayers(ID);

                string acronym = BrainAtlasManager.ActiveReferenceAtlas.Ontology.ID2Acronym(ID);
                Color color = BrainAtlasManager.ActiveReferenceAtlas.Ontology.ID2Color(ID);

                channelAnnotationData.Add((i, ID, acronym, Utils.Color2Hex(color)));
            }
        }

        string[] channelStrings;

        // If the data need to be collapsed, combine all channel IDs that are the same
        if (collapsed)
        {
            List<(string idxRange, string acronym, string color)> collapsedAnnotationData = new();

            int firstIdx = 0;
            int curID = channelAnnotationData[0].ID;
            string curAcronym = channelAnnotationData[0].acronym;
            string curColor = channelAnnotationData[0].color;

            for (int curIdx = 1; curIdx < channelAnnotationData.Count; curIdx++)
            {
                var data = channelAnnotationData[curIdx];
                if (data.ID != curID)
                {
                    // save the previous indexes as a range, then start a new one
                    collapsedAnnotationData.Add(($"{firstIdx}-{curIdx - 1}", curAcronym, curColor));
                    // start new
                    firstIdx = curIdx;
                    curID = channelAnnotationData[curIdx].ID;
                    curAcronym = channelAnnotationData[curIdx].acronym;
                    curColor = channelAnnotationData[curIdx].color;
                }
            }

            // make sure to get the final set of data
            if (firstIdx < (channelAnnotationData.Count - 1))
                collapsedAnnotationData.Add(
                    ($"{firstIdx}-{channelAnnotationData.Count - 1}", curAcronym, curColor)
                );

            channelStrings = collapsedAnnotationData
                .Select(x => $"{x.idxRange},{x.acronym},{x.color}")
                .ToArray();
        }
        else
            channelStrings = channelAnnotationData
                .Select(x => $"{x.idx},{x.ID},{x.acronym},{x.color}")
                .ToArray();

        return string.Join(";", channelStrings);
    }

    public void SetChannelVisibility(bool visible)
    {
        // TODO
    }

    #endregion

    #region Text

    public void Probe2Text()
    {
        string apStr;
        string mlStr;
        string dvStr = "DV";

        ProbeInsertion insertion = _probeController.Insertion;

        // If we are using the
        if (Settings.ConvertAPML2Probe)
        {
            Debug.LogWarning("Not working");
            apStr = "Forward";
            mlStr = "Right";
        }
        else
        {
            apStr = "AP";
            mlStr = "ML";
        }

        float mult = Settings.DisplayUM ? 1000f : 1f;

        Vector3 tipAtlasU =
            insertion.PositionSpaceU()
            + BrainAtlasManager.ActiveReferenceAtlas.AtlasSpace.ReferenceCoord;
        Vector3 tipAtlasT = insertion.APMLDV;

        Vector3 angles = insertion.Angles;

        (Vector3 entryAtlasT, float depthTransformed) = GetSurfaceCoordinateT();

        Vector3 entryAtlasU =
            BrainAtlasManager.ActiveAtlasTransform.T2U(entryAtlasT)
            + BrainAtlasManager.ActiveReferenceAtlas.AtlasSpace.ReferenceCoord;

        if (Settings.ConvertAPML2Probe)
        {
            float cos = Mathf.Cos(-angles.x * Mathf.Deg2Rad);
            float sin = Mathf.Sin(-angles.x * Mathf.Deg2Rad);

            float xRot = entryAtlasT.x * cos - entryAtlasT.y * sin;
            float yRot = entryAtlasT.x * sin + entryAtlasT.y * cos;

            entryAtlasT.x = xRot;
            entryAtlasT.y = yRot;

            float tipXRot = tipAtlasT.x * cos - tipAtlasT.y * sin;
            float tipYRot = tipAtlasT.x * sin + tipAtlasT.y * cos;

            tipAtlasT.x = tipXRot;
            tipAtlasT.y = tipYRot;
        }

        string dataStr = string.Format(
            $"{name}: ReferenceAtlas {BrainAtlasManager.ActiveReferenceAtlas.AtlasSpace.Name}, "
                + $"AtlasTransform {BrainAtlasManager.ActiveAtlasTransform.Name}, "
                + $"Entry and Tip are ({apStr}, {mlStr}, {dvStr}), "
                + $"Entry ({round0(entryAtlasT.x * mult)}, {round0(entryAtlasT.y * mult)}, {round0(entryAtlasT.z * mult)}), "
                + $"Tip ({round0(tipAtlasT.x * mult)}, {round0(tipAtlasT.y * mult)}, {round0(tipAtlasT.z * mult)}), "
                + $"Angles ({round2(Utils.CircDeg(angles.x, minYaw, maxYaw))}, {round2(angles.y)}, {round2(Utils.CircDeg(angles.z, minRoll, maxRoll))}), "
                + $"Depth {round0(depthTransformed * mult)}, "
                + $"CCF Entry ({round0(entryAtlasU.x * mult)}, {round0(entryAtlasU.y * mult)}, {round0(entryAtlasU.z * mult)}), "
                + $"CCF Tip ({round0(tipAtlasU.x * mult)}, {round0(tipAtlasU.y * mult)}, {round0(tipAtlasU.z * mult)}), "
                + $"CCF Depth {round0(Vector3.Distance(entryAtlasU, tipAtlasU))}"
        );

#if UNITY_WEBGL && !UNITY_EDITOR
        Copy2Clipboard(dataStr);
#else
        GUIUtility.systemCopyBuffer = dataStr;
#endif
    }

    private float round0(float input)
    {
        return Mathf.Round(input);
    }

    private float round2(float input)
    {
        return Mathf.Round(input * 100) / 100;
    }

    #endregion


    /// <summary>
    /// Re-scale probe panels
    /// </summary>
    /// <param name="newPxHeight">Set the probe panels of this probe to a new height</param>
    public void ResizeProbePanel(int newPxHeight)
    {
        foreach (ProbeUIManager puimanager in _probeUIManagers)
            puimanager.ResizeProbePanel(newPxHeight);

        UIUpdateEvent.Invoke();
    }

    #region Brain surface coordinate

    public (Vector3 surfaceCoordinateT, float depthT) GetSurfaceCoordinateT()
    {
        return (
            _brainSurfaceCoordT,
            Vector3.Distance(_probeController.Insertion.APMLDV, _brainSurfaceCoordT)
        );
    }

    public Vector3 GetSurfaceCoordinateWorldT()
    {
        return _brainSurfaceWorldT;
    }

    public bool IsProbeInBrain()
    {
        return _probeInBrain;
    }

    /// <summary>
    ///     Move probe to brain surface
    /// </summary>
    public void DropProbeToBrainSurface()
    {
        if (_probeInBrain)
        {
            _probeController.SetProbePosition(_brainSurfaceCoordT);
        }
        else
        {
            var (entryCoordAtlasIdx, _) = CalculateEntryCoordinate();

            if (float.IsNaN(entryCoordAtlasIdx.x))
            {
                Debug.LogWarning("Could not find brain surface! Canceling set brain offset.");
                return;
            }

            //Vector3 worldU = BrainAtlasManager.ActiveReferenceAtlas.AtlasIdx2World(entryCoordAtlasIdx);
            //Vector3 entryCoordAtlasT = BrainAtlasManager.WorldU2WorldT(worldU);

            var entryCoordAtlasT = BrainAtlasManager.ActiveAtlasTransform.U2T(
                BrainAtlasManager.ActiveReferenceAtlas.World2Atlas(
                    BrainAtlasManager.ActiveReferenceAtlas.AtlasIdx2World(entryCoordAtlasIdx)
                )
            );

            _probeController.SetProbePosition(entryCoordAtlasT);
        }
    }

    /// <summary>
    /// Check whether the probe is in the brain.
    /// If it is, calculate the brain surface coordinate by iterating up the probe until you leave the brain.
    /// </summary>
    public void UpdateSurfacePosition()
    {
        (Vector3 entryCoordAtlasIdx, bool probeInBrain) = CalculateEntryCoordinate();
        _probeInBrain = probeInBrain;

        if (!_probeInBrain)
        {
            _brainSurfaceWorldU = new Vector3(float.NaN, float.NaN, float.NaN);
            _brainSurfaceWorldT = new Vector3(float.NaN, float.NaN, float.NaN);
            _brainSurfaceCoordT = new Vector3(float.NaN, float.NaN, float.NaN);
        }
        else
        {
            // get the surface coordinate in un-transformed world space
            _brainSurfaceWorldU = BrainAtlasManager.ActiveReferenceAtlas.AtlasIdx2World(
                entryCoordAtlasIdx
            );
            // go back into transformed space, only using the reference coordinate for the entry coordinate (not the transform coordinate)
            _brainSurfaceWorldT = BrainAtlasManager.WorldU2WorldT(_brainSurfaceWorldU, true);
            _brainSurfaceCoordT = BrainAtlasManager.ActiveAtlasTransform.U2T(
                BrainAtlasManager.ActiveReferenceAtlas.World2Atlas(_brainSurfaceWorldU, true)
            );
        }
    }

    // TODO: Remove useDV and always use depth.
    /// <summary>
    /// Calculate the entry coordinate on the brain surface, returns coordIdx
    /// </summary>
    /// <param name="useDV">Whether the surface should be found along the DV axis or depth axis.</param>
    /// <returns>(entryCoordAtlasIdx, probeInBrain)</returns>
    public (Vector3 entryCoordAtlasIdx, bool probeInBrain) CalculateEntryCoordinate(
        bool useDV = false
    )
    {
        // note: the backward axis on the probe is the probe's "up" axis
        (Vector3 tipCoordWorldU, _, _, Vector3 tipForwardWorldU) = _probeController.GetTipWorldU();

        Vector3 tipAtlasIdxU = BrainAtlasManager.ActiveReferenceAtlas.World2AtlasIdx(
            tipCoordWorldU
        );

        Vector3 downDir = useDV ? Vector3.down : tipForwardWorldU;
        Vector3 downDirAtlas = BrainAtlasManager.ActiveReferenceAtlas.World2Atlas_Vector(downDir);

        // Check if we're in the brain
        bool probeInBrain =
            BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(tipAtlasIdxU) > 0;

        Vector3 tipInBrain = tipAtlasIdxU;

        // Adjust the tip coordinate to put it into the brain
        // This is kind of a dumb algorithm
        if (!probeInBrain)
        {
            // Get which direction we need to go
            (int ap, int ml, int dv) = BrainAtlasManager.ActiveReferenceAtlas.DimensionsIdx;
            Vector3 center = new Vector3(ap, ml, dv) / 2f;

            Vector3 towardBox = downDirAtlas;

            // Invert if the vector points the wrong way
            if (Vector3.Dot(downDirAtlas, tipAtlasIdxU - center) > 0)
                towardBox = -towardBox;

            // Step in 1mm increments
            float stepSize = 1000f / BrainAtlasManager.ActiveReferenceAtlas.Resolution.z;

            bool done = false;
            for (
                int steps = 1;
                steps < Mathf.RoundToInt(BrainAtlasManager.ActiveReferenceAtlas.Dimensions.z * 2f);
                steps++
            )
            {
                Vector3 tempTip = tipAtlasIdxU + towardBox * stepSize * steps;
                if (BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(tempTip) > 0)
                {
                    tipInBrain = tempTip;
                    done = true;
                    break;
                }
            }

            if (!done)
            {
                Debug.LogWarning("Impossible to find brain surface from here");
                return (new Vector3(float.NaN, float.NaN, float.NaN), false);
            }
        }

        Vector3 entryCoordAtlasIdx = FindEntryIdxCoordinate(tipInBrain, downDirAtlas);

        return (entryCoordAtlasIdx, probeInBrain);
    }

    /// <summary>
    /// Use the annotation dataset to discover whether there is a surface coordinate by going *down* from a point searchDistance
    /// *above* the startPos
    /// returns the coordinate in the annotation dataset that corresponds to the surface.
    ///
    /// Function guarantees that you enter the brain *once* before exiting, so if you start below the brain you need
    /// to enter first to discover the surface coordinate.
    /// </summary>
    /// <param name="bottomIdxCoordU">coordinate to go down to</param>
    /// <param name="downVector"></param>
    /// <returns></returns>
    public Vector3 FindEntryIdxCoordinate(Vector3 bottomIdxCoordU, Vector3 downVector)
    {
        float searchDistance =
            BrainAtlasManager.ActiveReferenceAtlas.Dimensions.z
            * 1000f
            / BrainAtlasManager.ActiveReferenceAtlas.Resolution.z;
        Vector3 topSearchIdxCoordU = bottomIdxCoordU - downVector * searchDistance;

        // If by chance we are inside the brain, go farther
        if (BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(topSearchIdxCoordU) > 0)
            topSearchIdxCoordU = bottomIdxCoordU - downVector * searchDistance * 2f;

        // We're going to speed this up by doing two searches: first a fast search to get into the brain, then a slow search to accurately
        // get the surface coordinate
        float finalPerc = -1f;
        for (float perc = 0; perc <= 1f; perc += 0.01f)
        {
            Vector3 point = Vector3.Lerp(topSearchIdxCoordU, bottomIdxCoordU, perc);
            if (BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(point) > 0)
            {
                finalPerc = perc;
                break;
            }
        }

        if (finalPerc > -1f)
        {
            for (float perc = finalPerc; perc >= 0f; perc -= 0.001f)
            {
                Vector3 point = Vector3.Lerp(topSearchIdxCoordU, bottomIdxCoordU, perc);
                if (BrainAtlasManager.ActiveReferenceAtlas.GetAnnotationIdx(point) <= 0)
                {
                    return point;
                }
            }
        }

        // If you got here it means you *never* entered and then exited the brain
        return new Vector3(float.NaN, float.NaN, float.NaN);
    }

    #endregion

    #region Ephys Link Control

    /// <summary>
    /// (un)Register a probe and begin echoing position.
    /// </summary>
    /// <param name="register">To register or deregister this probe</param>
    /// <param name="manipulatorId">ID of the manipulator in real life to connect to</param>
    /// <param name="calibrated">Is the manipulator in real life calibrated</param>
    /// <param name="onSuccess">Callback function to handle a successful registration</param>
    /// <param name="onError">Callback function to handle a failed registration</param>
    public void SetIsEphysLinkControlled(
        bool register,
        string manipulatorId = null,
        bool calibrated = true,
        Action onSuccess = null,
        Action<string> onError = null
    )
    {
        // Exit early if this was an invalid call
        switch (register)
        {
            case true when ManipulatorBehaviorController.enabled:
            case true when string.IsNullOrEmpty(manipulatorId):
            case false when !IsEphysLinkControlled:
                return;
        }

        // Set states
        UIUpdateEvent.Invoke();

        if (register)
        {
            IsEphysLinkControlled = true;
            ManipulatorBehaviorController.Initialize(manipulatorId, calibrated);
            onSuccess?.Invoke();
        }
        else
        {
            IsEphysLinkControlled = false;
            onSuccess?.Invoke();
        }
    }

    #endregion

    #region AxisControl

    public void SetAxisVisibility(bool X, bool Y, bool Z, bool depth)
    {
        if (Settings.AxisControl)
        {
            _axisControl.ZAxis.enabled = Z;
            _axisControl.XAxis.enabled = X;
            _axisControl.YAxis.enabled = Y;
            _axisControl.DepthAxis.enabled = depth;
        }
        else
        {
            _axisControl.ZAxis.enabled = false;
            _axisControl.XAxis.enabled = false;
            _axisControl.YAxis.enabled = false;
            _axisControl.DepthAxis.enabled = false;
        }
    }

    public void SetAxisTransform(Transform transform)
    {
        _axisControl.transform.position = transform.position;
        _axisControl.transform.rotation = transform.rotation;
    }

    #endregion AxisControl

    #region Materials

    private void SetMaterials()
    {
        switch (_probeDisplayType)
        {
            case ProbeDisplayType.Opaque:
                SetMaterialsDefault();
                break;
            case ProbeDisplayType.Transparent:
                SetMaterialsTransparent();
                break;
            case ProbeDisplayType.Line:
                SetMaterialsLine();
                break;
        }
    }

    /// <summary>
    /// Set all Renderer components to use the ghost material
    /// </summary>
    private void SetMaterialsTransparent()
    {
#if UNITY_EDITOR
        Debug.Log($"Setting materials for {name} to transparent");
#endif
        if (_lineRenderer != null)
            _lineRenderer.enabled = false;

        var currentColorTint = new Color(Color.r, Color.g, Color.b, .2f);
        foreach (var childRenderer in _activeRenderers)
        {
            childRenderer.enabled = true;
            childRenderer.material = _ghostMaterial;

            // Apply tint to the material
            childRenderer.material.color = currentColorTint;
        }
    }

    /// <summary>
    /// Reverse a previous call to SetMaterialsTransparent()
    /// </summary>
    private void SetMaterialsDefault()
    {
#if UNITY_EDITOR
        Debug.Log($"Setting materials for {name} to default");
#endif
        if (_lineRenderer != null)
            _lineRenderer.enabled = false;

        foreach (var childRenderer in _activeRenderers)
        {
            childRenderer.enabled = true;
            childRenderer.material = _defaultMaterials[childRenderer.gameObject];
        }
    }

    private LineRenderer _lineRenderer;

    private void SetMaterialsLine()
    {
#if UNITY_EDITOR
        Debug.Log($"Setting materials for {name} to line");
#endif
        foreach (var childRenderer in _activeRenderers)
            childRenderer.enabled = false;

        if (_lineRenderer != null)
            _lineRenderer.enabled = true;
        else
        {
            GameObject probeTipGO = _probeController.ProbeTipT.gameObject;
            _lineRenderer = probeTipGO.AddComponent<LineRenderer>();
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            _lineRenderer.startWidth = 0.1f;
            _lineRenderer.endWidth = 0.1f;

            _lineRenderer.material = _lineMaterial;
            _lineRenderer.material.color = Color;

            _lineRenderer.useWorldSpace = false;

            _lineRenderer.positionCount = 2;

            var channelData = GetChannelRangemm();
            _lineRenderer.SetPositions(
                new Vector3[] { Vector3.zero, Vector3.up * channelData.fullHeight }
            );
        }
    }

    #endregion

    #region Static conversion functions
    //public static ProbeData ProbeManager2ProbeData(ProbeManager probeManager)
    //{
    //    ProbeData data = new ProbeData();

    //    data.Insertion = probeManager.ProbeController.Insertion.Data;

    //    data.SelectionLayerName = probeManager.SelectionLayerName;
    //    // [TODO]
    //}
    #endregion
}

[Serializable, Obsolete("Replaced by ProbeData")]
public struct ProbeManagerData
{
    // ProbeInsertion
    public Vector3 APMLDV;
    public Vector3 Angles;

    // CoordinateSpace/Transform
    public string AtlasSpaceName;
    public string AtlasTransformName;

    // ChannelMap
    public string SelectionLayerName;

    // Data
    public int Type;
    public Color Color;
    public string UUID;
    public string Name;

    // API
    public string APITarget;

    // Ephys Link
    public int NumAxes;
    public string ManipulatorID;
    public Vector4 ZeroCoordOffset;
    public Vector3 Dimensions;
    public float BrainSurfaceOffset;
    public bool Drop2SurfaceWithDepth;
    public bool IsRightHanded;

    public static ProbeManagerData ProbeManager2ProbeData(ProbeManager probeManager)
    {
        ProbeManagerData data = new ProbeManagerData();

        data.APMLDV = probeManager.ProbeController.Insertion.APMLDV;
        data.Angles = probeManager.ProbeController.Insertion.Angles;

        data.AtlasSpaceName = probeManager.ProbeController.Insertion.AtlasName;

        // TODO
        //if (probeManager.ProbeController.Insertion.AtlasTransform.Name.Equals("Custom"))
        //    data.CoordTransformName = CoordinateSpaceManager.OriginalTransform.Name;
        //else
        //    data.CoordTransformName = probeManager.ProbeController.Insertion.AtlasTransform.Name;

        data.SelectionLayerName = probeManager.SelectionLayerName;

        data.Type = (int)probeManager.ProbeType;
        data.Color = probeManager.Color;
        data.UUID = probeManager.UUID;
        data.Name = probeManager.name;

        data.APITarget = probeManager.APITarget;

        // Manipulator Behavior data (if it exists)
        if (!probeManager.ManipulatorBehaviorController)
            return data;

        data.NumAxes = probeManager.ManipulatorBehaviorController.NumAxes;
        data.ManipulatorID = probeManager.ManipulatorBehaviorController.ManipulatorID;
        data.ZeroCoordOffset = probeManager.ManipulatorBehaviorController.ReferenceCoordinateOffset;
        data.Dimensions = probeManager.ManipulatorBehaviorController.Dimensions;
        data.BrainSurfaceOffset = probeManager.ManipulatorBehaviorController.BrainSurfaceOffset;
        // data.Drop2SurfaceWithDepth = probeManager
        //     .ManipulatorBehaviorController
        //     .IsSetToDropToSurfaceWithDepth;
        data.IsRightHanded = probeManager.ManipulatorBehaviorController.IsRightHanded;

        return data;
    }
}

public enum ProbeDisplayType
{
    Opaque,
    Transparent,
    Line
}
