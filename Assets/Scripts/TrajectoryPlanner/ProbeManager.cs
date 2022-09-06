using System;
using System.Collections.Generic;
using SensapexLink;
using TMPro;
using TrajectoryPlanner;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 3D space control for Neuropixels probes in the Trajectory Planner scene
/// </summary>
public class ProbeManager : MonoBehaviour
{
    // Internal flags that track whether we are in manual control or drag/link control mode
    private bool _sensapexLinkMovement;

    #region Sensapex Link

    private CommunicationManager _sensapexLinkCommunicationManager;
    private int _manipulatorId;
    private Vector3 _probeAngles;
    private float _phiCos = 1f;
    private float _phiSin;
    private Vector4 _zeroCoordinateOffset = Vector4.negativeInfinity;
    private float _brainSurfaceOffset;
    private bool _dropToSurfaceWithDepth = true;
    private Vector4 _lastManipulatorPosition = Vector4.negativeInfinity;

    #endregion

    // Exposed fields to collect links to other components inside of the Probe prefab
    [SerializeField] private List<Collider> probeColliders;
    [SerializeField] private List<ProbeUIManager> probeUIManagers;
    [SerializeField] private GameObject textPrefab;
    [SerializeField] private Renderer probeRenderer;
    [SerializeField] private int probeType;

    [SerializeField] private ProbeController probeController;

    [SerializeField] private Material ghostMaterial;
    private Dictionary<GameObject, Material> defaultMaterials;

    private TrajectoryPlannerManager tpmanager;

    // Text
    private int probeID;
    private TextMeshProUGUI textUI;
    private const float minPhi = -180;
    private const float maxPhi = 180f;
    private const float minSpin = -180f;
    private const float maxSpin = 180f;

    // Brain surface position
    private AnnotationDataset annotationDataset;
    private bool probeInBrain;
    private Vector3 brainSurfaceWorld;

    // Colliders
    private List<GameObject> visibleProbeColliders;
    private Dictionary<GameObject, Material> visibleOtherColliders;

    // Text button
    GameObject textGO;
    Button textButton;

    #region Accessors

    /// <summary>
    /// Get the probe-type of this probe
    /// </summary>
    /// <returns>probe type</returns>
    public int GetProbeType()
    {
        return probeType;
    }

    /// <summary>
    /// Get the tip transform
    /// </summary>
    /// <returns>tip transform</returns>
    public Transform GetTipTransform()
    {
        return probeController.GetTipTransform();
    }
    public int GetID()
    {
        return probeID;
    }

    public Color GetColor()
    {
        return probeRenderer.material.color;
    }
    public List<Collider> GetProbeColliders()
    {
        return probeColliders;
    }

    /// <summary>
    /// Return the probe panel UI managers
    /// </summary>
    /// <returns>list of probe panel UI managers</returns>
    public List<ProbeUIManager> GetProbeUIManagers()
    {
        return probeUIManagers;
    }

    #endregion

    #region Unity

    private void Awake()
    {
        // Setup some basic variables
        textGO = Instantiate(textPrefab, GameObject.Find("CoordinatePanel").transform);
        textButton = textGO.GetComponent<Button>();
        textButton.onClick.AddListener(Probe2Text);
        textUI = textGO.GetComponent<TextMeshProUGUI>();
        defaultMaterials = new Dictionary<GameObject, Material>();

        // Pull the tpmanager object and register this probe
        GameObject main = GameObject.Find("main");
        tpmanager = main.GetComponent<TrajectoryPlannerManager>();
        tpmanager.RegisterProbe(this);
        probeController.Register(tpmanager, this);

        // Pull sensapex link communication manager
        _sensapexLinkCommunicationManager = GameObject.Find("SensapexLink").GetComponent<CommunicationManager>();

        // Get access to the annotation dataset and world-space boundaries
        annotationDataset = tpmanager.GetAnnotationDataset();

        visibleProbeColliders = new List<GameObject>();
        visibleOtherColliders = new Dictionary<GameObject, Material>();
    }

