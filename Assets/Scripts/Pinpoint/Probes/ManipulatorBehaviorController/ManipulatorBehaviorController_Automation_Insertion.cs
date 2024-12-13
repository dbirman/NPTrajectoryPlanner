using System;
using System.Globalization;
using BrainAtlas;
using EphysLink;
using UnityEngine;

namespace Pinpoint.Probes.ManipulatorBehaviorController
{
    public partial class ManipulatorBehaviorController
    {
        #region Constants

        #region Relative distances

        /// <summary>
        ///     Extra safety margin for the Dura to outside to ensure probe is fully retracted (mm).
        /// </summary>
        private const float DURA_MARGIN_DISTANCE = 0.2f;

        /// <summary>
        ///     Distance from target to start slowing down probe (mm).
        /// </summary>
        private const float NEAR_TARGET_DISTANCE = 1f;

        #endregion

        #region Speed multipliers

        /// <summary>
        ///     Slowdown factor for the probe when it is near the target.
        /// </summary>
        private const float NEAR_TARGET_SPEED_MULTIPLIER = 2f / 3f;

        /// <summary>
        ///     Extra speed multiplier for the probe when it is exiting.
        /// </summary>
        private const int EXIT_DRIVE_SPEED_MULTIPLIER = 6;

        #endregion

        #endregion

        #region Properties

        /// <summary>
        ///     Is the probe driving in the insertion cycle?
        /// </summary>
        /// <remarks>Used to identify which buttons should be made available.</remarks>
        public bool IsMoving { get; private set; }

        private float _actualExitMarginToDuraDistance => _duraDepth - _entryCoordinateDepth;

        #endregion

        #region Drive Functions

        /// <summary>
        ///     Start or resume inserting the probe to the target insertion.
        /// </summary>
        /// <param name="targetInsertionProbeManager">Probe manager for the target insertion.</param>
        /// <param name="baseSpeed">Base driving speed in mm/s.</param>
        /// <param name="drivePastDistance">Distance to drive past target in mm.</param>
        /// <exception cref="InvalidOperationException">Probe is not in a drivable state.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Unhandled probe drive state.</exception>
        public async void Drive(
            ProbeManager targetInsertionProbeManager,
            float baseSpeed,
            float drivePastDistance
        )
        {
            while (
                ProbeAutomationStateManager.ProbeAutomationState != ProbeAutomationState.AtTarget
            )
            {
                // Throw exception if state is not valid.
                if (!ProbeAutomationStateManager.IsInsertable())
                    throw new InvalidOperationException(
                        "Cannot drive to target insertion if the probe is not in a drivable state."
                    );

                // Get target depth.
                var targetDepth = GetTargetDepth(targetInsertionProbeManager);

                // Set state to driving state (if needed).
                ProbeAutomationStateManager.SetToInsertionDrivingState();

                // Log set to driving state.
                LogDriveToTargetInsertion(targetDepth, baseSpeed, drivePastDistance);

                // Set probe to be moving.
                IsMoving = true;

                // Handle driving state.
                switch (ProbeAutomationStateManager.ProbeAutomationState)
                {
                    case ProbeAutomationState.DrivingToNearTarget:
                        // Drive to near target if not already there.
                        if (
                            GetCurrentDistanceToTarget(targetInsertionProbeManager)
                            > NEAR_TARGET_DISTANCE
                        )
                        {
                            var driveToNearTargetResponse =
                                await CommunicationManager.Instance.SetDepth(
                                    new SetDepthRequest(
                                        ManipulatorID,
                                        targetDepth - NEAR_TARGET_DISTANCE,
                                        baseSpeed
                                    )
                                );

                            // Shortcut exit if there was an error.
                            if (CommunicationManager.HasError(driveToNearTargetResponse.Error))
                                return;
                        }

                        break;
                    case ProbeAutomationState.DrivingToPastTarget:
                        // Drive to past target.
                        var driveToPastTargetResponse =
                            await CommunicationManager.Instance.SetDepth(
                                new SetDepthRequest(
                                    ManipulatorID,
                                    targetDepth + drivePastDistance,
                                    baseSpeed * NEAR_TARGET_SPEED_MULTIPLIER
                                )
                            );

                        // Shortcut exit if there was an error.
                        if (CommunicationManager.HasError(driveToPastTargetResponse.Error))
                            return;
                        break;
                    case ProbeAutomationState.ReturningToTarget:
                        // Drive up to target.
                        var returnToTargetResponse = await CommunicationManager.Instance.SetDepth(
                            new SetDepthRequest(
                                ManipulatorID,
                                targetDepth,
                                baseSpeed * NEAR_TARGET_SPEED_MULTIPLIER
                            )
                        );

                        // Shortcut exit if there was an error.
                        if (CommunicationManager.HasError(returnToTargetResponse.Error))
                            return;
                        break;
                    case ProbeAutomationState.IsUncalibrated:
                    case ProbeAutomationState.IsCalibrated:
                    case ProbeAutomationState.DrivingToTargetEntryCoordinate:
                    case ProbeAutomationState.AtEntryCoordinate:
                    case ProbeAutomationState.AtDuraInsert:
                    case ProbeAutomationState.AtNearTargetInsert:
                    case ProbeAutomationState.AtPastTarget:
                    case ProbeAutomationState.AtTarget:
                    case ProbeAutomationState.ExitingToDura:
                    case ProbeAutomationState.AtDuraExit:
                    case ProbeAutomationState.ExitingToMargin:
                    case ProbeAutomationState.AtExitMargin:
                    case ProbeAutomationState.ExitingToTargetEntryCoordinate:
                        throw new InvalidOperationException(
                            $"Not a valid driving state: {ProbeAutomationStateManager.ProbeAutomationState}"
                        );
                    default:
                        throw new ArgumentOutOfRangeException(
                            $"Unhandled probe drive state: {ProbeAutomationStateManager.ProbeAutomationState}"
                        );
                }

                // Increment cycle state.
                ProbeAutomationStateManager.IncrementInsertionCycleState();

                // Log the event.
                LogDriveToTargetInsertion(targetDepth, baseSpeed, drivePastDistance);
            }

            // Set probe to be done moving.
            IsMoving = false;
        }

