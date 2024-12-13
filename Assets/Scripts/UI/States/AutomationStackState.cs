using System;
using System.Collections.Generic;
using System.Linq;
using BrainAtlas;
using Core.Util;
using Pinpoint.Probes;
using Pinpoint.Probes.ManipulatorBehaviorController;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.States
{
    [CreateAssetMenu]
    public class AutomationStackState : ResettingScriptableObject
    {
        #region Common

        /// <summary>
        ///     Active probe's manipulator behavior controller.
        /// </summary>
        private static ManipulatorBehaviorController ActiveManipulatorBehaviorController =>
            ProbeManager.ActiveProbeManager.ManipulatorBehaviorController;

        /// <summary>
        ///     Active probe's probe automation state manager.
        /// </summary>
        private static ProbeAutomationStateManager ActiveProbeAutomationStateManager =>
            ActiveManipulatorBehaviorController.ProbeAutomationStateManager;

        #endregion

        #region Panel

        /// <summary>
        ///     Is the current probe's pitch valid for automation.
        /// </summary>
        /// <returns>True if the probe's pitch is above 30°, false otherwise (or when there is no active probe).</returns>
        private static bool IsPitchValid =>
            ProbeManager.ActiveProbeManager
            && ProbeManager.ActiveProbeManager.ProbeController.Insertion.Pitch > 30;

        /// <summary>
        ///     Visibility of the pitch warning.
        /// </summary>
        /// <returns>Flex when the pitch is invalid, none otherwise.</returns>
        /// <see cref="IsPitchValid" />
        [CreateProperty]
        public DisplayStyle PitchWarningDisplayStyle =>
            !IsPitchValid ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        ///     Is the entire Automation stack enabled.
        /// </summary>
        /// <returns>True when the active probe manager is Ephys Link controlled.</returns>
        [CreateProperty]
        // ReSharper disable once MemberCanBePrivate.Global
        // ReSharper disable once MemberCanBeMadeStatic.Global
        public bool IsEnabled =>
            ProbeManager.ActiveProbeManager
            && ProbeManager.ActiveProbeManager.IsEphysLinkControlled
            && IsPitchValid;

        #endregion

        #region Reference Coordinate Calibration

        /// <summary>
        ///     X offset of the reference coordinate calibration (mm).
        /// </summary>
        /// <exception cref="InvalidOperationException">Automation is not enabled for the active probe manager.</exception>
        [CreateProperty]
        public float ReferenceCoordinateCalibrationX
        {
            get => IsEnabled ? ActiveManipulatorBehaviorController.ReferenceCoordinateOffset.x : 0;
            set
            {
                if (!IsEnabled)
                    throw new InvalidOperationException(
                        "Cannot set the X offset for reference coordinate when automation is not enabled for probe "
                            + ProbeManager.ActiveProbeManager.name
                    );
                ActiveManipulatorBehaviorController.ReferenceCoordinateOffset = new Vector4(
                    value,
                    float.NaN,
                    float.NaN,
                    float.NaN
                );
            }
        }

        /// <summary>
        ///     Y offset of the reference coordinate calibration (mm).
        /// </summary>
        /// <exception cref="InvalidOperationException">Automation is not enabled for the active probe manager.</exception>
        [CreateProperty]
        public float ReferenceCoordinateCalibrationY
        {
            get => IsEnabled ? ActiveManipulatorBehaviorController.ReferenceCoordinateOffset.y : 0;
            set
            {
                if (!IsEnabled)
                    throw new InvalidOperationException(
                        "Cannot set the Y offset for reference coordinate when automation is not enabled for probe "
                            + ProbeManager.ActiveProbeManager.name
                    );
                ActiveManipulatorBehaviorController.ReferenceCoordinateOffset = new Vector4(
                    float.NaN,
                    value,
                    float.NaN,
                    float.NaN
                );
            }
        }

        /// <summary>
        ///     Z offset of the reference coordinate calibration (mm).
        /// </summary>
        /// <exception cref="InvalidOperationException">Automation is not enabled for the active probe manager.</exception>
        [CreateProperty]
        public float ReferenceCoordinateCalibrationZ
        {
            get => IsEnabled ? ActiveManipulatorBehaviorController.ReferenceCoordinateOffset.z : 0;
            set
            {
                if (!IsEnabled)
                    throw new InvalidOperationException(
                        "Cannot set the Z offset for reference coordinate when automation is not enabled for probe "
                            + ProbeManager.ActiveProbeManager.name
                    );
                ActiveManipulatorBehaviorController.ReferenceCoordinateOffset = new Vector4(
                    float.NaN,
                    float.NaN,
                    value,
                    float.NaN
                );
            }
        }

        /// <summary>
        ///     Depth offset of the reference coordinate calibration (mm).
        /// </summary>
        /// <exception cref="InvalidOperationException">Automation is not enabled for the active probe manager.</exception>
        [CreateProperty]
        public float ReferenceCoordinateCalibrationDepth
        {
            get => IsEnabled ? ActiveManipulatorBehaviorController.ReferenceCoordinateOffset.w : 0;
            set
            {
                if (!IsEnabled)
                    throw new InvalidOperationException(
                        "Cannot set the depth offset for reference coordinate when automation is not enabled for probe "
                            + ProbeManager.ActiveProbeManager.name
                    );
                ActiveManipulatorBehaviorController.ReferenceCoordinateOffset = new Vector4(
                    float.NaN,
                    float.NaN,
                    float.NaN,
                    value
                );
            }
        }

        #endregion

        #region Target Insertion

        /// <summary>
        ///     Mapping from Ephys Link controlled manipulator probe's probe manager to the selected target insertion's probe
        ///     manager.
        /// </summary>
        private readonly Dictionary<
            ProbeManager,
            ProbeManager
        > _manipulatorProbeManagerToSelectedTargetInsertionProbeManager = new();

        /// <summary>
        ///     Is the target insertion radio button group enabled.
        /// </summary>
        /// <returns>
        ///     True if the probe is enabled and has not started driving into the brain yet, false otherwise.
        /// </returns>
        [CreateProperty]
        public bool IsTargetInsertionRadioButtonGroupEnabled =>
            IsEnabled
            && ActiveProbeAutomationStateManager.ProbeAutomationState
                < ProbeAutomationState.DrivingToNearTarget;

        /// <summary>
        ///     Selected target insertion option index.
        ///     Property accessor for the selected target insertion index.
        /// </summary>
        /// <remarks>
        ///     Acts as a conversion layer between the mapping of probe managers to target insertion probe managers. Does not
        ///     set any value if automation is not enabled for the active probe manager.
        /// </remarks>
        // /// <exception cref="InvalidOperationException">Automation is not enabled for the active probe manager.</exception>
        [CreateProperty]
        public int SelectedTargetInsertionIndex
        {
            get
            {
                // Shortcut exit if panel is not enabled or if the probe hasn't selected a target.
                if (
                    !IsEnabled
                    || !_manipulatorProbeManagerToSelectedTargetInsertionProbeManager.TryGetValue(
                        ProbeManager.ActiveProbeManager,
                        out var selectedTargetInsertionProbeManager
                    )
                )
                    return 0;

                // Compute and return the index of the selected target insertion probe manager.
                return TargetInsertionOptions
                    .ToList()
                    .IndexOf(
                        ProbeManagerToTargetInsertionOption(selectedTargetInsertionProbeManager)
                    );
                ;
            }
            set
            {
                // TODO: Change to throw exception if invariant is violated once update issue is resolved.
                // Shortcut exit if invariant is violated.
                if (!IsEnabled)
                    return;
                // throw new InvalidOperationException(
                //     "Cannot set the selected target insertion index when automation is not enabled for probe "
                //         + ProbeManager.ActiveProbeManager.name
                // );

                // Remove mapping if selected index <= 0 ("None").
                if (value <= 0)
                {
                    _manipulatorProbeManagerToSelectedTargetInsertionProbeManager.Remove(
                        ProbeManager.ActiveProbeManager
                    );
                    return;
                }

                // Get probe manager.
                var targetInsertionProbeManager =
                    SurfaceCoordinateStringToTargetInsertionOptionProbeManagers[
                        TargetInsertionOptions.ElementAt(value)
                    ];

                // Update the mapping
                _manipulatorProbeManagerToSelectedTargetInsertionProbeManager[
                    ProbeManager.ActiveProbeManager
                ] = targetInsertionProbeManager;
            }
        }

        /// <summary>
        ///     Option list for target insertion.
        /// </summary>
        /// <remarks>
        ///     Convert's the targetable probe manager's surface coordinate to a string and prepends "None".
        /// </remarks>
        /// <returns>Target insertion options as a string enumerable, or an empty enumerable if the panel is not enabled.</returns>
        [CreateProperty]
        // ReSharper disable once MemberCanBePrivate.Global
        public IEnumerable<string> TargetInsertionOptions =>
            IsEnabled
                ? SurfaceCoordinateStringToTargetInsertionOptionProbeManagers.Keys.Prepend("None")
                : Enumerable.Empty<string>();

        #region Option List helpers

        /// <summary>
        ///     Compute the target insertion probe manager from selected target insertion index.
        /// </summary>
        public ProbeManager TargetInsertionProbeManager =>
            SelectedTargetInsertionIndex > 0
                ? SurfaceCoordinateStringToTargetInsertionOptionProbeManagers[
                    TargetInsertionOptions.ElementAt(SelectedTargetInsertionIndex)
                ]
                : null;

        /// <summary>
        ///     Expose mapping from target insertion option probe manager to surface coordinate string.
        /// </summary>
        public Dictionary<
            string,
            ProbeManager
        > SurfaceCoordinateStringToTargetInsertionOptionProbeManagers =>
            TargetInsertionOptionsProbeManagers.ToDictionary(ProbeManagerToTargetInsertionOption);

        /// <summary>
        ///     Filter for probe managers this manipulator can target defined by:<br />
        ///     1. Are not ephys link controlled<br />
        ///     2. Are inside the brain (non-NaN entry coordinate).<br />
        ///     3. Not already selected<br />
        ///     4. Angles are coterminal<br />
        /// </summary>
        /// <returns>Filtered enumerable of probe managers, or an empty one if the panel is not enabled.</returns>
        private IEnumerable<ProbeManager> TargetInsertionOptionsProbeManagers =>
            IsEnabled
                ? ProbeManager
                    .Instances
                    // 1. Are not EphysLink controlled.
                    .Where(manager => !manager.IsEphysLinkControlled)
                    // 2. Are inside the brain (non-NaN entry coordinate).
                    .Where(manager =>
                        !float.IsNaN(
                            manager
                                .FindEntryIdxCoordinate(
                                    BrainAtlasManager.ActiveReferenceAtlas.World2AtlasIdx(
                                        manager.ProbeController.Insertion.PositionWorldU()
                                    ),
                                    BrainAtlasManager.ActiveReferenceAtlas.World2Atlas_Vector(
                                        manager.ProbeController.GetTipWorldU().tipUpWorldU
                                    )
                                )
                                .x
                        )
                    )
                    // 3. Not already selected (except for your own).
                    .Where(manager =>
                        !_manipulatorProbeManagerToSelectedTargetInsertionProbeManager
                            .Where(pair => pair.Key != ProbeManager.ActiveProbeManager)
                            .Select(pair => pair.Value)
                            .Contains(manager)
                    )
                    // 4. Angles are coterminal.
                    .Where(manager =>
                        IsCoterminal(
                            manager.ProbeController.Insertion.Angles,
                            ProbeManager.ActiveProbeManager.ProbeController.Insertion.Angles
                        )
                    )
                : Enumerable.Empty<ProbeManager>();

        /// <summary>
        ///     Compute if two sets of 3D angles are coterminal.
        /// </summary>
        /// <param name="first">First set of angles.</param>
        /// <param name="second">Second set of angles.</param>
        /// <returns>True if the 3D angles are coterminous, false otherwise.</returns>
        private static bool IsCoterminal(Vector3 first, Vector3 second)
        {
            return Mathf.Abs(first.x - second.x) % 360 < 0.01f
                && Mathf.Abs(first.y - second.y) % 360 < 0.01f
                && Mathf.Abs(first.z - second.z) % 360 < 0.01f;
        }

        /// <summary>
        ///     Create a target insertion option string from a probe manager.
        /// </summary>
        /// <param name="manager">Probe manager to extract info from</param>
        /// <returns>Target insertion option string from a probe manager.</returns>
        private static string ProbeManagerToTargetInsertionOption(ProbeManager manager)
        {
            return (manager.OverrideName ?? manager.name)
                + ": "
                + SurfaceCoordinateToString(manager.GetSurfaceCoordinateT());
        }

        /// <summary>
        ///     Convert a surface coordinate to a string.
        /// </summary>
        /// <param name="surfaceCoordinate">Brain surface coordinate to convert.</param>
        /// <returns>Brain surface coordinate encoded as a string.</returns>
        private static string SurfaceCoordinateToString(
            (Vector3 surfaceCoordinateT, float depthT) surfaceCoordinate
        )
        {
            var apMicrometers = Math.Truncate(surfaceCoordinate.surfaceCoordinateT.x * 1000);
            var mlMicrometers = Math.Truncate(surfaceCoordinate.surfaceCoordinateT.y * 1000);
            var dvMicrometers = Math.Truncate(surfaceCoordinate.surfaceCoordinateT.z * 1000);
            var depthMicrometers = Math.Truncate(surfaceCoordinate.depthT * 1000);
            return "AP: "
                + (Settings.DisplayUM ? apMicrometers : apMicrometers / 1000f)
                + ", ML: "
                + (Settings.DisplayUM ? mlMicrometers : mlMicrometers / 1000f)
                + ", DV: "
                + (Settings.DisplayUM ? dvMicrometers : dvMicrometers / 1000f)
                + ", Depth: "
                + (Settings.DisplayUM ? depthMicrometers : depthMicrometers / 1000f);
        }

        #endregion

        /// <summary>
        ///     Record of probes that have acknowledged their target insertion is out of their bounds.
        /// </summary>
        public readonly HashSet<ProbeManager> AcknowledgedTargetInsertionIsOutOfBoundsProbes =
            new();

        /// <summary>
        ///     Is the drive to selected target entry coordinate button enabled.<br />
        /// </summary>
        /// <returns>
        ///     Returns true if the active probe manager is Ephys Link controlled, calibrated to reference coordinate, and has a
        ///     selected target.
        /// </returns>
        [CreateProperty]
        public bool IsDriveToTargetEntryCoordinateButtonEnabled =>
            IsEnabled
            && ActiveProbeAutomationStateManager.IsCalibrated()
            && !ActiveProbeAutomationStateManager.HasReachedTargetEntryCoordinate()
            && _manipulatorProbeManagerToSelectedTargetInsertionProbeManager.ContainsKey(
                ProbeManager.ActiveProbeManager
            );

        /// <summary>
        ///     Text for the drive to target entry coordinate button.
        /// </summary>
        /// <returns>
        ///     Says "Stop" when the probe is in motion, "Drive to Target Entry Coordinate" when the probe is calibrated (and
        ///     ready), 'Entry Coordinate Reached' when the probe has reached the target entry coordinate, and "Please Calibrate
        ///     Probe" when the probe is not calibrated (else case).
        /// </returns>
        [CreateProperty]
        public string DriveToTargetEntryCoordinateButtonText =>
            IsEnabled
                ? ActiveProbeAutomationStateManager.HasReachedTargetEntryCoordinate()
                    ? "Entry Coordinate Reached"
                    : ActiveProbeAutomationStateManager.ProbeAutomationState
                    == ProbeAutomationState.DrivingToTargetEntryCoordinate
                        ? "Stop"
                        : ActiveProbeAutomationStateManager.IsCalibrated()
                            ? "Drive to Target Entry Coordinate"
                            : "Please Calibrate Probe"
                : "Please Enable Automation";

        #endregion

        #region Dura Calibration

        /// <summary>
        ///     Dura calibration offset (mm).
        /// </summary>
        /// <exception cref="InvalidOperationException">Automation is not enabled for the active probe manager.</exception>
        [CreateProperty]
        public float DuraCalibrationOffset
        {
            get => IsEnabled ? ActiveManipulatorBehaviorController.BrainSurfaceOffset : 0;
            set
            {
                if (!IsEnabled)
                    throw new InvalidOperationException(
                        "Cannot set the Dura calibration offset when automation is not enabled for probe "
                            + ProbeManager.ActiveProbeManager.name
                    );
                ActiveManipulatorBehaviorController.BrainSurfaceOffset = value;
            }
        }

        #endregion

        #region Insertion

        /// <summary>
        ///     Is the base speed radio group enabled for editing.
        /// </summary>
        /// <returns>True when the selected probe is Ephys Link controlled and not moving, false otherwise.</returns>
        [CreateProperty]
        public bool IsBaseSpeedRadioGroupEnabled =>
            IsEnabled && !ActiveManipulatorBehaviorController.IsMoving;

        /// <summary>
        ///     Selection index in radio button group for base insertion speed.
        /// </summary>
        public int SelectedBaseSpeedIndex;

        /// <summary>
        ///     Is the custom base speed field enabled.
        /// </summary>
        /// <returns>True when the selected probe is Ephys Link controlled and not moving, false otherwise.</returns>
        [CreateProperty]
        public bool IsCustomBaseSpeedEnabled =>
            IsEnabled && !ActiveManipulatorBehaviorController.IsMoving;

        /// <summary>
        ///     Custom base insertion speed (µm/s).
        /// </summary>
        /// <remarks>Should only be used when <see cref="SelectedBaseSpeedIndex" /> is on "Custom" index.</remarks>
        public int CustomBaseSpeed;

        /// <summary>
        ///     Compute the base speed from selected base speed index.
        /// </summary>
        public float BaseSpeed =>
            SelectedBaseSpeedIndex switch
            {
                0 => 0.002f,
                1 => 0.005f,
                2 => 0.01f,
                3 => 0.5f,
                _ => CustomBaseSpeed / 1000f
            };

        /// <summary>
        ///     Visibility of the custom insertion base speed field.
        /// </summary>
        /// <remarks>Should be invisible unless <see cref="SelectedBaseSpeedIndex" /> is on "Custom" index.</remarks>
        [CreateProperty]
        public DisplayStyle CustomInsertionBaseSpeedDisplayStyle =>
            SelectedBaseSpeedIndex == 4 ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        ///     Is the drive past target distance field enabled.
        /// </summary>
        /// <returns>True when the selected probe is Ephys Link controlled and not moving, false otherwise.</returns>
        [CreateProperty]
        public bool IsDrivePastTargetDistanceEnabled =>
            IsEnabled && !ActiveManipulatorBehaviorController.IsMoving;

        /// <summary>
        ///     Distance to drive past the target insertion depth (mm).
        /// </summary>
        public float DrivePastTargetDistanceMillimeters;

        /// <summary>
        ///     Distance to drive past the target insertion depth (µm).
        /// </summary>
        /// <remarks>Used in UI since these are small numbers.</remarks>
        [CreateProperty]
        public int DrivePastTargetDistanceMicrometers
        {
            get => Mathf.RoundToInt(DrivePastTargetDistanceMillimeters * 1000);
            set => DrivePastTargetDistanceMillimeters = value / 1000f;
        }

        /// <summary>
        ///     Is the drive to target insertion button enabled.
        /// </summary>
        /// <returns>
        ///     Returns true if the active probe manager is Ephys Link controlled and has its Dura offset calibrated.
        /// </returns>
        [CreateProperty]
        public bool IsDriveToTargetInsertionButtonEnabled =>
            IsEnabled
            && SelectedTargetInsertionIndex > 0
            && ActiveProbeAutomationStateManager.ProbeAutomationState
                >= ProbeAutomationState.AtDuraInsert;

        /// <summary>
        ///     Text for the drive to target insertion button.
        /// </summary>
        /// <returns>Request to calibrate to the Dura if not calibrated, otherwise "Drive".</returns>
        [CreateProperty]
        public string DriveToTargetInsertionButtonText =>
            IsEnabled
            && ActiveProbeAutomationStateManager.ProbeAutomationState
                < ProbeAutomationState.AtDuraInsert
                ? "Please Calibrate to the Dura"
                : "Drive";

        /// <summary>
        ///     Visibility of the drive to target insertion button (insert probe into brain).
        /// </summary>
        /// <remarks>
        ///     Shown only when selected/active probe is Ephys Link controlled, has a target insertion selected, not moving,
        ///     at the Dura or inside.
        /// </remarks>
        [CreateProperty]
        public DisplayStyle DriveToTargetInsertionButtonDisplayStyle =>
            IsEnabled
            && !ActiveManipulatorBehaviorController.IsMoving
            && ActiveProbeAutomationStateManager.ProbeAutomationState
                < ProbeAutomationState.AtDuraExit
            && ActiveProbeAutomationStateManager.ProbeAutomationState
                != ProbeAutomationState.AtTarget
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        /// <summary>
        ///     Visibility of the drive stop button (stop the probe from moving).
        /// </summary>
        /// <remarks>Shown when selected/active probe is Ephys Link controlled and the probe is moving.</remarks>
        [CreateProperty]
        public DisplayStyle StopButtonDisplayStyle =>
            IsEnabled && ActiveManipulatorBehaviorController.IsMoving
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        /// <summary>
        ///     Visibility of the exit button (drive the probe back out of the brain).
        /// </summary>
        /// <remarks>
        ///     Shown only when selected/active probe is Ephys Link controlled, has a target insertion selected, not moving,
        ///     and is past the Dura.
        /// </remarks>
        [CreateProperty]
        public DisplayStyle ExitButtonDisplayStyle =>
            IsEnabled
            && SelectedTargetInsertionIndex > 0
            && !ActiveManipulatorBehaviorController.IsMoving
            && ActiveProbeAutomationStateManager.IsExitable()
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        [CreateProperty]
        public string ETA =>
            IsEnabled && SelectedTargetInsertionIndex > 0
                ? $"ETA: {ActiveManipulatorBehaviorController.GetETA(TargetInsertionProbeManager, BaseSpeed, DrivePastTargetDistanceMillimeters)}"
                : "ETA: N/A";

        /// <summary>
        ///     Visibility of the ETA label.
        /// </summary>
        /// <remarks>
        ///     Shown only when selected/active probe is Ephys Link controlled, the probe is moving, and the probe is in the
        ///     brain.
        /// </remarks>
        [CreateProperty]
        public DisplayStyle ETADisplayStyle =>
            IsEnabled
            && ActiveManipulatorBehaviorController.IsMoving
            && ActiveProbeAutomationStateManager.ProbeAutomationState
                >= ProbeAutomationState.AtDuraInsert
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        #endregion
    }
}
