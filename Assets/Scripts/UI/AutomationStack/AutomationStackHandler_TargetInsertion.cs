using System;
using System.Collections.Generic;
using System.Linq;
using Pinpoint.Probes;
using UI.States;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.AutomationStack
{
    /// <summary>
    ///     Handles picking and driving to a target insertion in the Automation Stack.<br />
    /// </summary>
    public partial class AutomationStackHandler : MonoBehaviour
    {
        #region Properties

        /// <summary>
        ///     Cached copy of the target insertion options.
        /// </summary>
        private IEnumerable<string> _targetInsertionOptionsCache = Enumerable.Empty<string>();

        #endregion

        #region Implementations

        private partial void UpdateTargetInsertionOptionsRadioButtonColors()
        {
            // Shortcut exit if the target insertion options have not changed.
            if (
                _targetInsertionOptionsCache.SequenceEqual(_targetInsertionRadioButtonGroup.choices)
            )
                return;

            // Update the target insertion options cache.
            _targetInsertionOptionsCache = _targetInsertionRadioButtonGroup.choices.ToList();

            // Loop through each child in the target insertion radio button group (skipping first option, "None").
            for (var i = 1; i < _targetInsertionRadioButtonGroup.contentContainer.childCount; i++)
            {
                // Get button visual element.
                var buttonVisualElement = _targetInsertionRadioButtonGroup
                    .contentContainer[i]
                    .Q("unity-checkmark")
                    .parent;

                // Set the color of the button visual element.
                buttonVisualElement.style.backgroundColor = _state
                    .SurfaceCoordinateStringToTargetInsertionOptionProbeManagers[
                        _state.TargetInsertionOptions.ElementAt(i)
                    ]
                    .Color;
            }
        }

        private partial void FlushTargetInsertionOptionsCache()
        {
            if (_targetInsertionOptionsCache.Count() != 0)
                _targetInsertionOptionsCache = Enumerable.Empty<string>();
        }

        private partial void OnTargetInsertionSelectionChanged(ChangeEvent<int> changeEvent)
        {
            // TODO: Change to throw exception if invariant is violated once update issue is resolved.
            // Shortcut exit if invariant is violated.
            if (!_state.IsEnabled)
                return;
            // throw new InvalidOperationException(
            //     "Cannot select target insertion if automation is not enabled on probe "
            //         + ProbeManager.ActiveProbeManager.name
            // );

            switch (changeEvent.newValue)
            {
                // Ignore reset state (-1).
                case -1:
                    return;
                // Shortcut to deselection for "None" (0).
                case 0:
                    // Remove existing trajectory.
                    ActiveManipulatorBehaviorController.ComputeEntryCoordinateTrajectory(null);

                    // ReSharper disable once GrammarMistakeInComment
                    // Reset probe state to calibrated if it was calibrated before.
                    if (ActiveProbeStateManager.IsCalibrated())
                        ActiveProbeStateManager.SetCalibrated();
                    return;
            }

            // Get target insertion probe manager.
            var targetInsertionProbeManager =
                _state.SurfaceCoordinateStringToTargetInsertionOptionProbeManagers[
                    _state.TargetInsertionOptions.ElementAt(changeEvent.newValue)
                ];

            // Compute entry coordinate trajectory.
            var entryCoordinate =
                ActiveManipulatorBehaviorController.ComputeEntryCoordinateTrajectory(
                    targetInsertionProbeManager
                );

            // Shortcut exit if it is negative infinity (no trajectory, probably finished driving).
            if (float.IsNegativeInfinity(entryCoordinate.x))
                return;

            // Skip checking if the target insertion is out of bounds if the user has already acknowledged it.
            if (
                _state.AcknowledgedTargetInsertionIsOutOfBoundsProbes.Contains(
                    ProbeManager.ActiveProbeManager
                )
            )
                return;

            // Check if entry coordinate is out of bounds.
            if (
                !ActiveManipulatorBehaviorController.IsAPMLDVWithinManipulatorBounds(
                    entryCoordinate
                )
            )
            {
                // Prompt user acknowledgement.
                QuestionDialogue.Instance.NewQuestion(
                    "This insertion's entry coordinate is outside the bounds of the manipulator. Are you sure you want to continue?"
                );

                // Record that user has acknowledged the entry coordinate is out of bounds.
                QuestionDialogue.Instance.YesCallback = () =>
                {
                    _state.AcknowledgedTargetInsertionIsOutOfBoundsProbes.Add(
                        ProbeManager.ActiveProbeManager
                    );

                    // Then also check if the final insertion is out of bounds.
                    CheckFinalInsertionIsOutOfBounds();
                };

                // Reset the target insertion radio button group to "None".
                QuestionDialogue.Instance.NoCallback = () =>
                    _targetInsertionRadioButtonGroup.value = 0;
            }
            // Check if the final insertion is out of bounds too.
            else
            {
                CheckFinalInsertionIsOutOfBounds();
            }

            return;

            void CheckFinalInsertionIsOutOfBounds()
            {
                // Shortcut exit if the target insertion is within the manipulator bounds.
                if (
                    ActiveManipulatorBehaviorController.IsAPMLDVWithinManipulatorBounds(
                        targetInsertionProbeManager.ProbeController.Insertion.APMLDV
                    )
                )
                    return;

                // Prompt user acknowledgement.
                QuestionDialogue.Instance.NewQuestion(
                    "This insertion is outside the bounds of the manipulator. Are you sure you want to continue?"
                );

                // Record that user has acknowledged the target insertion is out of bounds.
                QuestionDialogue.Instance.YesCallback = () =>
                    _state.AcknowledgedTargetInsertionIsOutOfBoundsProbes.Add(
                        ProbeManager.ActiveProbeManager
                    );

                // Reset the target insertion radio button group to "None".
                QuestionDialogue.Instance.NoCallback = () =>
                    _targetInsertionRadioButtonGroup.value = 0;
            }
        }

        private async partial void OnDriveToTargetEntryCoordinatePressed()
        {
            // Throw exception if invariant is violated.
            if (!_state.IsEnabled || !ActiveProbeStateManager.IsCalibrated())
                throw new InvalidOperationException(
                    $"Cannot drive {ProbeManager.ActiveProbeManager.name} to target insertion if not enabled and not calibrated to reference coordinate."
                );

            // If the probe is moving, call stop.
            if (
                ActiveProbeStateManager.ProbeAutomationState
                == ProbeAutomationState.DrivingToTargetEntryCoordinate
            )
            {
                // Call stop and wait.
                if (await ActiveManipulatorBehaviorController.StopDriveToTargetEntryCoordinate())
                    // Reset probe to be calibrated on stop.
                    ActiveProbeStateManager.SetCalibrated();
            }
            else
            {
                // Set probe to be driving to target entry coordinate.
                ActiveProbeStateManager.SetDrivingToTargetEntryCoordinate();

                // Send drive command.
                if (await ActiveManipulatorBehaviorController.DriveToTargetEntryCoordinate())
                    // Mark as at entry coordinate when completed.
                    ActiveProbeStateManager.SetAtEntryCoordinate();
            }
        }

        #endregion
    }
}
