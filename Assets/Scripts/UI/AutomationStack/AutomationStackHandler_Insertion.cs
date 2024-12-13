using System;
using UnityEngine.UIElements;

namespace UI.AutomationStack
{
    /// <summary>
    ///     Implements reference coordinate calibration in the Automation Stack.<br />
    /// </summary>
    public partial class AutomationStackHandler
    {
        #region Implementations

        private partial void OnDriveToTargetInsertionButtonPressed()
        {
            // Throw exception if in an invalid state.
            if (
                !_state.IsDriveToTargetInsertionButtonEnabled
                || _state.DriveToTargetInsertionButtonDisplayStyle == DisplayStyle.None
            )
                throw new InvalidOperationException(
                    "Cannot drive to target insertion if the button is not enabled or visible (ready to drive)."
                );

            // Call drive.
            ActiveManipulatorBehaviorController.Drive(
                _state.TargetInsertionProbeManager,
                _state.BaseSpeed,
                _state.DrivePastTargetDistanceMillimeters
            );
        }

        private partial void OnStopDriveButtonPressed()
        {
            // Throw exception if in an invalid state.
            if (_state.StopButtonDisplayStyle == DisplayStyle.None)
                throw new InvalidOperationException(
                    "Cannot stop driving to target insertion if the button is not visible (driving)."
                );

            // Call stop.
            ActiveManipulatorBehaviorController.StopInsertion();
        }

        private async partial void OnExitButtonPressed()
        {
            // Throw exception if in an invalid state.
            if (_state.ExitButtonDisplayStyle == DisplayStyle.None)
                throw new InvalidOperationException(
                    "Cannot exit to target insertion if the button is not visible (ready to exit)."
                );

            // Call exit and reset insertion selection once exit has completed.
            if (
                await ActiveManipulatorBehaviorController.Exit(
                    _state.TargetInsertionProbeManager,
                    _state.BaseSpeed
                )
            )
                _state.SelectedTargetInsertionIndex = 0;
        }

        #endregion
    }
}