    /// <summary>
    /// Called by Unity when this object is destroyed. 
    /// Unregisters the probe from tpmanager
    /// Removes the probe panels and the position text.
    /// </summary>
    public void Destroy()
    {
        // Delete this gameObject
        foreach (ProbeUIManager puimanager in probeUIManagers)
            puimanager.Destroy();
        Destroy(textGO);
        
        // Unregister this probe from the sensapex link
        if (IsConnectedToManipulator())
        {
            SetSensapexLinkMovement(false, 0);
        }
    }

    #endregion

    public void UpdateUI()
    {
        // Reset our probe UI panels
        foreach (ProbeUIManager puimanager in probeUIManagers)
            puimanager.ProbeMoved();
    }

    public void SetUIVisibility(bool state)
    {
        foreach (ProbeUIManager puimanager in probeUIManagers)
            puimanager.SetProbePanelVisibility(state);
    }


    /// <summary>
    /// Update the size of the recording region.
    /// </summary>
    /// <param name="newSize">New size of recording region in mm</param>
    public void ChangeRecordingRegionSize(float newSize)
    {
        probeController.ChangeRecordingRegionSize(newSize);

        // Update all the UI panels
        UpdateUI();
    }


    /// <summary>
    /// Move the probe with the option to check for collisions
    /// </summary>
    /// <param name="checkForCollisions">Set to true to check for collisions with rig colliders and other probes</param>
    /// <returns>Whether or not the probe moved on this frame</returns>
    public bool MoveProbe(bool checkForCollisions = false)
    {
        // Cancel movement if being controlled by SensapexLink
        if (_sensapexLinkMovement)
            return false;

        return probeController.MoveProbe_Keyboard(checkForCollisions);
    }

    /// <summary>
    /// Return the current size of the recording region
    /// </summary>
    /// <returns>size of the recording region</returns>
    public float GetRecordingRegionSize()
    {
        return probeController.GetRecordingRegionSize();
    }

    /// <summary>
    /// Return the mm position of the bottom of the recording region and the height
    /// </summary>
    /// <returns>float array [0]=bottom, [1]=height</returns>
    public float[] GetRecordingRegionHeight()
    {
        return probeController.GetRecordingRegionHeight();
    }


    /// <summary>
    /// Get the coordinates of the current probe (tip/angles) in mm or um, depending on the current IBL state
    /// </summary>
    /// <returns>(ap, ml, dv, phi, theta, spin)</returns>
    public (float, float, float, float, float, float) GetCoordinates()
    {
        return probeController.GetCoordinates();
    }
    public (float, float, float, float, float, float, float) GetCoordinatesSurface()
    {
        return probeController.GetCoordinatesSurface(probeInBrain, brainSurfaceWorld);
    }

    public ProbeInsertion GetInsertion()
    {
        return probeController.GetInsertion();
    }


    /// <summary>
    /// Check for collisions between the probe colliders and a list of other colliders
    /// </summary>
    /// <param name="otherColliders">colliders to check against</param>
    /// <returns></returns>
    public void CheckCollisions(List<Collider> otherColliders)
    {
        if (tpmanager.GetCollisions())
        {
            bool collided = CheckCollisionsHelper(otherColliders);

            if (collided)
                tpmanager.SetCollisionPanelVisibility(true);
            else
            {
                tpmanager.SetCollisionPanelVisibility(false);
                ClearCollisionMesh();
            }
        }
        else
        {
            tpmanager.SetCollisionPanelVisibility(false);
            ClearCollisionMesh();
        }
    }

