using System;
using System.Globalization;
using EphysLink;
using UnityEngine;

namespace Pinpoint.Probes.ManipulatorBehaviorController
{
    /// <summary>
    ///     Manage the Dura calibration of the manipulator.
    /// </summary>
    public partial class ManipulatorBehaviorController
    {
        #region Properties

        /// <summary>
        ///     Record of the manipulator's depth at the Dura.
        /// </summary>
        private float _duraDepth;

        /// <summary>
        ///     AP, ML, DV coordinate of the Dura.
        /// </summary>
        private Vector3 _duraCoordinate;

        /// <summary>
        ///     Skip passing through the exit margin.
        /// </summary>
        private bool _skipExitMargin;

        #endregion

        /// <summary>
        ///     Reset the dura offset of the probe and enable the next step
        /// </summary>
        /// <returns>True if the dura offset was reset successfully, false otherwise.</returns>
        public async Awaitable<bool> ResetDuraOffset()
        {
            // Get the current manipulator depth.
            var positionResponse = await CommunicationManager.Instance.GetPosition(ManipulatorID);
            if (CommunicationManager.HasError(positionResponse.Error))
                return false;
            
            // Save the Dura's position.
            _duraDepth = positionResponse.Position.w;

            // Check if there is enough room for exit margin.
            var continueWithDuraResetCompletionSource = new AwaitableCompletionSource<bool>();

            // Alert user if there is not enough space for exit margin.
            if (_duraDepth < 1.5f * DURA_MARGIN_DISTANCE)
            {
                QuestionDialogue.Instance.NewQuestion(
                    "The depth axis is too retracted and does not leave enough space for a safe exit. Are you sure you want to continue (safety measures will be skipped)?"
                );
                QuestionDialogue.Instance.YesCallback = () =>
                {
                    continueWithDuraResetCompletionSource.SetResult(true);

                    // Mark that they are ok with skipping the exit margin.
                    _skipExitMargin = true;
                };
                QuestionDialogue.Instance.NoCallback = () =>
                    continueWithDuraResetCompletionSource.SetResult(false);
            }
            else
            {
                continueWithDuraResetCompletionSource.SetResult(true);
            }

            // Shortcut exit if user does not want to continue.
            if (!await continueWithDuraResetCompletionSource.Awaitable)
                return false;

            // Reset dura offset.
            ComputeBrainSurfaceOffset();

            // Force update probe position.
            await UpdateProbePositionFromManipulator();

            // Save the probe's coordinates at the Dura.
            _duraCoordinate = _probeController.Insertion.APMLDV;
            
            // Log the event.
            OutputLog.Log(
                new[]
                {
                    "Automation",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "ResetDuraOffset",
                    ManipulatorID,
                    BrainSurfaceOffset.ToString(CultureInfo.InvariantCulture)
                }
            );

            // Return success.
            return true;
        }
    }
}
