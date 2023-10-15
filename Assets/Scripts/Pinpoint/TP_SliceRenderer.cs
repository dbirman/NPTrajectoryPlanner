using BrainAtlas;
using System;
using System.Threading.Tasks;
using TMPro;
using TrajectoryPlanner;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Serialization;

public class TP_SliceRenderer : MonoBehaviour
{
    [FormerlySerializedAs("sagittalSliceGO")] [SerializeField] private GameObject _sagittalSliceGo;
    [FormerlySerializedAs("coronalSliceGO")] [SerializeField] private GameObject _coronalSliceGo;
    [FormerlySerializedAs("tpmanager")] [SerializeField] private TrajectoryPlannerManager _tpmanager;
    [FormerlySerializedAs("inPlaneSlice")] [SerializeField] private TP_InPlaneSlice _inPlaneSlice;
    [FormerlySerializedAs("dropdownMenu")] [SerializeField] private TMP_Dropdown _dropdownMenu;

    private bool loaded;

    private bool camXLeft;
    private bool camYBack;

    private Material saggitalSliceMaterial;
    private Material coronalSliceMaterial;

    private Vector3[] _coronalOrigWorldU;
    private Vector3[] _sagittalOrigWorldU;

    private void Awake()
    {
        saggitalSliceMaterial = _sagittalSliceGo.GetComponent<Renderer>().material;
        coronalSliceMaterial = _coronalSliceGo.GetComponent<Renderer>().material;


        loaded = false;
    }

    public void Startup(Texture3D annotationTexture)
    {
        saggitalSliceMaterial.SetTexture("_Volume", annotationTexture);
        coronalSliceMaterial.SetTexture("_Volume", annotationTexture);

        Vector3 dims = BrainAtlasManager.ActiveReferenceAtlas.Dimensions;

        // x = ml
        // y = dv
        // z = ap

        // (dims.y, dims.z, dims.x)

        // -x+y, +x+y, +x-y, -x-y
        _coronalOrigWorldU = new Vector3[] {
                new Vector3(-dims.y, dims.z, 0f),
                new Vector3(dims.y, dims.z, 0f),
                new Vector3(dims.y, -dims.z, 0f),
                new Vector3(-dims.y, -dims.z, 0f)
            };

        // -z+y, +z+y, +z-y, -z-y
        _sagittalOrigWorldU = new Vector3[] {
                new Vector3(0f, dims.z, -dims.x),
                new Vector3(0f, dims.z, dims.x),
                new Vector3(0f, -dims.z, dims.x),
                new Vector3(0f, -dims.z, -dims.x)
            };
    }

    private void Update()
    {
        if (Settings.Slice3DDropdownOption>0 && loaded)
        {
            // Check if the camera moved such that we have to flip the slice quads
            UpdateCameraPosition();
        }
    }

    private float apWorldmm;
    private float mlWorldmm;
    
    /// <summary>
    /// Shift the position of the sagittal and coronal slices to match the tip of the active probe
    /// </summary>
    public void UpdateSlicePosition()
    {
        if (Settings.Slice3DDropdownOption > 0)
        {
            if (ProbeManager.ActiveProbeManager == null) return;
            // Use the un-transformed CCF coordinates to obtain the position in the CCF volume
            (Vector3 tipCoordWorld, _, _, _) = ProbeManager.ActiveProbeManager.ProbeController.GetTipWorldU();

            // vertex order -x-y, +x-y, -x+y, +x+y

            // compute the world vertex positions from the raw coordinates
            // then get the four corners, and warp these according to the active warp
            Vector3[] newCoronalVerts = new Vector3[4];
            Vector3[] newSagittalVerts = new Vector3[4];
            for (int i = 0; i < _coronalOrigWorldU.Length; i++)
            {
                newCoronalVerts[i] = BrainAtlasManager.WorldU2WorldT(new Vector3(_coronalOrigWorldU[i].x, _coronalOrigWorldU[i].y, tipCoordWorld.z));
                newSagittalVerts[i] = BrainAtlasManager.WorldU2WorldT(new Vector3(tipCoordWorld.x, _sagittalOrigWorldU[i].y, _sagittalOrigWorldU[i].z));
            }

            _coronalSliceGo.GetComponent<MeshFilter>().mesh.vertices = newCoronalVerts;
            _sagittalSliceGo.GetComponent<MeshFilter>().mesh.vertices = newSagittalVerts;

            // Use that coordinate to render the actual slice position
            apWorldmm = tipCoordWorld.z + 6.6f;
            coronalSliceMaterial.SetFloat("_SlicePosition", apWorldmm / 13.2f);

            mlWorldmm = -(tipCoordWorld.x - 5.7f);
            saggitalSliceMaterial.SetFloat("_SlicePosition", mlWorldmm / 11.4f);

            UpdateNodeModelSlicing();
        }
    }

    private void UpdateCameraPosition()
    {
        Vector3 camPosition = Camera.main.transform.position;
        bool changed = false;
        if (camXLeft && camPosition.x < 0)
        {
            camXLeft = false;
            changed = true;
        }
        else if (!camXLeft && camPosition.x > 0)
        {
            camXLeft = true;
            changed = true;
        }
        else if (camYBack && camPosition.z < 0)
        {
            camYBack = false;
            changed = true;
        }
        else if (!camYBack && camPosition.z > 0)
        {
            camYBack = true;
            changed = true;
        }
        if (changed)
            UpdateNodeModelSlicing();
    }

    private void UpdateNodeModelSlicing()
    {
        // Update the renderers on the node objects
        throw new NotImplementedException();
        //foreach (CCFTreeNode node in _modelControl.GetDefaultLoadedNodes())
        //{
        //    if (camYBack)
        //        // clip from apPosition forward
        //        node.SetShaderProperty("_APClip", new Vector2(0f, apWorldmm));
        //    else
        //        node.SetShaderProperty("_APClip", new Vector2(apWorldmm, 13.2f));

        //    if (camXLeft)
        //        // clip from mlPosition forward
        //        node.SetShaderProperty("_MLClip", new Vector2(mlWorldmm, 11.4f));
        //    else
        //        node.SetShaderProperty("_MLClip", new Vector2(0f, mlWorldmm));
        //}
    }

    private void ClearNodeModelSlicing()
    {
        // Update the renderers on the node objects
        throw new NotImplementedException();
        //foreach (CCFTreeNode node in _modelControl.GetDefaultLoadedNodes())
        //{
        //    node.SetShaderProperty("_APClip", new Vector2(0f, 13.2f));
        //    node.SetShaderProperty("_MLClip", new Vector2(0f, 11.4f));
        //}
    }

    public void ToggleSliceVisibility(int sliceType)
    {
        if (sliceType==0)
        {
            // make slices invisible
            _sagittalSliceGo.SetActive(false);
            _coronalSliceGo.SetActive(false);
            ClearNodeModelSlicing();
        }
        else
        {
            // Standard sagittal/coronal slices
            _sagittalSliceGo.SetActive(true);
            _coronalSliceGo.SetActive(true);

            //if (sliceType == 1)
            //    SetActiveTextureAnnotation();
            //else if (sliceType == 2)
            //    SetActiveTextureIBLCoverage();

            UpdateSlicePosition();
            UpdateCameraPosition();
        }
    }
}