        /// <summary>
        ///     Stop the probe's insertion.
        /// </summary>
        public async void StopInsertion()
        {
            var stopResponse = await CommunicationManager.Instance.Stop(ManipulatorID);

            // Log and exit if there was an error.
            if (!string.IsNullOrEmpty(stopResponse))
            {
                Debug.LogError(stopResponse);
                return;
            }

            // Set probe to be not moving.
            IsMoving = false;

            // Log stop event.
            OutputLog.Log(
                new[]
                {
                    "Automation",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "Drive",
                    ManipulatorID,
                    "Stop"
                }
            );
        }

        /// <summary>
        ///     Start or resume exiting the probe to the target insertion.
        /// </summary>
        /// <param name="targetInsertionProbeManager">Probe manager for the target insertion.</param>
        /// <param name="baseSpeed">Base driving speed in mm/s.</param>
        /// <exception cref="InvalidOperationException">Probe is not in an exitable state.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Unhandled probe exit state.</exception>
        /// <returns>True when exited out to entry coordinate, false otherwise.</returns>
        public async Awaitable<bool> Exit(ProbeManager targetInsertionProbeManager, float baseSpeed)
        {
            while (
                ProbeAutomationStateManager.ProbeAutomationState
                != ProbeAutomationState.AtEntryCoordinate
            )
            {
                // Throw exception if state is not valid.
                if (!ProbeAutomationStateManager.IsExitable())
                    throw new InvalidOperationException(
                        "Cannot exit to target insertion if the probe is not in a state that can exit."
                    );

                // Get target depth.
                var targetDepth = GetTargetDepth(targetInsertionProbeManager);

                // Set state to exiting state (if needed).
                ProbeAutomationStateManager.SetToExitingDrivingState();

                // Log set to exiting state.
                LogDriveToTargetInsertion(targetDepth, baseSpeed);

                // Set probe to be moving.
                IsMoving = true;

                // Handle exiting state.
                switch (ProbeAutomationStateManager.ProbeAutomationState)
                {
                    case ProbeAutomationState.ExitingToDura:
                        // Exit back up to the Dura.
                        var exitToDuraResponse = await CommunicationManager.Instance.SetDepth(
                            new SetDepthRequest(
                                ManipulatorID,
                                _duraDepth,
                                baseSpeed * EXIT_DRIVE_SPEED_MULTIPLIER
                            )
                        );

                        // Shortcut exit if there was an error.
                        if (CommunicationManager.HasError(exitToDuraResponse.Error))
                            return false;
                        break;
                    case ProbeAutomationState.ExitingToMargin:
                        // Remove brain surface offset.
                        BrainSurfaceOffset = 0;

                        // Shortcut skip if user wanted to skip exit margin.
                        if (_skipExitMargin)
                            break;

                        // Exit to the safe margin above the Dura.
                        var exitToMarginResponse = await CommunicationManager.Instance.SetDepth(
                            new SetDepthRequest(
                                ManipulatorID,
                                _duraDepth - DURA_MARGIN_DISTANCE,
                                baseSpeed * EXIT_DRIVE_SPEED_MULTIPLIER
                            )
                        );

                        // Shortcut exit if there was an error.
                        if (CommunicationManager.HasError(exitToMarginResponse.Error))
                            return false;
                        break;
                    case ProbeAutomationState.ExitingToTargetEntryCoordinate:
                        // Drive to the target entry coordinate (same place before calibrating to the Dura).
                        var exitToTargetEntryCoordinateResponse =
                            await CommunicationManager.Instance.SetPosition(
                                new SetPositionRequest(
                                    ManipulatorID,
                                    ConvertInsertionAPMLDVToManipulatorPosition(
                                        _trajectoryCoordinates.third
                                    ),
                                    AUTOMATIC_MOVEMENT_SPEED
                                )
                            );

                        // Shortcut exit if there was an error.
                        if (
                            CommunicationManager.HasError(exitToTargetEntryCoordinateResponse.Error)
                        )
                            return false;

                        break;
                    case ProbeAutomationState.IsUncalibrated:
                    case ProbeAutomationState.IsCalibrated:
                    case ProbeAutomationState.DrivingToTargetEntryCoordinate:
                    case ProbeAutomationState.AtEntryCoordinate:
                    case ProbeAutomationState.AtDuraInsert:
                    case ProbeAutomationState.DrivingToNearTarget:
                    case ProbeAutomationState.AtNearTargetInsert:
                    case ProbeAutomationState.DrivingToPastTarget:
                    case ProbeAutomationState.AtPastTarget:
                    case ProbeAutomationState.ReturningToTarget:
                    case ProbeAutomationState.AtTarget:
                    case ProbeAutomationState.AtDuraExit:
                    case ProbeAutomationState.AtExitMargin:
                        throw new InvalidOperationException(
                            $"Not a valid exit state: {ProbeAutomationStateManager.ProbeAutomationState}"
                        );
                    default:
                        throw new ArgumentOutOfRangeException(
                            $"Unhandled probe exit state: {ProbeAutomationStateManager.ProbeAutomationState}"
                        );
                }

                // Increment cycle state.
                ProbeAutomationStateManager.IncrementInsertionCycleState();

                // Log the event.
                LogDriveToTargetInsertion(targetDepth, baseSpeed);
            }

            // Set probe to be done moving.
            IsMoving = false;

            // Indicate that the exit has completed.
            return true;
        }

