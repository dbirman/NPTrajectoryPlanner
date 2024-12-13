using System;
using Pinpoint.Probes;
using Pinpoint.Probes.ManipulatorBehaviorController;
using UI.States;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.AutomationStack
{
    /// <summary>
    ///     Automation Stack UI origin class.
    /// </summary>
    /// <remarks>
    ///     This script handles getting the components, running UI setup functions, and registering callbacks.
    /// </remarks>
    public partial class AutomationStackHandler : MonoBehaviour
    {
        #region Components

        #region State

        [SerializeField]
        private AutomationStackState _state;

        #endregion

        #region UI

        // Document.
        [SerializeField]
        private UIDocument _uiDocument;
        private VisualElement _root => _uiDocument.rootVisualElement;

        // Panels.
        private VisualElement _automationStackPanel;

        #region Reference Coordinate Calibration

        private Button _resetReferenceCoordinateButton;

        #endregion

        #region Target Insertion

        private RadioButtonGroup _targetInsertionRadioButtonGroup;
        private Button _driveToTargetEntryCoordinateButton;

        #endregion

        #region Dura Calibration

        private Button _resetDuraCalibrationButton;

        #endregion

        #region Insertion

        private Button _driveToTargetInsertionButton;
        private Button _stopButton;
        private Button _exitButton;

        #endregion

        #endregion


        #region Probes

        private static ManipulatorBehaviorController ActiveManipulatorBehaviorController =>
            ProbeManager.ActiveProbeManager.ManipulatorBehaviorController;

        private static ProbeAutomationStateManager ActiveProbeStateManager =>
            ActiveManipulatorBehaviorController.ProbeAutomationStateManager;

        #endregion

        #endregion


        #region Unity

        private void OnEnable()
        {
            // Get components.
            _automationStackPanel = _root.Q("automation-stack-panel");
            _resetReferenceCoordinateButton = _automationStackPanel.Q<Button>(
                "reset-reference-coordinate-button"
            );
            _targetInsertionRadioButtonGroup = _automationStackPanel.Q<RadioButtonGroup>(
                "target-insertion-radio-button-group"
            );
            _driveToTargetEntryCoordinateButton = _automationStackPanel.Q<Button>(
                "drive-to-target-entry-coordinate-button"
            );
            _resetDuraCalibrationButton = _automationStackPanel.Q<Button>(
                "reset-dura-calibration-button"
            );
            _driveToTargetInsertionButton = _automationStackPanel.Q<Button>(
                "drive-to-target-insertion-button"
            );
            _stopButton = _automationStackPanel.Q<Button>("stop-button");
            _exitButton = _automationStackPanel.Q<Button>("exit-button");

            // Register callbacks.
            _resetReferenceCoordinateButton.clicked += ResetReferenceCoordinate;
            _targetInsertionRadioButtonGroup.RegisterValueChangedCallback(
                OnTargetInsertionSelectionChanged
            );
            _driveToTargetEntryCoordinateButton.clicked += OnDriveToTargetEntryCoordinatePressed;
            _resetDuraCalibrationButton.clicked += OnResetDuraCalibrationPressed;
            _driveToTargetInsertionButton.clicked += OnDriveToTargetInsertionButtonPressed;
            _stopButton.clicked += OnStopDriveButtonPressed;
            _exitButton.clicked += OnExitButtonPressed;
        }

        private void OnDisable()
        {
            // Unregister callbacks.
            _resetReferenceCoordinateButton.clicked -= ResetReferenceCoordinate;
            _targetInsertionRadioButtonGroup.UnregisterValueChangedCallback(
                OnTargetInsertionSelectionChanged
            );
            _driveToTargetEntryCoordinateButton.clicked -= OnDriveToTargetEntryCoordinatePressed;
            _resetDuraCalibrationButton.clicked -= OnResetDuraCalibrationPressed;
            _driveToTargetInsertionButton.clicked -= OnDriveToTargetInsertionButtonPressed;
            _stopButton.clicked -= OnStopDriveButtonPressed;
            _exitButton.clicked -= OnExitButtonPressed;
        }

        private void FixedUpdate()
        {
            // Shortcut exit if not enabled and cleanup.
            if (!_state.IsEnabled)
            {
                FlushTargetInsertionOptionsCache();
                return;
            }

            // Update the target insertion options radio button colors.
            UpdateTargetInsertionOptionsRadioButtonColors();
        }

        #endregion

        #region Stages

        #region Reference Coordinate Calibration

        /// <summary>
        ///     Reset the reference coordinate calibration of the active probe.
        /// </summary>
        /// <exception cref="InvalidOperationException">Probe is not selected/active and is not controlled by Ephys Link</exception>
        private partial void ResetReferenceCoordinate();

        #endregion

        #region Target Insertion

        /// <summary>
        ///     Updates the colors of the target insertion options radio buttons to match the target probe's colors.
        /// </summary>
        /// <remarks>
        ///     Will only update if the cached options mismatch the current options.
        /// </remarks>
        private partial void UpdateTargetInsertionOptionsRadioButtonColors();

        /// <summary>
        ///     Flush cached target insertion options.
        /// </summary>
        /// <remarks>
        ///     Used when switching to a probe that may not be enabled (since the cache will not be updated then). This will fix
        ///     the issue where returning to an enabled probe will not update the radio button colors.
        /// </remarks>
        private partial void FlushTargetInsertionOptionsCache();

        /// <summary>
        ///     Callback for when the target insertion selection changes.
        /// </summary>
        /// <remarks>
        ///     Sets (or unsets) the target insertion on the probe. Calibrated probes will be set back to "Is Calibrated" state.
        ///     Will do nothing if the probe is not selected/active and is not controlled by Ephys Link.
        /// </remarks>
        /// <param name="changeEvent">The change event holding the new selection.</param>
        // /// <exception cref="InvalidOperationException">Probe is not selected/active and is not controlled by Ephys Link</exception>
        private partial void OnTargetInsertionSelectionChanged(ChangeEvent<int> changeEvent);

        /// <summary>
        ///     Callback for moving or stopping the drive to the target entry coordinate.<br />
        /// </summary>
        /// <remarks>
        ///     Will move or stop based on automation state of the probe.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        ///     Probe is not selected/active, is not controlled by Ephys Link, and is not
        ///     calibrated to reference coordinate.
        /// </exception>
        private partial void OnDriveToTargetEntryCoordinatePressed();

        #endregion

        #region Dura Calibration

        /// <summary>
        ///     Reset the Dura calibration of the active probe.
        /// </summary>
        /// <exception cref="InvalidOperationException">Probe is not selected/active and is not controlled by Ephys Link</exception>
        private partial void OnResetDuraCalibrationPressed();

        #endregion

        #region Insertion

        /// <summary>
        ///     Start or resume probe insertion.
        /// </summary>
        /// <remarks>Sets moving state to true.</remarks>
        /// <exception cref="InvalidOperationException">UI state does not have the button enabled and showing.</exception>
        private partial void OnDriveToTargetInsertionButtonPressed();

        /// <summary>
        ///     Stop probe movement.
        /// </summary>
        /// <remarks>Sets moving state to false.</remarks>
        /// <exception cref="InvalidOperationException">UI state does not have the button enabled and showing.</exception>
        private partial void OnStopDriveButtonPressed();

        /// <summary>
        ///     Start or resume probe retraction.
        /// </summary>
        /// <remarks>Sets moving state to true.</remarks>
        /// <exception cref="InvalidOperationException">
        ///     UI state does not have the button enabled and show
        ///     ing.
        /// </exception>
        private partial void OnExitButtonPressed();

        #endregion

        #endregion
    }
}
