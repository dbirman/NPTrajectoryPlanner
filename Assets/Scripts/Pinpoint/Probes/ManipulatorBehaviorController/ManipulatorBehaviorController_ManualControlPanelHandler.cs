using System;
using System.Globalization;
using EphysLink;
using UnityEngine;

namespace Pinpoint.Probes.ManipulatorBehaviorController
{
    public partial class ManipulatorBehaviorController
    {
        /// <summary>
        ///     Drive the manipulator back to the reference coordinate position.
        /// </summary>
        public async Awaitable<bool> MoveBackToReferenceCoordinate()
        {
            // Set moving state.
            IsMoving = true;

            // Set state back to be calibrated if it has been calibrated.
            if (ProbeAutomationStateManager.IsCalibrated())
                ProbeAutomationStateManager.SetCalibrated();

            // Log start of movement.
            OutputLog.Log(
                new[]
                {
                    "ManualControl",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "MoveBackToReferenceCoordinate",
                    ManipulatorID,
                    "Start"
                }
            );

            // Send move command
            var setPositionResponse = await CommunicationManager.Instance.SetPosition(
                new SetPositionRequest(
                    ManipulatorID,
                    ReferenceCoordinateOffset,
                    AUTOMATIC_MOVEMENT_SPEED
                )
            );

            // Reset moving state.
            IsMoving = false;

            // Log end of movement.
            OutputLog.Log(
                new[]
                {
                    "ManualControl",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "MoveBackToReferenceCoordinate",
                    ManipulatorID,
                    "End"
                }
            );

            return !CommunicationManager.HasError(setPositionResponse.Error);
        }

        /// <summary>
        ///     Stop the manipulator from returning to the reference coordinate position.
        /// </summary>
        public async void StopReturnToReferenceCoordinate()
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
                    "ManualControl",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "MoveBackToReferenceCoordinate",
                    ManipulatorID,
                    "Stop"
                }
            );
        }
    }
}