        #endregion

        #region Helper Functions

        /// <summary>
        ///     Log a drive event.
        /// </summary>
        /// <param name="targetDepth">Target depth of drive.</param>
        /// <param name="baseSpeed">Base speed of drive.</param>
        /// <param name="drivePastDistance">Distance (mm) driven past the target. Only supplied in insertion drives.</param>
        private void LogDriveToTargetInsertion(
            float targetDepth,
            float baseSpeed,
            float drivePastDistance = 0
        )
        {
            OutputLog.Log(
                new[]
                {
                    "Automation",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "DriveToTargetInsertion",
                    ManipulatorID,
                    ProbeAutomationStateManager.ProbeAutomationState.ToString(),
                    targetDepth.ToString(CultureInfo.InvariantCulture),
                    baseSpeed.ToString(CultureInfo.InvariantCulture),
                    drivePastDistance.ToString(CultureInfo.InvariantCulture)
                }
            );
        }

        /// <summary>
        ///     Compute the target coordinate adjusted for the probe's actual position.
        /// </summary>
        /// <param name="targetInsertionProbeManager"></param>
        /// <returns>APMLDV coordinates of where the probe should actually go.</returns>
        private Vector3 GetOffsetAdjustedTargetCoordinate(ProbeManager targetInsertionProbeManager)
        {
            // Extract target insertion.
            var targetInsertion = targetInsertionProbeManager.ProbeController.Insertion;

            var targetWorldT = targetInsertion.PositionWorldT();
            var relativePositionWorldT = _probeController.Insertion.PositionWorldT() - targetWorldT;
            var probeTipTForward = _probeController.ProbeTipT.forward;
            var offsetAdjustedRelativeTargetPositionWorldT = Vector3.ProjectOnPlane(
                relativePositionWorldT,
                probeTipTForward
            );
            var offsetAdjustedTargetCoordinateWorldT =
                targetWorldT + offsetAdjustedRelativeTargetPositionWorldT;

            // Converting worldT to AtlasT (to capture new reference coordinate offset when there is scaling)
            // then switch axes to get APMLDV.
            var offsetAdjustedTargetCoordinateAtlasT =
                BrainAtlasManager.ActiveReferenceAtlas.World2Atlas(
                    offsetAdjustedTargetCoordinateWorldT
                );
            var offsetAdjustedTargetCoordinateT = BrainAtlasManager.ActiveAtlasTransform.U2T_Vector(
                offsetAdjustedTargetCoordinateAtlasT
            );

            return offsetAdjustedTargetCoordinateT;
        }

