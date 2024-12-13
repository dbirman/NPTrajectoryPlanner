using System.Linq;
using EphysLink;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace TrajectoryPlanner
{
    /// <summary>
    /// Handles the Probe Data panel UI
    /// 
    /// Interfaces with the probeID, coordinate data, position/angle fields
    /// </summary>
    public class TP_ProbeQuickSettings : MonoBehaviour
    {
        #region Components

        [FormerlySerializedAs("probeIdText")] [SerializeField] private TMP_Text _probeIdText;
        [FormerlySerializedAs("coordinatePanel")] [SerializeField] private CoordinateEntryPanel _coordinatePanel;
        [SerializeField] private QuickSettingsLockBehavior _lockBehavior;
        [SerializeField] private RawImage _colorChooser;

        #endregion

        #region Private vars
        private CommunicationManager _communicationManager;
        private TMP_InputField[] _inputFields;
        private bool _dirty;
        #endregion

        #region Unity

        /// <summary>
        ///     Initialize components
        /// </summary>
        private void Awake()
        {
            _communicationManager = GameObject.Find("EphysLink").GetComponent<CommunicationManager>();
            _inputFields = gameObject.GetComponentsInChildren<TMP_InputField>(true);

            ProbeManager.ActiveProbeUIUpdateEvent.AddListener(SetUIDirty);

            UpdateInteractable(true);
        }

        private void LateUpdate()
        {
            if (_dirty)
            {
                _dirty = false;
                UpdateQuickUI();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Set active probe (called by TrajectoryPlannerManager)
        /// </summary>
        /// <param name="probeManager">Probe Manager of active probe</param>
        public void SetActiveProbeManager()
        {
            if (ProbeManager.ActiveProbeManager == null)
            {
                gameObject.SetActive(false);
            }
            else
            {
                gameObject.SetActive(true);

                _coordinatePanel.UpdateAxisLabels();

                UpdateQuickUI();

                // Handle picking up events
                ProbeManager.ActiveProbeManager.EphysLinkControlChangeEvent.AddListener(() =>
                {
                    UpdateInteractable();
                });
            }
        }

        public void UpdateInteractable(bool disableAll=false)
        {
            if (disableAll || ProbeManager.ActiveProbeManager == null)
            {
                _coordinatePanel.UpdateInteractable(Vector4.zero, Vector3.zero);
            }
            else
                _coordinatePanel.UpdateInteractable(ProbeManager.ActiveProbeManager.ProbeController.UnlockedDir,
                    ProbeManager.ActiveProbeManager.ProbeController.UnlockedRot);
        }

        public void UpdateCoordinates()
        {
            _coordinatePanel.UpdateText();
        }

        /// <summary>
        /// Calls UpdateQuickUI a maximum of once per frame (in LateUpdate)
        /// </summary>
        public void SetUIDirty()
        {
            _dirty = true;
        }

        /// <summary>
        /// Update the probe panel UI
        /// </summary>
        public void UpdateQuickUI()
        {
            if (ProbeManager.ActiveProbeManager != null)
            {
                _probeIdText.text = ProbeManager.ActiveProbeManager.name;
                _probeIdText.color = ProbeManager.ActiveProbeManager.Color;
                SetColorChooserColor(ProbeManager.ActiveProbeManager.Color);
                _lockBehavior.UpdateSprite(ProbeManager.ActiveProbeManager.ProbeController.Locked);
            }
            else
            {
                _probeIdText.text = "";
            }

            UpdateCoordinates();
            UpdateInteractable();
        }

        /// <summary>
        ///     Move probe to brain surface and zero out depth
        /// </summary>
        public void ZeroDepth()
        {
            if (ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.enabled)
                ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.ComputeBrainSurfaceOffset();
            else
                ProbeManager.ActiveProbeManager.DropProbeToBrainSurface();

            UpdateCoordinates();
        }

        /// <summary>
        ///     Set current manipulator position to be Bregma and move probe to Bregma
        /// </summary>
        public void ResetZeroCoordinate()
        {
            if (ProbeManager.ActiveProbeManager.IsEphysLinkControlled)
            {
                _communicationManager.GetPosition(ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.ManipulatorID,
                    zeroCoordinate => ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.ReferenceCoordinateOffset = zeroCoordinate);
                ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.BrainSurfaceOffset = 0;
            }
            else
            {
                ProbeManager.ActiveProbeManager.ProbeController.ResetPosition();
                ProbeManager.ActiveProbeManager.ProbeController.SetProbePosition();
            }

            UpdateCoordinates();
        }

        public bool IsFocused()
        {
            return isActiveAndEnabled && _inputFields.Any(inputField => inputField != null && inputField.isFocused);
        }

        public void SetColorChooserColor(Color color)
        {
            _colorChooser.color = color;
        }

        public void ColorChooserCycle()
        {
            ProbeManager.ActiveProbeManager.Color = ProbeProperties.NextColor;
        }

        #endregion
    }
}