    /// <summary>
    /// Internal function to perform collision checks between Collider components
    /// </summary>
    /// <param name="otherColliders"></param>
    /// <returns></returns>
    private bool CheckCollisionsHelper(List<Collider> otherColliders)
    {
        foreach (Collider activeCollider in probeColliders)
        {
            foreach (Collider otherCollider in otherColliders)
            {
                Vector3 dir;
                float dist;
                if (Physics.ComputePenetration(activeCollider, activeCollider.transform.position, activeCollider.transform.rotation, otherCollider, otherCollider.transform.position, otherCollider.transform.rotation, out dir, out dist))
                {
                    CreateCollisionMesh(activeCollider, otherCollider);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// When collisions occur we want to make the colliders we hit change material, but we might need to later swap them back
    /// </summary>
    /// <param name="activeCollider"></param>
    /// <param name="otherCollider"></param>
    private void CreateCollisionMesh(Collider activeCollider, Collider otherCollider)
    {
        if (!visibleProbeColliders.Contains(activeCollider.gameObject))
        {
            visibleProbeColliders.Add(activeCollider.gameObject);
            activeCollider.gameObject.GetComponent<Renderer>().enabled = true;
        }

        GameObject otherColliderGO = otherCollider.gameObject;
        if (!visibleOtherColliders.ContainsKey(otherColliderGO))
        {
            visibleOtherColliders.Add(otherColliderGO, otherColliderGO.GetComponent<Renderer>().material);
            otherColliderGO.GetComponent<Renderer>().material = tpmanager.GetCollisionMaterial();
        }
    }

    // Clear probe colliders by disabling the renderers and then clear the other colliders by swapping back their materials
    private void ClearCollisionMesh()
    {
        if (visibleProbeColliders.Count > 0 || visibleOtherColliders.Count > 0)
        {
            foreach (GameObject probeColliderGO in visibleProbeColliders)
                probeColliderGO.GetComponent<Renderer>().enabled = false;
            foreach (KeyValuePair<GameObject, Material> kvp in visibleOtherColliders)
                kvp.Key.GetComponent<Renderer>().material = kvp.Value;

            visibleProbeColliders.Clear();
            visibleOtherColliders.Clear();
        }
    }

    #region Text

    public void UpdateText()
    {
        float localDepth = GetLocalDepth();
        Vector3 apmldv = GetInsertionCoordinateTransformed();
        string[] apml_string = GetAPMLStr();

        (float ap, float ml, float dv, float phi, float theta, float spin) = tpmanager.GetSetting_UseIBLAngles() ?
            probeController.GetInsertion().GetCoordinatesFloat_IBL() :
            probeController.GetInsertion().GetCoordinatesFloat();

        string updateStr = string.Format("Probe #{0}: " + apml_string[0] + " {1} " + 
            apml_string[1] + " {2} " +
            apml_string[2] + " {3} Azimuth {4} Elevation {5} " + GetDepthStr() + " {6} Spin {7}",
            probeID, round0(apmldv.x * 1000), round0(apmldv.y * 1000), round0(apmldv.z * 1000), round2(Utils.CircDeg(phi, minPhi, maxPhi)), round2(theta), round0(localDepth * 1000), round2(Utils.CircDeg(spin, minSpin, maxSpin))); ;

        textUI.text = updateStr;
    }

    private void Probe2Text()
    {
        Debug.LogWarning("Text not setup right now");
        //float localDepth = GetLocalDepth();
        //Vector2 apml_local = GetTransformedTipCoord();
        //string[] apml_string = GetAPMLStr();

        //(float ap, float ml, float dv, float depth, float phi, float theta, float spin) = tpmanager.UseIBLAngles() ?
        //    insertion.GetCoordinatesFloat_IBL() :
        //    insertion.GetCoordinatesFloat();

        //string fullStr = string.Format("Probe #{0}: " + apml_string[0] + " {1} " + apml_string[1] + " {2} Azimuth {3} Elevation {4} "+ GetDepthStr()+" {5} Spin {6} Record Height {7}",
        //    probeID, apml_local.x * 1000, apml_local.y * 1000, Utils.CircDeg(phi,minPhi, maxPhi), theta, localDepth, Utils.CircDeg(spin,minSpin,maxSpin), minRecordHeight * 1000);
        //GUIUtility.systemCopyBuffer = fullStr;

        // When you copy text, also set this probe to be active
        tpmanager.SetActiveProbe(this);
    }

    public float[] Text2Probe()
    {
        float[] output = new float[7];

        // Parse the text string and re-build the probe variables. 

        return output;
    }

    private string[] GetAPMLStr()
    {
        if (tpmanager.GetSetting_InVivoTransformActive())
        {
            string prefix = tpmanager.GetInVivoPrefix();
            if (tpmanager.GetSetting_ConvertAPMLAxis2Probe())
            {
                return new string[] { prefix + "Forward", prefix + "Side", prefix + "DV" };
            }
            else
            {
                return new string[] { prefix + "AP", prefix + "ML", prefix + "DV" };
            }
        }
        else
        {
            if (tpmanager.GetSetting_ConvertAPMLAxis2Probe())
            {
                return new string[] { "ccfForward", "ccfSide", "ccfDV" };
            }
            else
            {
                return new string[] { "ccfAP", "ccfML", "ccfDV" };
            }
        }
    }

    private string GetDepthStr()
    {
        if (tpmanager.GetSetting_InVivoTransformActive())
            return tpmanager.GetInVivoPrefix() + "Depth";
        else
            return "ccfDepth";
    }

    #endregion

    /// <summary>
    /// Returns the coordinate that a user should target to insert a probe into the brain.
    /// If the probe is outside the brain we return the tip position
    /// Once the probe is in the brain we return the brain surface position
    /// </summary>
    /// <returns></returns>
    private Vector3 GetInsertionCoordinateTransformed()
    {
        Vector3 insertionCoord = probeInBrain ? Utils.world2apmldv(brainSurfaceWorld + tpmanager.GetCenterOffset()) : probeController.GetInsertion().apmldv;
        
        // If we're in a transformed space we need to transform the coordinates
        // before we do anything else.
        if (tpmanager.GetSetting_InVivoTransformActive())
            insertionCoord = tpmanager.CoordinateTransformFromCCF(insertionCoord);

        // We can rotate the ap/ml position now to account for off-coronal/sagittal manipulator angles
        if (tpmanager.GetSetting_ConvertAPMLAxis2Probe())
        {
            // convert to probe angle by solving 
            float localAngleRad = probeController.GetInsertion().phi * Mathf.PI / 180f; // our phi is 0 when it we point forward, and our angles go backwards

            float x = insertionCoord.x * Mathf.Cos(localAngleRad) + insertionCoord.y * Mathf.Sin(localAngleRad);
            float y = -insertionCoord.x * Mathf.Sin(localAngleRad) + insertionCoord.y * Mathf.Cos(localAngleRad);
            return new Vector3(x, y, insertionCoord.z);
        }
        else
        {
            // just return apml
            return insertionCoord;
        }
    }

    private float GetLocalDepth()
    {
        if (probeInBrain)
        {
            Transform probeTipT = probeController.GetTipTransform();
            // Get the direction
            float dir = Mathf.Sign(Vector3.Dot(probeTipT.position - brainSurfaceWorld, -probeTipT.up));
            // Get the distance
            float distance = (tpmanager.GetSetting_InVivoTransformActive()) ?
                Vector3.Distance(tpmanager.CoordinateTransformFromCCF(probeTipT.position), tpmanager.CoordinateTransformFromCCF(brainSurfaceWorld)) :
                Vector3.Distance(probeTipT.position, brainSurfaceWorld);

            return dir * distance;
        }
        // If the probe is not in the brain, return NaN
        return float.NaN;
    }

    public void RegisterProbeCallback(int ID, Color probeColor)
    {
        probeID = ID;
        name = "PROBE_" + probeID;
        probeRenderer.material.color = probeColor;

        var colors = textButton.colors;
        colors.highlightedColor = probeColor;
        Color probeColorTransparent = probeColor;
        probeColorTransparent.a = 0.75f;
        colors.selectedColor = probeColorTransparent;
        colors.pressedColor = probeColorTransparent;
        textButton.colors = colors;
    }


    private float round0(float input)
    {
        return Mathf.Round(input);
    }
    private float round2(float input)
    {
        return Mathf.Round(input * 100) / 100;
    }



    public (Vector3, Vector3) GetRecordingRegionCoordinatesAPDVLR()
    {
        return probeController.GetRecordingRegionCoordinatesAPDVLR();
    }

    /// <summary>
    /// Re-scale probe panels 
    /// </summary>
    /// <param name="newPxHeight">Set the probe panels of this probe to a new height</param>
    public void ResizeProbePanel(int newPxHeight)
    {
        foreach (ProbeUIManager puimanager in probeUIManagers)
        {
            puimanager.ResizeProbePanel(newPxHeight);
            puimanager.ProbeMoved();
        }
    }

    /// <summary>
    /// Check whether the probe is in the brain.
    /// If it is, calculate the brain surface coordinate by iterating up the probe until you leave the brain.
    /// </summary>
    public void UpdateSurfacePosition()
    {
        (Vector3 surfacePosition, float depth, Vector3 angles) = CCF2Surface(probeController.GetTipTransform().position, probeController.GetInsertion().angles);

        brainSurfaceWorld = surfacePosition;


        if (float.IsNaN(depth))
        {
            // not in the brain
            probeInBrain = false;
            // these debugs are really bad coding style -- tpmanager should *get* the position and set these, it shouldn't be called from here
            tpmanager.SetSurfaceDebugActive(false);
        }
        else
        {
            // in the brain
            probeInBrain = true;
            // these debugs are really bad coding style -- tpmanager should *get* the position and set these, it shouldn't be called from here
            tpmanager.SetSurfaceDebugActive(true);
            tpmanager.SetSurfaceDebugPosition(brainSurfaceWorld);
        }
    }

    /// <summary>
    /// to implement 
    /// </summary>
    public void LockProbeToArea()
    {

    }

    #region Transforms

    /// <summary>
    /// Convert from CCF insertion coordinates to brain surface/depth/angles coordinates
    /// 
    /// If the entire probe is outside the brain this function returns the tip coordinate as the surface, a depth of NaN, and identical angles
    /// 
    /// This function is quite expensive to run!
    /// </summary>
    /// <param name="tipPosition"></param>
    /// <param name="angles"></param>
    /// <param name="useDepth">Determine which direction to seek brain surface (travel along depth or DV). Defaults to depth</param>
    /// <returns></returns>
    public (Vector3, float, Vector3) CCF2Surface(Vector3 tipPosition, Vector3 angles, bool useDepth = true)
    {
        Vector3 tip_apdvlr25 = Utils.WorldSpace2apdvlr25(tipPosition);

        bool crossedThroughBrain = annotationDataset.ValueAtIndex(tip_apdvlr25) > 0;

        // Iterate up until you exit the brain
        // if you started outside, first find when you enter
        Transform probeTipT = probeController.GetTipTransform();
        var top = Utils.WorldSpace2apdvlr25(probeTipT.position + (useDepth ? probeTipT.up : Vector3.up) * 10f);
        for (float perc = 0; perc <= 1f; perc += 0.0005f) 
        {
            Vector3 point = Vector3.Lerp(tip_apdvlr25, top, perc);
            if (crossedThroughBrain)
            {
                if (annotationDataset.ValueAtIndex(point) <= 0)
                {
                    Vector3 surfacePosition = Utils.apdvlr25_2World(point);
                    return (surfacePosition, Vector3.Distance(tipPosition, surfacePosition), angles);
                }
            }
            else
            {
                if (annotationDataset.ValueAtIndex(point) > 0)
                    crossedThroughBrain = true;
            }
        }

        // If you got here it means you *never* crossed through the brain
        return (tipPosition, float.NaN, angles);
    }


    #endregion

    #region Sensapex Link and Control

    #region Property Getters and Setters

    /// <summary>
    /// Return if this probe is being controlled by the Sensapex Link
    /// </summary>
    /// <returns>True if movement is controlled by Sensapex Link, False otherwise</returns>
    public bool GetSensapexLinkMovement()
    {
        return _sensapexLinkMovement;
    }

    public ProbeController GetProbeController()
    {
        return probeController;
    }

    /// <summary>
    /// (un)Register a probe and begin echoing position
    /// </summary>
    /// <param name="register">To register or deregister this probe</param>
    /// <param name="manipulatorId">ID of the manipulator in real life to connect to</param>
    /// <param name="calibrated">Is the manipulator in real life calibrated</param>
    /// <param name="onSuccess">Callback function to handle a successful registration</param>
    /// <param name="onError">Callback function to handle a failed registration</param>
    public void SetSensapexLinkMovement(bool register, int manipulatorId = 0, bool calibrated = true,
        Action onSuccess = null, Action<string> onError = null)
    {
        // Exit early if this was an invalid call
        switch (register)
        {
            case true when IsConnectedToManipulator():
            case true when manipulatorId == 0:
                return;
        }

        // Set states
        _sensapexLinkMovement = register;
        tpmanager.UpdateQuickSettings();

        if (register)
            _sensapexLinkCommunicationManager.RegisterManipulator(manipulatorId, () =>
            {
                Debug.Log("Manipulator Registered");
                _manipulatorId = manipulatorId;

                if (calibrated)
                    // Bypass calibration and start echoing
                    _sensapexLinkCommunicationManager.BypassCalibration(manipulatorId, StartEchoing);
                else
                    // Enable write
                    _sensapexLinkCommunicationManager.SetCanWrite(manipulatorId, true, 1,
                        _ =>
                        {
                            // Calibrate
                            _sensapexLinkCommunicationManager.Calibrate(manipulatorId,
                                () =>
                                {
                                    // Disable write and start echoing
                                    _sensapexLinkCommunicationManager.SetCanWrite(manipulatorId, false, 0,
                                        _ => onSuccess?.Invoke());
                                });
                        });

                onSuccess?.Invoke();
            }, err => onError?.Invoke(err));
        else
            _sensapexLinkCommunicationManager.UnregisterManipulator(_manipulatorId, () =>
            {
                Debug.Log("Manipulator Unregistered");
                ResetManipulatorProperties();
                onSuccess?.Invoke();
            }, err => onError?.Invoke(err));

        // Start echoing process
        void StartEchoing()
        {
            // Read and start echoing position
            _sensapexLinkCommunicationManager.GetPos(manipulatorId, vector4 =>
            {
                if (_zeroCoordinateOffset.Equals(Vector4.negativeInfinity)) _zeroCoordinateOffset = vector4;
                EchoPositionFromSensapexLink(vector4);
            });
        }
    }

    /// <summary>
    ///     Get attached manipulator ID
    /// </summary>
    /// <returns>Attached manipulator ID, 0 if none are attached</returns>
    public int GetManipulatorId()
    {
        return _manipulatorId;
    }

    /// <summary>
    ///     Set manipulator properties such as ID and positional offsets back to defaults
    /// </summary>
    public void ResetManipulatorProperties()
    {
        _manipulatorId = 0;
        _zeroCoordinateOffset = Vector4.negativeInfinity;
        _brainSurfaceOffset = 0;
    }

    /// <summary>
    ///     Return if this probe is being controlled by the Sensapex Link
    /// </summary>
    /// <returns>True if this probe is attached to a manipulator, false otherwise</returns>
    public bool IsConnectedToManipulator()
    {
        return _manipulatorId != 0;
    }

    /// <summary>
    ///     Probe angles as phi, theta, spin
    /// </summary>
    /// <returns>Angles in degrees in phi, theta, spin order</returns>
    public Vector3 GetProbeAngles()
    {
        return _probeAngles;
    }

    /// <summary>
    ///     Set probe angles in phi, theta, spin order
    /// </summary>
    /// <param name="angles">Angles in degrees in phi, theta, spin order</param>
    public void SetProbeAngles(Vector3 angles)
    {
        _probeAngles = angles;
        _phiCos = Mathf.Cos(_probeAngles.x * Mathf.Deg2Rad);
        _phiSin = Mathf.Sin(_probeAngles.x * Mathf.Deg2Rad);
    }

    /// <summary>
    ///     Manipulator space offset to zero coordinate as X, Y, Z, Depth
    /// </summary>
    /// <returns>Manipulator space offset to zero coordinate as X, Y, Z, Depth</returns>
    public Vector4 GetZeroCoordinateOffset()
    {
        return _zeroCoordinateOffset;
    }

    /// <summary>
    ///     Set manipulator space offset to zero coordinate as X, Y, Z, Depth
    /// </summary>
    /// <param name="zeroCoordinateOffset">Offset from zero coordinate as X, Y, Z, Depth</param>
    public void SetZeroCoordinateOffset(Vector4 zeroCoordinateOffset)
    {
        _zeroCoordinateOffset = zeroCoordinateOffset;
    }

    /// <summary>
    ///     Update x coordinate of manipulator space offset to zero coordinate
    /// </summary>
    /// <param name="x">X coordinate</param>
    public void SetZeroCoordinateOffsetX(float x)
    {
        _zeroCoordinateOffset.x = x;
    }

    /// <summary>
    ///     Update y coordinate of manipulator space offset to zero coordinate
    /// </summary>
    /// <param name="y">Y coordinate</param>
    public void SetZeroCoordinateOffsetY(float y)
    {
        _zeroCoordinateOffset.y = y;
    }


    /// <summary>
    ///     Update Z coordinate of manipulator space offset to zero coordinate
    /// </summary>
    /// <param name="z">Z coordinate</param>
    public void SetZeroCoordinateOffsetZ(float z)
    {
        _zeroCoordinateOffset.z = z;
    }


    /// <summary>
    ///     Update D coordinate of manipulator space offset to zero coordinate
    /// </summary>
    /// <param name="depth">D coordinate</param>
    public void SetZeroCoordinateOffsetDepth(float depth)
    {
        _zeroCoordinateOffset.w = depth;
    }

    /// <summary>
    /// Get manipulator space offset from brain surface as Depth
    /// </summary>
    /// <returns>Manipulator space offset to brain surface</returns>
    public float GetBrainSurfaceOffset()
    {
        return _brainSurfaceOffset;
    }
    
    /// <summary>
    ///     Set manipulator space offset from brain surface as Depth from input
    /// </summary>
    /// <param name="brainSurfaceOffset">Offset from brain surface as Depth</param>
    public void SetBrainSurfaceOffset(float brainSurfaceOffset)
    {
        _brainSurfaceOffset = brainSurfaceOffset;
    }

    /// <summary>
    ///     Set manipulator space offset from brain surface as Depth from manipulator or probe coordinates
    /// </summary>
    public void SetBrainSurfaceOffset()
    {
        var tipExtensionDirection = _dropToSurfaceWithDepth ? probeController.GetTipTransform().up : Vector3.up;
        var brainSurface = CCF2Surface(probeController.GetTipTransform().position - tipExtensionDirection * 5, _probeAngles,
            _dropToSurfaceWithDepth);

        if (IsConnectedToManipulator())
        {
            _brainSurfaceOffset -= (brainSurface.Item2 - 5) * 1000;
        }
        else
        {
            Debug.DrawLine(probeController.GetTipTransform().position, brainSurface.Item1, Color.red, 5);
            probeController.SetProbePositionCCF(new ProbeInsertion(Utils.world2apmldv(brainSurface.Item1), _probeAngles));
        }
    }

    /// <summary>
    ///     Manual adjustment of brain surface offset
    /// </summary>
    /// <param name="increment">Amount to change the brain surface offset by</param>
    public void IncrementBrainSurfaceOffset(float increment)
    {
        _brainSurfaceOffset += increment;
    }

    /// <summary>
    ///     Manually edit brain surface offset
    /// </summary>
    /// <param name="offset">Amount to offset by</param>
    public void SetBrainSurfaceOffsetManually(float offset)
    {
        _brainSurfaceOffset = offset;
    }

    /// <summary>
    ///     Set if the probe should be dropped to the surface with depth or with DV
    /// </summary>
    /// <param name="dropToSurfaceWithDepth">Use depth if true, use DV if false</param>
    public void SetDropToSurfaceWithDepth(bool dropToSurfaceWithDepth)
    {
        _dropToSurfaceWithDepth = dropToSurfaceWithDepth;
    }

    /// <summary>
    ///     Return if this probe is currently set to drop to the surface using depth
    /// </summary>
    /// <returns>True if dropping to surface via depth, false if using DV</returns>
    public bool IsSetToDropToSurfaceWithDepth()
    {
        return _dropToSurfaceWithDepth;
    }

    #endregion

    #region Actions

    /// <summary>
    ///     Echo given position in needles transform space to the probe
    /// </summary>
    /// <param name="pos">Position of manipulator in needles transform</param>
    public void EchoPositionFromSensapexLink(Vector4 pos)
    {
        /*
         * Left-handed manipulator movement
         * +x = L
         * +Y = A
         * +Z = V
         */
        
        // Convert position to CCF
        var zeroCoordinateAdjustedPosition = pos - _zeroCoordinateOffset;
        
        // Phi adjustment
        var phiAdjustedX = zeroCoordinateAdjustedPosition.x * _phiCos -
                           zeroCoordinateAdjustedPosition.y * _phiSin;
        var phiAdjustedY = zeroCoordinateAdjustedPosition.x * _phiSin +
                           zeroCoordinateAdjustedPosition.y * _phiCos;
        zeroCoordinateAdjustedPosition.x = phiAdjustedX;
        zeroCoordinateAdjustedPosition.y = phiAdjustedY;

        // Calculate last used direction (between depth and DV)
        var dvDelta = Math.Abs(zeroCoordinateAdjustedPosition.z - _lastManipulatorPosition.z);
        var depthDelta = Math.Abs(zeroCoordinateAdjustedPosition.w - _lastManipulatorPosition.w);
        if (dvDelta > 0.1 || depthDelta > 0.1) _dropToSurfaceWithDepth = depthDelta >= dvDelta;
        _lastManipulatorPosition = zeroCoordinateAdjustedPosition;
        
        // Brain surface adjustment
        var brainSurfaceAdjustment = float.IsNaN(_brainSurfaceOffset) ? 0 : _brainSurfaceOffset;
        if (_dropToSurfaceWithDepth)
            zeroCoordinateAdjustedPosition.w += brainSurfaceAdjustment;
        else
            zeroCoordinateAdjustedPosition.z += brainSurfaceAdjustment;

        // Swap axes to match AP/ML/DV order and adjust for handedness
        var positionAxisSwapped = new Vector3(
            zeroCoordinateAdjustedPosition.y * (tpmanager.IsManipulatorRightHanded(_manipulatorId) ? -1 : 1),
            -zeroCoordinateAdjustedPosition.x,
            -zeroCoordinateAdjustedPosition.z);

        // Drive normally when not moving depth, otherwise use surface coordinates
        probeController.ManualCoordinateEntryTransformed(positionAxisSwapped, _probeAngles,
            zeroCoordinateAdjustedPosition.w / 1000f);


        // Continue echoing position
        if (_sensapexLinkMovement)
            _sensapexLinkCommunicationManager.GetPos(_manipulatorId, EchoPositionFromSensapexLink);
    }

    #endregion

    #endregion

    #region AxisControl

    public void SetAxisVisibility(bool AP, bool ML, bool DV, bool depth)
    {
        Transform tipT = probeController.GetTipTransform();
        tpmanager.SetAxisVisibility(AP, ML, DV, depth, tipT);
    }

    #endregion AxisControl

    #region Materials

    /// <summary>
    /// Set all Renderer components to use the ghost material
    /// </summary>
    public void SetMaterialsTransparent()
    {
        defaultMaterials.Clear();
        foreach (Renderer renderer in transform.GetComponentsInChildren<Renderer>())
        {
            defaultMaterials.Add(renderer.gameObject, renderer.material);
            renderer.material = ghostMaterial;
        }
    }

    /// <summary>
    /// Reverse a previous call to SetMaterialsTransparent()
    /// </summary>
    public void SetMaterialsDefault()
    {
        foreach (Renderer renderer in transform.GetComponentsInChildren<Renderer>())
            if (defaultMaterials.ContainsKey(renderer.gameObject))
                renderer.material = defaultMaterials[renderer.gameObject];
    }

    #endregion
}