        /// <summary>
        ///     Compute the absolute distance from the target insertion to the Dura.
        /// </summary>
        /// <param name="targetInsertionProbeManager">Target to computer distance to.</param>
        /// <returns>Distance in mm to the target from the Dura.</returns>
        private float GetTargetDistanceToDura(ProbeManager targetInsertionProbeManager)
        {
            return Vector3.Distance(
                GetOffsetAdjustedTargetCoordinate(targetInsertionProbeManager),
                _duraCoordinate
            );
        }

        /// <summary>
        ///     Compute the current distance to the target insertion.
        /// </summary>
        /// <param name="targetInsertionProbeManager"></param>
        /// <returns>Distance in mm to the target from the probe. NaN on error.</returns>
        private float GetCurrentDistanceToTarget(ProbeManager targetInsertionProbeManager)
        {
            return Vector3.Distance(
                _probeController.Insertion.APMLDV,
                GetOffsetAdjustedTargetCoordinate(targetInsertionProbeManager)
            );
        }

        /// <summary>
        ///     Compute the target depth for the probe to drive to.
        /// </summary>
        /// <param name="targetInsertionProbeManager">Target to drive (insert) to.</param>
        /// <returns>The depth the manipulator needs to drive to reach the target insertion.</returns>
        private float GetTargetDepth(ProbeManager targetInsertionProbeManager)
        {
            return _duraDepth + GetTargetDistanceToDura(targetInsertionProbeManager);
        }

        /// <summary>
        ///     Compute the ETA for a probe to reach a target insertion (or exit).
        /// </summary>
        /// <param name="targetInsertionProbeManager">Target to calculate ETA to.</param>
        /// <param name="baseSpeed">Base driving speed in mm/s.</param>
        /// <param name="drivePastDistance">Distance to drive past target in mm.</param>
        /// <returns>MM:SS format ETA for reaching a target or exiting, based on the probe's state.</returns>
        public string GetETA(
            ProbeManager targetInsertionProbeManager,
            float baseSpeed,
            float drivePastDistance
        )
        {
            // Get current distance to target.
            var distanceToTarget = GetCurrentDistanceToTarget(targetInsertionProbeManager);

            // Compute ETA.
            var secondsToDestination = ProbeAutomationStateManager.ProbeAutomationState switch
            {
                ProbeAutomationState.DrivingToNearTarget
                    => Mathf.Max(0, distanceToTarget - NEAR_TARGET_DISTANCE) / baseSpeed // To near target.
                        + (NEAR_TARGET_DISTANCE + 2 * drivePastDistance)
                            / (baseSpeed * NEAR_TARGET_SPEED_MULTIPLIER), // To past target and back to target.
                ProbeAutomationState.DrivingToPastTarget
                    => (distanceToTarget + 2 * drivePastDistance)
                        / (baseSpeed * NEAR_TARGET_SPEED_MULTIPLIER), // To past target and back to target.
                ProbeAutomationState.ReturningToTarget
                    => distanceToTarget / (baseSpeed * NEAR_TARGET_SPEED_MULTIPLIER), // Back to target.
                ProbeAutomationState.ExitingToDura
                    => (GetTargetDistanceToDura(targetInsertionProbeManager) - distanceToTarget)
                        / (baseSpeed * EXIT_DRIVE_SPEED_MULTIPLIER) // To Dura.
                        + DURA_MARGIN_DISTANCE / (baseSpeed * EXIT_DRIVE_SPEED_MULTIPLIER) // To exit margin.
                        + _actualExitMarginToDuraDistance / AUTOMATIC_MOVEMENT_SPEED, // To entry coordinate.
                ProbeAutomationState.ExitingToMargin
                    => (
                        DURA_MARGIN_DISTANCE
                        - (distanceToTarget - GetTargetDistanceToDura(targetInsertionProbeManager))
                    ) / (baseSpeed * EXIT_DRIVE_SPEED_MULTIPLIER) // To exit margin.
                        + _actualExitMarginToDuraDistance / AUTOMATIC_MOVEMENT_SPEED, // To entry coordinate.
                ProbeAutomationState.ExitingToTargetEntryCoordinate
                    => (
                        IDEAL_ENTRY_COORDINATE_TO_DURA_DISTANCE
                        - (distanceToTarget - GetTargetDistanceToDura(targetInsertionProbeManager))
                    ) / AUTOMATIC_MOVEMENT_SPEED,
                _ => 0
            };

            // Return formatted time if above 1 minute.
            return secondsToDestination < 60
                ? secondsToDestination.ToString("F1")
                : TimeSpan.FromSeconds(secondsToDestination).ToString(@"mm\:ss");
        }

        #endregion
    }
}
