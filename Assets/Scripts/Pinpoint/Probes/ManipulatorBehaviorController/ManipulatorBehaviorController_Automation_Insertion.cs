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

        /// <summary>
        ///     Speed multiplier of the probe once outside the brain.
        /// </summary>
        private const int OUTSIDE_DRIVE_SPEED_MULTIPLIER = 50;

        #endregion

        #endregion

        #region Properties

        /// <summary>
        ///     Is the probe driving in the insertion cycle?
        /// </summary>
        /// <remarks>Used to identify which buttons should be made available.</remarks>
        public bool IsMoving { get; private set; }

        #region Caches

        private Vector3 _cachedTargetCoordinate = Vector3.negativeInfinity;
        private Vector3 _cachedOffsetAdjustedTargetCoordinate = Vector3.negativeInfinity;

        #endregion

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
        public void Drive(
            ProbeManager targetInsertionProbeManager,
            float baseSpeed,
            float drivePastDistance
        )
        {
            // Throw exception if invariant is violated.
            if (!ProbeAutomationStateManager.IsInsertable())
                throw new InvalidOperationException(
                    "Cannot drive to target insertion if the probe is not in a drivable state."
                );

            // Get target depth.
            var targetDepth = GetTargetDepth(targetInsertionProbeManager);

            // Set state to driving state (if needed).
            ProbeAutomationStateManager.SetToInsertionDrivingState();

            // Log set to driving state.
            LogDrive(targetDepth, baseSpeed, drivePastDistance);

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
                        CommunicationManager.Instance.SetDepth(
                            new SetDepthRequest(
                                ManipulatorID,
                                targetDepth - NEAR_TARGET_DISTANCE,
                                baseSpeed
                            ),
                            _ => CompleteDriveStep(),
                            Debug.LogError
                        );
                    // Skip to next step if already through near target.
                    else
                        CompleteDriveStep();
                    break;
                case ProbeAutomationState.DrivingToPastTarget:
                    // Drive to past target.
                    CommunicationManager.Instance.SetDepth(
                        new SetDepthRequest(
                            ManipulatorID,
                            targetDepth + drivePastDistance,
                            baseSpeed * NEAR_TARGET_SPEED_MULTIPLIER
                        ),
                        _ => CompleteDriveStep(),
                        Debug.LogError
                    );
                    break;
                case ProbeAutomationState.ReturningToTarget:
                    // Drive up to target.
                    CommunicationManager.Instance.SetDepth(
                        new SetDepthRequest(
                            ManipulatorID,
                            targetDepth,
                            baseSpeed * NEAR_TARGET_SPEED_MULTIPLIER
                        ),
                        _ => CompleteDriveStep(),
                        Debug.LogError
                    );
                    break;
                case ProbeAutomationState.IsUncalibrated:
                case ProbeAutomationState.IsCalibrated:
                case ProbeAutomationState.DrivingToTargetEntryCoordinate:
                case ProbeAutomationState.AtEntryCoordinate:
                case ProbeAutomationState.AtDuraInsert:
                case ProbeAutomationState.AtNearTargetInsert:
                case ProbeAutomationState.AtPastTarget:
                case ProbeAutomationState.AtTarget:
                case ProbeAutomationState.ExitingToNearTarget:
                case ProbeAutomationState.AtNearTargetExit:
                case ProbeAutomationState.ExitingToDura:
                case ProbeAutomationState.AtDuraExit:
                case ProbeAutomationState.ExitingToMargin:
                case ProbeAutomationState.AtExitMargin:
                case ProbeAutomationState.ExitingToTargetEntryCoordinate:
                case ProbeAutomationState.DrivingToBregma:
                    throw new InvalidOperationException(
                        $"Not a valid driving state: {ProbeAutomationStateManager.ProbeAutomationState}"
                    );
                default:
                    throw new ArgumentOutOfRangeException(
                        $"Unhandled probe drive state: {ProbeAutomationStateManager.ProbeAutomationState}"
                    );
            }

            return;

            // Increment the state in the insertion cycle and call drive if not at target yet.
            void CompleteDriveStep()
            {
                // Increment cycle state.
                ProbeAutomationStateManager.IncrementInsertionCycleState();

                // Log the event.
                LogDrive(targetDepth, baseSpeed, drivePastDistance);

                // Call drive if not at target yet.
                if (
                    ProbeAutomationStateManager.ProbeAutomationState
                    != ProbeAutomationState.AtTarget
                )
                    Drive(targetInsertionProbeManager, baseSpeed, drivePastDistance);
                // Set probe to be done moving.
                else
                    IsMoving = false;
            }
        }

        /// <summary>
        ///     Stop the probe's movement.
        /// </summary>
        public void Stop()
        {
            CommunicationManager.Instance.Stop(
                ManipulatorID,
                () =>
                {
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
                },
                Debug.LogError
            );
        }

        /// <summary>
        ///     Start or resume exiting the probe to the target insertion.
        /// </summary>
        /// <param name="targetInsertionProbeManager">Probe manager for the target insertion.</param>
        /// <param name="baseSpeed">Base driving speed in mm/s.</param>
        /// <exception cref="InvalidOperationException">Probe is not in an exitable state.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Unhandled probe exit state.</exception>
        public void Exit(ProbeManager targetInsertionProbeManager, float baseSpeed)
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
            LogDrive(targetDepth, baseSpeed);

            // Set probe to be moving.
            IsMoving = true;

            // Handle exiting state.
            switch (ProbeAutomationStateManager.ProbeAutomationState)
            {
                case ProbeAutomationState.ExitingToNearTarget:
                    // Exit to near target if not already there.
                    if (
                        GetCurrentDistanceToTarget(targetInsertionProbeManager)
                        < NEAR_TARGET_DISTANCE
                    )
                        CommunicationManager.Instance.SetDepth(
                            new SetDepthRequest(
                                ManipulatorID,
                                targetDepth - NEAR_TARGET_DISTANCE,
                                baseSpeed
                                    * EXIT_DRIVE_SPEED_MULTIPLIER
                                    * NEAR_TARGET_SPEED_MULTIPLIER
                            ),
                            _ => CompleteExitStep(),
                            Debug.LogError
                        );
                    // Skip to next step if already above near target.
                    else
                        CompleteExitStep();
                    break;
                case ProbeAutomationState.ExitingToDura:
                    // Exit back up to the Dura.
                    CommunicationManager.Instance.SetDepth(
                        new SetDepthRequest(
                            ManipulatorID,
                            _duraDepth,
                            baseSpeed * EXIT_DRIVE_SPEED_MULTIPLIER
                        ),
                        _ => CompleteExitStep(),
                        Debug.LogError
                    );
                    break;
                case ProbeAutomationState.ExitingToMargin:
                    // Exit to the safe margin above the Dura.
                    CommunicationManager.Instance.SetDepth(
                        new SetDepthRequest(
                            ManipulatorID,
                            _duraDepth - DURA_MARGIN_DISTANCE,
                            baseSpeed * EXIT_DRIVE_SPEED_MULTIPLIER
                        ),
                        _ => CompleteExitStep(),
                        Debug.LogError
                    );
                    break;
                case ProbeAutomationState.ExitingToTargetEntryCoordinate:
                    // Drive to the target entry coordinate (same place before calibrating to the Dura).
                    CommunicationManager.Instance.SetPosition(
                        new SetPositionRequest(
                            ManipulatorID,
                            ConvertInsertionAPMLDVToManipulatorPosition(
                                _trajectoryCoordinates.third
                            ),
                            AUTOMATIC_MOVEMENT_SPEED
                        ),
                        _ => CompleteExitStep(),
                        Debug.LogError
                    );
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
                case ProbeAutomationState.AtNearTargetExit:
                case ProbeAutomationState.AtDuraExit:
                case ProbeAutomationState.AtExitMargin:
                case ProbeAutomationState.DrivingToBregma:
                    throw new InvalidOperationException(
                        $"Not a valid exit state: {ProbeAutomationStateManager.ProbeAutomationState}"
                    );
                default:
                    throw new ArgumentOutOfRangeException(
                        $"Unhandled probe exit state: {ProbeAutomationStateManager.ProbeAutomationState}"
                    );
            }

            return;

            // Increment the state in the insertion cycle and call exit if not at entry coordinate yet.
            void CompleteExitStep()
            {
                // Increment cycle state.
                ProbeAutomationStateManager.IncrementInsertionCycleState();

                // Log the event.
                LogDrive(targetDepth, baseSpeed);

                // Call exit if not back at entry coordinate yet.
                if (
                    ProbeAutomationStateManager.ProbeAutomationState
                    != ProbeAutomationState.AtEntryCoordinate
                )
                {
                    Exit(targetInsertionProbeManager, baseSpeed);
                }
                // Set probe to be done moving and remove Dura offset.
                else
                {
                    IsMoving = false;
                    BrainSurfaceOffset = 0;
                }
            }
        }

        #endregion

        #region Helper Functions

        /// <summary>
        ///     Log a drive event.
        /// </summary>
        /// <param name="targetDepth">Target depth of drive.</param>
        /// <param name="baseSpeed">Base speed of drive.</param>
        /// <param name="drivePastDistance">Distance (mm) driven past the target. Only supplied in insertion drives.</param>
        private void LogDrive(float targetDepth, float baseSpeed, float drivePastDistance = 0)
        {
            OutputLog.Log(
                new[]
                {
                    "Automation",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "Drive",
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

            // Shortcut exit if already computed and targetInsertion did not change.
            if (
                targetInsertion.APMLDV == _cachedTargetCoordinate
                && !float.IsNegativeInfinity(_cachedOffsetAdjustedTargetCoordinate.x)
            )
                return _cachedOffsetAdjustedTargetCoordinate;

            var targetWorldT = targetInsertion.PositionWorldT();
            var relativePositionWorldT = _probeController.Insertion.PositionWorldT() - targetWorldT;
            var probeTipTForward = _probeController.ProbeTipT.forward;
            var offsetAdjustedRelativeTargetPositionWorldT = Vector3.ProjectOnPlane(
                relativePositionWorldT,
                probeTipTForward
            );
            var offsetAdjustedTargetCoordinateWorldT =
                targetWorldT + offsetAdjustedRelativeTargetPositionWorldT;

            // Converting worldT to AtlasT (to capture new Bregma offset when there is scaling)
            // then switch axes to get APMLDV.
            var offsetAdjustedTargetCoordinateAtlasT =
                BrainAtlasManager.ActiveReferenceAtlas.World2Atlas(
                    offsetAdjustedTargetCoordinateWorldT
                );
            var offsetAdjustedTargetCoordinateT = BrainAtlasManager.ActiveAtlasTransform.U2T_Vector(
                offsetAdjustedTargetCoordinateAtlasT
            );

            // Cache the computed values.
            _cachedTargetCoordinate = targetInsertion.APMLDV;
            _cachedOffsetAdjustedTargetCoordinate = offsetAdjustedTargetCoordinateT;

            return _cachedOffsetAdjustedTargetCoordinate;
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
        /// <returns>Distance in mm to the target from the probe.</returns>
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

        #endregion
    }
}
