using System.Globalization;
using System.Linq;
using EphysLink;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pinpoint.UI.EphysLinkSettings
{
    /// <summary>
    ///     Panel representing an available manipulator to connect to and its settings.
    /// </summary>
    public class ManipulatorConnectionPanel : MonoBehaviour
    {
        #region Components

        [SerializeField]
        private TMP_Text _manipulatorIdText;

        [SerializeField]
        private GameObject _handednessGroup;

        [SerializeField]
        private Dropdown _handednessDropdown;

        [SerializeField]
        private GameObject _probeConnectionGroup;

        [SerializeField]
        private Dropdown _linkedProbeDropdown;

        [SerializeField]
        private GameObject _probePropertiesSection;

        [SerializeField]
        private InputField _zeroCoordinateXInputField;

        [SerializeField]
        private InputField _zeroCoordinateYInputField;

        [SerializeField]
        private InputField _zeroCoordinateZInputField;

        [SerializeField]
        private InputField _zeroCoordinateDInputField;

        [SerializeField]
        private Dropdown _duraDropDirectionDropdown;

        [SerializeField]
        private InputField _brainSurfaceOffsetInputField;

        [SerializeField]
        private Toggle _enableManualControlToggle;

        [SerializeField]
        private GameObject _returnToZeroCoordinateButtonGameObject;

        [SerializeField]
        private GameObject _stopReturningToZeroCoordinateButtonGameObject;

        private ProbeManager _attachedProbe;

        #endregion

        #region Properties

        private EphysLinkSettings _ephysLinkSettings;
        private string _manipulatorId;
        private int _numAxes;

        #endregion

        #region Constructor

        public void Initialize(EphysLinkSettings settingsMenu, string manipulatorID, int numAxes)
        {
            // Set properties
            _ephysLinkSettings = settingsMenu;
            _manipulatorId = manipulatorID;
            _numAxes = numAxes;

            // Get attached probe (could be null)
            _attachedProbe = ProbeManager.Instances.Find(manager =>
                manager.IsEphysLinkControlled
                && manager.ManipulatorBehaviorController.ManipulatorID == manipulatorID
            );

            // Initialize components
            _manipulatorIdText.text = manipulatorID;

            // Restore or setup normal manipulator

            UpdateLinkableProbeOptions();

            // Show or hide handedness dropdown depending on manipulator type
            if (numAxes == 3)
            {
                _handednessDropdown.value = 0;
                _handednessGroup.SetActive(false);
            }
            else
            {
                _handednessGroup.SetActive(true);
            }

            // Apply handedness from memory or default to right-handed, also pass along manipulator type
            if (_attachedProbe)
            {
                _handednessDropdown.value = _attachedProbe
                    .ManipulatorBehaviorController
                    .IsRightHanded
                    ? 1
                    : 0;
                _attachedProbe.ManipulatorBehaviorController.NumAxes = numAxes;
            }
            else
            {
                _handednessDropdown.value = Settings
                    .EphysLinkRightHandedManipulators.Split("\n")
                    .Contains(manipulatorID)
                    ? 1
                    : 0;
            }

            // Register event listeners for updating probes list
            settingsMenu.ShouldUpdateProbesListEvent.AddListener(UpdateLinkableProbeOptions);
        }

        #endregion

        #region UI Function

        /// <summary>
        ///     Handle changing manipulator's registered handedness on UI change.
        /// </summary>
        /// <param name="value">Selected index of the handedness options (0 = left-handed, 1 = right-handed)</param>
        public void OnManipulatorHandednessValueChanged(int value)
        {
            // Set handedness on attached probe if it exists
            if (_attachedProbe)
                _attachedProbe.ManipulatorBehaviorController.IsRightHanded = value == 1;

            // Update handedness in settings
            var currentRightHandedManipulators = Settings
                .EphysLinkRightHandedManipulators.Split("\n")
                .ToList();
            if (currentRightHandedManipulators.Contains(_manipulatorId) && value == 0)
                currentRightHandedManipulators.Remove(_manipulatorId);
            else if (!currentRightHandedManipulators.Contains(_manipulatorId) && value == 1)
                currentRightHandedManipulators.Add(_manipulatorId);

            Settings.EphysLinkRightHandedManipulators = string.Join(
                "\n",
                currentRightHandedManipulators
            );
        }

        /// <summary>
        ///     Handle selecting (or deselecting) a probe to attach to this manipulator
        /// </summary>
        /// <param name="value">The selected index</param>
        public void OnLinkedProbeSelectionChanged(int value)
        {
            if (value == 0)
            {
                // With values != 0, there definitely was an attached probe before
                _attachedProbe.SetIsEphysLinkControlled(
                    false,
                    _manipulatorId,
                    onSuccess: () =>
                    {
                        // Disable keyboard control
                        _attachedProbe.ProbeController.ManipulatorManualControl = false;
                        _enableManualControlToggle.SetIsOnWithoutNotify(false);

                        // Clear event listeners
                        _attachedProbe.ManipulatorBehaviorController.ZeroCoordinateOffsetChangedEvent.RemoveAllListeners();
                        _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffsetChangedEvent.RemoveAllListeners();
                        _attachedProbe.ManipulatorBehaviorController.IsSetToDropToSurfaceWithDepthChangedEvent.RemoveAllListeners();

                        // Remove probe from linked probes list
                        _ephysLinkSettings.LinkedProbes.Remove(_attachedProbe);
                        _attachedProbe = null;

                        // Inform others a change was made
                        _ephysLinkSettings.InvokeShouldUpdateProbesListEvent();
                    }
                );
            }
            else
            {
                // Disconnect currently attached probe (if it's different)
                if (
                    _attachedProbe
                    && _attachedProbe.UUID != _linkedProbeDropdown.options[value].text
                )
                    _attachedProbe.SetIsEphysLinkControlled(
                        false,
                        onSuccess: () =>
                        {
                            // Disable keyboard control
                            _attachedProbe.ProbeController.ManipulatorManualControl = false;
                            _enableManualControlToggle.SetIsOnWithoutNotify(false);

                            // Clear event listeners
                            _attachedProbe.ManipulatorBehaviorController.ZeroCoordinateOffsetChangedEvent.RemoveAllListeners();
                            _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffsetChangedEvent.RemoveAllListeners();
                            _attachedProbe.ManipulatorBehaviorController.IsSetToDropToSurfaceWithDepthChangedEvent.RemoveAllListeners();

                            // Remove probe from linked probes list
                            _ephysLinkSettings.LinkedProbes.Remove(_attachedProbe);
                        }
                    );

                // Find the new probe and attach it
                var selectedProbeUUID = _linkedProbeDropdown.options[value].text;
                var newProbeManager = ProbeManager.Instances.Find(manager =>
                    manager.UUID == selectedProbeUUID
                );
                newProbeManager.SetIsEphysLinkControlled(
                    true,
                    _manipulatorId,
                    onSuccess: () =>
                    {
                        _attachedProbe = newProbeManager;
                        _ephysLinkSettings.LinkedProbes.Add(_attachedProbe);

                        // Copy over manipulator type, handedness, and dropdown direction and enable state
                        _attachedProbe.ManipulatorBehaviorController.NumAxes = _numAxes;
                        OnManipulatorHandednessValueChanged(_handednessDropdown.value);
                        UpdateDuraDropDirection(_duraDropDirectionDropdown.value);
                        SetDuraDropInteractable(
                            _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffset == 0
                        );
                        _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffsetChangedEvent.AddListener(
                            brainSurfaceOffset => SetDuraDropInteractable(brainSurfaceOffset == 0)
                        );

                        // Inform others a change was made
                        _ephysLinkSettings.InvokeShouldUpdateProbesListEvent();
                    }
                );
            }
        }

        /// <summary>
        ///     Update zero coordinate offset X-axis to the given value
        /// </summary>
        /// <param name="newValue">New offset X-axis value</param>
        public void UpdateZeroCoordinateOffsetX(string newValue)
        {
            _attachedProbe.ManipulatorBehaviorController.ReferenceCoordinateOffset = new Vector4(
                float.Parse(newValue),
                float.NaN,
                float.NaN,
                float.NaN
            );
        }

        /// <summary>
        ///     Update zero coordinate offset Y-axis to the given value
        /// </summary>
        /// <param name="newValue">New offset Y-axis value</param>
        public void UpdateZeroCoordinateOffsetY(string newValue)
        {
            _attachedProbe.ManipulatorBehaviorController.ReferenceCoordinateOffset = new Vector4(
                float.NaN,
                float.Parse(newValue),
                float.NaN,
                float.NaN
            );
        }

        /// <summary>
        ///     Update zero coordinate offset Z-axis to the given value
        /// </summary>
        /// <param name="newValue">New offset Z-axis value</param>
        public void UpdateZeroCoordinateOffsetZ(string newValue)
        {
            _attachedProbe.ManipulatorBehaviorController.ReferenceCoordinateOffset = new Vector4(
                float.NaN,
                float.NaN,
                float.Parse(newValue),
                float.NaN
            );
        }

        /// <summary>
        ///     Update zero coordinate offset D-axis to the given value
        /// </summary>
        /// <param name="newValue">New offset D-axis value</param>
        public void UpdateZeroCoordinateOffsetD(string newValue)
        {
            _attachedProbe.ManipulatorBehaviorController.ReferenceCoordinateOffset = new Vector4(
                float.NaN,
                float.NaN,
                float.NaN,
                float.Parse(newValue)
            );
        }

        public void UpdateDuraDropDirection(int value)
        {
            // Set drop direction on attached probe if it exists
            // if (_attachedProbe)
            //     _attachedProbe.ManipulatorBehaviorController.IsSetToDropToSurfaceWithDepth =
            //         value == 1;
        }

        private void SetDuraDropInteractable(bool interactable)
        {
            _duraDropDirectionDropdown.interactable = interactable;
        }

        /// <summary>
        ///     Update brain surface offset to the given value
        /// </summary>
        /// <param name="newValue">New brain surface offset value</param>
        public void UpdateBrainSurfaceOffset(string newValue)
        {
            _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffset = float.Parse(newValue);
        }

        /// <summary>
        ///     Increment the brain surface offset by 100 µm
        /// </summary>
        /// <param name="positive">Increment in the positive direction or not</param>
        public void IncrementBrainSurfaceOffset(bool positive)
        {
            _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffset += positive
                ? 0.1f
                : -0.1f;
        }

        /// <summary>
        ///     Activate or deactivate keyboard control of the manipulator
        /// </summary>
        /// <param name="state">True to enable keyboard control, False otherwise</param>
        public void UpdateManualControlState(bool state)
        {
            // Enable flag in probe controller
            _attachedProbe.ProbeController.ManipulatorManualControl = state;

            // Enable/disable return to zero coordinate button
            _returnToZeroCoordinateButtonGameObject.SetActive(state);
        }

        /// <summary>
        ///     Return manipulator back to zero coordinate
        /// </summary>
        public async void ReturnToZeroCoordinate()
        {
            // Disable keyboard control
            _attachedProbe.ProbeController.ManipulatorManualControl = false;

            // Hide move button and show stop button
            _returnToZeroCoordinateButtonGameObject.SetActive(false);
            _stopReturningToZeroCoordinateButtonGameObject.SetActive(true);

            // Move manipulator back to zero coordinate
            if (await _attachedProbe.ManipulatorBehaviorController.MoveBackToReferenceCoordinate())
                // Reset dura drop direction on successful return.
                _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffset = 0;
            
            // Show move button and hide stop button
            _returnToZeroCoordinateButtonGameObject.SetActive(true);
            _stopReturningToZeroCoordinateButtonGameObject.SetActive(false);

            // Re-enable keyboard control
            _attachedProbe.ProbeController.ManipulatorManualControl = true;
        }

        public void StopReturningToZeroCoordinate()
        {
            CommunicationManager.Instance.Stop(
                _manipulatorId,
                () =>
                {
                    // Hide stop button and show move button
                    _returnToZeroCoordinateButtonGameObject.SetActive(true);
                    _stopReturningToZeroCoordinateButtonGameObject.SetActive(false);

                    // Re-enable keyboard control
                    _attachedProbe.ProbeController.ManipulatorManualControl = true;
                },
                Debug.LogError
            );
        }

        #endregion

        #region Helper Functions

        /// <summary>
        ///     Refresh probe properties section
        /// </summary>
        private void UpdateProbePropertiesSectionState()
        {
            // Split between having probes and whether pathfinder is being used (pathfinder has no probe properties)
            if (_attachedProbe && _numAxes > 0)
            {
                _probePropertiesSection.SetActive(true);
                UpdateZeroCoordinateOffsetInputFields(
                    _attachedProbe.ManipulatorBehaviorController.ReferenceCoordinateOffset
                );
                UpdateBrainSurfaceOffsetInputField(
                    _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffset
                );

                // Attach update event listeners
                _attachedProbe.ManipulatorBehaviorController.ZeroCoordinateOffsetChangedEvent.AddListener(
                    UpdateZeroCoordinateOffsetInputFields
                );
                _attachedProbe.ManipulatorBehaviorController.BrainSurfaceOffsetChangedEvent.AddListener(
                    UpdateBrainSurfaceOffsetInputField
                );
            }
            else
            {
                _probePropertiesSection.SetActive(false);
            }
        }

        /// <summary>
        ///     Updates the list of linkable probes and re-selects the previously selected probe if it is still available.
        /// </summary>
        private void UpdateLinkableProbeOptions()
        {
            // Capture current selection (either from the previous selection or from the attached probe)
            var previouslyLinkedProbeUUID = _attachedProbe ? _attachedProbe.UUID : "";

            // Repopulate dropdown options
            _linkedProbeDropdown.ClearOptions();
            var availableProbes = ProbeManager
                .Instances.Where(manager =>
                    manager.ManipulatorBehaviorController
                    && (
                        manager.UUID == previouslyLinkedProbeUUID
                        || !_ephysLinkSettings.LinkedProbes.Contains(manager)
                    )
                )
                .Select(manager => manager.UUID)
                .ToList();
            availableProbes.Insert(0, "None");
            _linkedProbeDropdown.AddOptions(availableProbes);

            // Select previously selected probe if it exists
            var value = Mathf.Max(0, availableProbes.IndexOf(previouslyLinkedProbeUUID));
            if (previouslyLinkedProbeUUID == "")
                _linkedProbeDropdown.SetValueWithoutNotify(value);
            else
                _linkedProbeDropdown.value = value;

            // Update probe properties section
            UpdateProbePropertiesSectionState();

            // Update color of dropdown to match probe
            var colorBlockCopy = _linkedProbeDropdown.colors;
            colorBlockCopy.normalColor = _attachedProbe ? _attachedProbe.Color : Color.white;
            colorBlockCopy.selectedColor = new Color(
                colorBlockCopy.normalColor.r * 0.9f,
                colorBlockCopy.normalColor.g * 0.9f,
                colorBlockCopy.normalColor.b * 0.9f
            );
            colorBlockCopy.highlightedColor = colorBlockCopy.selectedColor;
            _linkedProbeDropdown.colors = colorBlockCopy;
        }

        /// <summary>
        ///     Update input fields for zero coordinate offset.
        /// </summary>
        /// <param name="zeroCoordinateOffset">New zero coordinate offset</param>
        private void UpdateZeroCoordinateOffsetInputFields(Vector4 zeroCoordinateOffset)
        {
            _zeroCoordinateXInputField.text = zeroCoordinateOffset.x.ToString(
                CultureInfo.InvariantCulture
            );
            _zeroCoordinateYInputField.text = zeroCoordinateOffset.y.ToString(
                CultureInfo.InvariantCulture
            );
            _zeroCoordinateZInputField.text = zeroCoordinateOffset.z.ToString(
                CultureInfo.InvariantCulture
            );
            _zeroCoordinateDInputField.text = zeroCoordinateOffset.w.ToString(
                CultureInfo.InvariantCulture
            );
        }

        /// <summary>
        ///     Update input field for brain surface offset.
        /// </summary>
        /// <param name="brainSurfaceOffset">New brain surface offset</param>
        private void UpdateBrainSurfaceOffsetInputField(float brainSurfaceOffset)
        {
            _brainSurfaceOffsetInputField.text = brainSurfaceOffset.ToString(
                CultureInfo.InvariantCulture
            );
        }

        #endregion
    }
}
