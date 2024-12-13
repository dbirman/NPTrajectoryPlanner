using Core.Util;
using Pinpoint.Probes;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.States
{
    [CreateAssetMenu]
    public class ManualControlPanelState : ResettingScriptableObject
    {
        /// <summary>
        ///     Stack enabled state.
        /// </summary>
        /// <remarks>Enabled if there is an active probe controlled by Ephys Link and not inside the brain.</remarks>
        [CreateProperty]
        // ReSharper disable once MemberCanBePrivate.Global
        // ReSharper disable once MemberCanBeMadeStatic.Global
        public bool IsPanelEnabled =>
            ProbeManager.ActiveProbeManager
            && ProbeManager.ActiveProbeManager.IsEphysLinkControlled
            && ProbeManager
                .ActiveProbeManager
                .ManipulatorBehaviorController
                .ProbeAutomationStateManager
                .ProbeAutomationState < ProbeAutomationState.DrivingToNearTarget;

        /// <summary>
        ///     Control enabled state.
        /// </summary>
        public bool IsControlEnabled;

        /// <summary>
        ///     Visibility of the return to reference coordinates button.
        /// </summary>
        /// <returns>True if the control is enabled (even if the panel is disabled), false otherwise.</returns>
        [CreateProperty]
        public DisplayStyle ReturnToReferenceCoordinatesButtonDisplayStyle =>
            IsControlEnabled ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        ///     Return to reference coordinates button text.
        /// </summary>
        /// <returns>"Stop" if the probe is moving back, normal text otherwise.</returns>
        [CreateProperty]
        public string ReturnToReferenceCoordinatesButtonText =>
            IsPanelEnabled && ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.IsMoving
                ? "Stop"
                : "Return to Reference Coordinate";
    }
}
