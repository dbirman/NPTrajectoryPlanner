using System;
using System.Globalization;
using EphysLink;
using UnityEngine;

namespace Pinpoint.Probes.ManipulatorBehaviorController
{
    public partial class ManipulatorBehaviorController
    {
        #region Constants

        /// <summary>
        ///     How far the depth axis can be off center of its range before the user is warned.
        /// </summary>
        /// <remarks>Applies to 3-axis manipulators when calibrating.</remarks>
        private const float CENTER_DEVIATION_FACTOR = 0.125f;

        #endregion

        /// <summary>
        ///     Reset reference coordinate of the manipulator
        /// </summary>
        /// <remarks>
        ///     Alerts user of 4-axis manipulators if depth axis is not at 0 and 3-axis manipulators if depth is too far from
        ///     center.
        /// </remarks>
        public async Awaitable<bool> ResetReferenceCoordinate()
        {
            // Query current position.
            var positionalResponse = await CommunicationManager.Instance.GetPosition(ManipulatorID);

            // Shortcut exit if error.
            if (CommunicationManager.HasError(positionalResponse.Error))
                return false;

            // Setup callback completion source.
            var canDoResetCompletionSource = new AwaitableCompletionSource<bool>();

            // Setup alert callbacks (continue with reset).
            QuestionDialogue.Instance.YesCallback = () =>
                canDoResetCompletionSource.SetResult(true);
            QuestionDialogue.Instance.NoCallback = () =>
                canDoResetCompletionSource.SetResult(false);

            // Check depth position and alert.
            switch (NumAxes)
            {
                case 3
                    when Mathf.Abs(Dimensions.z / 2f - positionalResponse.Position.w)
                        > CENTER_DEVIATION_FACTOR * Dimensions.z:
                    QuestionDialogue.Instance.NewQuestion(
                        "The depth axis is too far from the center of its range and may not have enough space to reach the target. Are you sure you want to continue?"
                    );
                    break;
                case 4 when positionalResponse.Position.w > Dimensions.z * 0.05f:
                    QuestionDialogue.Instance.NewQuestion(
                        "The depth axis is not retracted and may not have enough space to reach the target. Are you sure you want to continue?"
                    );
                    break;
                default:
                    canDoResetCompletionSource.SetResult(true);
                    break;
            }

            // Wait for verification to continue.
            var canDoReset = await canDoResetCompletionSource.Awaitable;

            // Shortcut exit if reset is canceled.
            if (!canDoReset)
                return false;

            // Complete reset.
            ReferenceCoordinateOffset = positionalResponse.Position;
            BrainSurfaceOffset = 0;

            // Log event.
            OutputLog.Log(
                new[]
                {
                    "Automation",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "ResetReferenceCoordinate",
                    ManipulatorID,
                    ReferenceCoordinateOffset.ToString()
                }
            );
            return true;
        }
    }
}
