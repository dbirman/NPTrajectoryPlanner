using System;
using UI.States;

namespace UI.AutomationStack
{
    /// <summary>
    ///     Implements reference coordinate calibration in the Automation Stack.<br />
    /// </summary>
    public partial class AutomationStackHandler
    {
        private async partial void ResetReferenceCoordinate()
        {
            // Throw exception if invariant is violated.
            if (!_state.IsEnabled)
                throw new InvalidOperationException(
                    "Cannot reset reference coordinate calibration if automation is not enabled on probe "
                        + ProbeManager.ActiveProbeManager.name
                );

            // Reset the reference coordinate calibration of the active probe manager.
            if (await ActiveManipulatorBehaviorController.ResetReferenceCoordinate())
                // Set probe's automation state to be calibrated if it did happen.
                ActiveProbeStateManager.SetCalibrated();
        }
    }
}
