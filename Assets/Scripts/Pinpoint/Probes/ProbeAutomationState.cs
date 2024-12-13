namespace Pinpoint.Probes
{
    /// <summary>
    ///     Drive state in the automation cycle.
    /// </summary>
    public enum ProbeAutomationState
    {
        /// <summary>
        ///     Initial, uncalibrated state.
        /// </summary>
        IsUncalibrated,

        /// <summary>
        ///     Is calibrated to the reference coordinate. Could be positioned anywhere.
        /// </summary>
        IsCalibrated,

        /// <summary>
        ///     Moving to the target entry coordinate.
        /// </summary>
        DrivingToTargetEntryCoordinate,

        /// <summary>
        ///     At the target entry coordinate.
        /// </summary>
        AtEntryCoordinate,

        /// <summary>
        ///     Calibrated to the Dura; ready for insertion drive.
        /// </summary>
        AtDuraInsert,

        /// <summary>
        ///     Driving to near target depth (insertion drive).
        /// </summary>
        DrivingToNearTarget,

        /// <summary>
        ///     At near target depth (insertion drive). Need to switch to 2/3 speed.
        /// </summary>
        AtNearTargetInsert,

        /// <summary>
        ///     Driving to past target depth (insertion drive).
        /// </summary>
        DrivingToPastTarget,

        /// <summary>
        ///     At past target depth (insertion drive).
        /// </summary>
        AtPastTarget,

        /// <summary>
        ///     Driving back up to target depth (insertion drive).
        /// </summary>
        ReturningToTarget,

        /// <summary>
        ///     At target depth (insertion drive).
        /// </summary>
        AtTarget,

        /// <summary>
        ///     Driving back up to the Dura (exit drive).
        /// </summary>
        ExitingToDura,

        /// <summary>
        ///     At the Dura (exit drive). Should not re-insert.
        /// </summary>
        AtDuraExit,

        /// <summary>
        ///     Driving above the Dura by a safe margin (exit drive).
        /// </summary>
        ExitingToMargin,

        /// <summary>
        ///     At the safe margin above the Dura (exit drive).
        /// </summary>
        AtExitMargin,

        /// <summary>
        ///     Driving back up to the target entry coordinate (exit drive).
        /// </summary>
        ExitingToTargetEntryCoordinate,
    }
}
