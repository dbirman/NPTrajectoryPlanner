using System.Collections.Generic;
using System.Linq;
using EphysLink;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrajectoryPlanner
{
    public class TP_ProbeQuickSettings : MonoBehaviour
    {
        #region Components

        [SerializeField] private TMP_Text probeIdText;
        [SerializeField] private TP_CoordinateEntryPanel coordinatePanel;
        [SerializeField] private CanvasGroup positionFields;
        [SerializeField] private CanvasGroup angleFields;
        [SerializeField] private CanvasGroup buttons;
        [SerializeField] private Toggle automaticMovementToggle;
        [SerializeField] private TMP_InputField automaticMovementSpeedInputField;
        [SerializeField] private GameObject automaticMovementControlPanelGameObject;
        [SerializeField] private Button automaticMovementGoButton;
        
        private ProbeManager _probeManager;
        private CommunicationManager _communicationManager;
        private TrajectoryPlannerManager _trajectoryPlannerManager;
        private TMP_InputField[] _inputFields;

        [SerializeField] private AccountsManager _accountsManager;
        [SerializeField] private TMP_Dropdown _linkedExperimentDropdown;

        private TMP_InputField[] inputFields;

        #endregion

        #region Unity

        /// <summary>
        ///     Initialize components
        /// </summary>
        private void Awake()
        {
            _communicationManager = GameObject.Find("EphysLink").GetComponent<CommunicationManager>();
            _trajectoryPlannerManager = GameObject.Find("main").GetComponent<TrajectoryPlannerManager>();

            _inputFields = gameObject.GetComponentsInChildren<TMP_InputField>(true);

            UpdateInteractable(true);
        }

        /// <summary>
        ///     Update UI components based on external updates
        /// </summary>
        private void FixedUpdate()
        {
            if (!_probeManager) return;
            if (_probeManager.IsConnectedToManipulator() == automaticMovementControlPanelGameObject.activeSelf) return;
            UpdateAutomaticControlPanel();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Set active probe (called by TrajectoryPlannerManager)
        /// </summary>
        /// <param name="probeManager">Probe Manager of active probe</param>
        public void SetProbeManager(ProbeManager probeManager)
        {
            if (probeManager == null)
                gameObject.SetActive(false);
            else
            {
                gameObject.SetActive(true);
                _probeManager = probeManager;

                UpdateProbeIdText();

                coordinatePanel.LinkProbe(probeManager);
                
                UpdateInteractable();
                UpdateCoordinates();
                UpdateAutomaticControlPanel();
            }
        }

        public void UpdateInteractable(bool disableAll=false)
        {
            if (disableAll)
            {
                positionFields.interactable = false;
                angleFields.interactable = false;
                buttons.interactable = false;
            }
            else
            {
                positionFields.interactable = false; // !_probeManager.GetEphysLinkMovement();
                angleFields.interactable = _probeManager == null || !_probeManager.IsGhost();
                buttons.interactable = true;
            }
        }

        public void UpdateCoordinates()
        {
            coordinatePanel.UpdateText();
        }

        public void UpdateProbeIdText()
        {
            probeIdText.text = _probeManager.GetID().ToString();
            probeIdText.color = _probeManager.GetColor();
        }

        private void UpdateAutomaticControlPanel()
        {
            // Check if this probe can be controlled by EphysLink
            if (_probeManager.IsConnectedToManipulator())
            {
                // Show the panel
                automaticMovementControlPanelGameObject.SetActive(true);
                
                // Set enable status (based on if there is a ghost attached or not)
                automaticMovementToggle.SetIsOnWithoutNotify(_probeManager.HasGhost());
                
                // Enable/disable interaction based on if there is a ghost attached or not
                EnableAutomaticControlUI(_probeManager.HasGhost());
                
                // Set value in speed input field
                automaticMovementSpeedInputField.text = _probeManager.GetAutomaticMovementSpeed().ToString();
            }
            else
            {
                // Hide the panel
                automaticMovementControlPanelGameObject.SetActive(false);
            }
        }

        public void EnableAutomaticControlUI(bool enable)
        {
            automaticMovementSpeedInputField.interactable = enable;
            automaticMovementGoButton.interactable = enable;
        }

        /// <summary>
        ///     Move probe to brain surface and zero out depth
        /// </summary>
        public void ZeroDepth()
        {
            _probeManager.SetBrainSurfaceOffset();

            UpdateCoordinates();
        }

        /// <summary>
        ///     Set current manipulator position to be Bregma and move probe to Bregma
        /// </summary>
        public void ResetZeroCoordinate()
        {
            if (_probeManager.IsConnectedToManipulator())
            {
                _communicationManager.GetPos(_probeManager.GetManipulatorId(), _probeManager.SetZeroCoordinateOffset);
                _probeManager.SetBrainSurfaceOffset(0);
            }
            else
            {
                _probeManager.GetProbeController().ResetPosition();
                _probeManager.GetProbeController().SetProbePosition();
            }

            UpdateCoordinates();
        }

        public bool IsFocused()
        {
            return isActiveAndEnabled && _inputFields.Any(inputField => inputField != null && inputField.isFocused);
        }

        /// <summary>
        /// Toggle on or off automatic manipulator control
        /// </summary>
        /// <param name="isOn">Toggle state</param>
        public void ToggleAutomaticControl(bool isOn)
        {
            EnableAutomaticControlUI(isOn);

            if (isOn)
            {
                // Spawn ghost
                var originalProbeManager = _probeManager;
                var ghostProbeManager = _trajectoryPlannerManager.AddNewProbeTransformed(
                    _probeManager.ProbeType, _probeManager.GetProbeController().Insertion, "",
                    _probeManager.GetZeroCoordinateOffset(), _probeManager.GetBrainSurfaceOffset(),
                    _probeManager.IsSetToDropToSurfaceWithDepth(), null, true);
    
                // Configure ghost
                var originalProbeInsertion = originalProbeManager.GetProbeController().Insertion;
    
                ghostProbeManager.SetMaterialsTransparent();
                ghostProbeManager.DisableAllColliders();
    
                // Deep copy overwrite the positions and angles of the insertion
                ghostProbeManager.GetProbeController().SetProbePosition(new ProbeInsertion(originalProbeInsertion.ap,
                    originalProbeInsertion.ml, originalProbeInsertion.dv, originalProbeInsertion.phi, originalProbeInsertion.theta,
                    originalProbeInsertion.spin, originalProbeInsertion.CoordinateSpace, originalProbeInsertion.CoordinateTransform));
                
                // Set references
                originalProbeManager.SetGhostProbeManager(ghostProbeManager);
                ghostProbeManager.SetId(originalProbeManager.GetID());
                ghostProbeManager.name = "GHOST_PROBE_" + originalProbeManager.GetID();
            }
            else
            {
                // Disable UI
                EnableAutomaticControlUI(false);
                // Remove ghost
                _trajectoryPlannerManager.DestroyProbe(_probeManager.GetGhostProbeManager());
                // Remove ghost probe manager reference
                _probeManager.SetGhostProbeManager(null);
            }
        }

        /// <summary>
        /// Automatically limit speed input from text input
        /// </summary>
        /// <param name="input">Value from text input</param>
        public void LimitSpeedInput(string input)
        {
            if (input.Length == 0)
            {
                return;
            }

            switch (int.Parse(input))
            {
                case <= 0:
                    automaticMovementSpeedInputField.SetTextWithoutNotify("1");
                    break;
                case > 8000:
                    automaticMovementSpeedInputField.SetTextWithoutNotify("8000");
                    break;
            }
            
            // Set speed to probe
            _probeManager.SetAutomaticMovementSpeed(int.Parse(automaticMovementSpeedInputField.text));
        }

        /// <summary>
        /// Send position to start automatic driving
        /// </summary>
        public void AutomaticallyDriveManipulator()
        {
            // Gather info
            var apmldv = _probeManager.GetGhostProbeManager().GetProbeController().Insertion.apmldv;
            var depth = _probeManager.GetGhostProbeManager().GetProbeController().GetProbeDepth();

            // Convert apmldv to world coordinate
            var convertToWorld = _probeManager.GetGhostProbeManager().GetProbeController().Insertion
                .Transformed2WorldAxisChange(apmldv);

            // Flip axes to match manipulator
            var posWithDepthAndCorrectAxes = new Vector4(
                -convertToWorld.z,
                convertToWorld.x,
                convertToWorld.y,
                depth);

            // Apply brain surface offset
            var brainSurfaceAdjustment = float.IsNaN(_probeManager.GetBrainSurfaceOffset())
                ? 0
                : _probeManager.GetBrainSurfaceOffset();
            if (_probeManager.IsSetToDropToSurfaceWithDepth())
                posWithDepthAndCorrectAxes.w -= brainSurfaceAdjustment;
            else
                posWithDepthAndCorrectAxes.z -= brainSurfaceAdjustment;

            // Adjust for phi
            var probePhi = _probeManager.GetProbeController().Insertion.phi * Mathf.Deg2Rad;
            var phiCos = Mathf.Cos(probePhi);
            var phiSin = Mathf.Sin(probePhi);
            var phiAdjustedX = posWithDepthAndCorrectAxes.x * phiCos -
                               posWithDepthAndCorrectAxes.y * phiSin;
            var phiAdjustedY = posWithDepthAndCorrectAxes.x * phiSin +
                               posWithDepthAndCorrectAxes.y * phiCos;
            posWithDepthAndCorrectAxes.x = phiAdjustedX;
            posWithDepthAndCorrectAxes.y = phiAdjustedY;
            
            // Apply axis negations
            posWithDepthAndCorrectAxes.z *= -1;
            posWithDepthAndCorrectAxes.y *= _trajectoryPlannerManager.IsManipulatorRightHanded(_probeManager.GetManipulatorId()) ? 1 : -1;

            // Apply coordinate offsets
            var zeroCoordinateOffsetPos = posWithDepthAndCorrectAxes + _probeManager.GetZeroCoordinateOffset();

            // Draw pathway
            var lineObject = new GameObject("AutoControlPath")
            {
                layer = 5
            };
            var lineRenderer = lineObject.AddComponent<LineRenderer>();
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            lineRenderer.startColor = Color.green;
            lineRenderer.endColor = Color.red;
            lineRenderer.startWidth = 0.05f;
            lineRenderer.endWidth = 0.05f;
            lineRenderer.positionCount = 2;
            // Set start position (current position)
            lineRenderer.SetPosition(0, _probeManager.GetProbeController().ProbeTipT.position);
            // Set end position (ghost position)
            lineRenderer.SetPosition(1,
                _probeManager.GetGhostProbeManager().GetProbeController().ProbeTipT.position);

            // Send position to manipulator
            _communicationManager.SetCanWrite(_probeManager.GetManipulatorId(), true, 1, canWrite =>
            {
                if (canWrite)
                    _communicationManager.GotoPos((string)_probeManager.GetManipulatorId(),
                        zeroCoordinateOffsetPos, _probeManager.GetAutomaticMovementSpeed(), endPos => Destroy(lineObject),
                        Debug.LogError);
            }, Debug.LogError);
        }
        public void UpdateExperimentList()
        {
            List<string> experiments = _accountsManager.GetExperiments();
            _linkedExperimentDropdown.ClearOptions();

            List<TMP_Dropdown.OptionData> optList = new List<TMP_Dropdown.OptionData>();
            optList.Add(new TMP_Dropdown.OptionData("Not saved"));
            foreach (string experiment in experiments)
                optList.Add(new TMP_Dropdown.OptionData(experiment));
            _linkedExperimentDropdown.AddOptions(optList);
        }

        public void ChangeExperiment(int optIdx)
        {
            if (optIdx > 0)
            {
                string optText = _linkedExperimentDropdown.options[optIdx].text;
                _accountsManager.ChangeProbeExperiment(_probeManager.UUID, optText);
            }
            else
                _accountsManager.RemoveProbeExperiment(_probeManager.UUID);
        }

        #endregion
    }
}