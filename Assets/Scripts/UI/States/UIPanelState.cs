using Core.Util;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI.States
{
    /// <summary>
    ///     UI State for main panels.
    /// </summary>
    [CreateAssetMenu]
    public class UIPanelState : ResettingScriptableObject
    {
        #region Constants

        /// <summary>
        ///     Unity defined panel background color.
        /// </summary>
        private static readonly Color PANEL_BACKGROUND_COLOR =
            new(0.647058824f, 0.647058824f, 0.647058824f);

        #endregion

        #region Panels

        /// <summary>
        ///     Left side panel opened or closed state.
        /// </summary>
        public bool IsLeftSidePanelOpen;

        /// <summary>
        ///     Right side panel opened or closed state.
        /// </summary>
        public bool IsRightSidePanelOpen;

        /// <summary>
        ///     Conversion of <see cref="IsLeftSidePanelOpen" /> to <see cref="DisplayStyle" /> for display setting.
        /// </summary>
        [CreateProperty]
        public DisplayStyle LeftSidePanelVisibilityDisplayStyle =>
            IsLeftSidePanelOpen ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        ///     Conversion of <see cref="IsRightSidePanelOpen" /> to <see cref="DisplayStyle" /> for display setting.
        /// </summary>
        [CreateProperty]
        public DisplayStyle RightSidePanelVisibilityDisplayStyle =>
            IsRightSidePanelOpen ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        ///     Conversion of <see cref="IsLeftSidePanelOpen" /> to <see cref="PickingMode" /> to allow for click through.
        /// </summary>
        [CreateProperty]
        public PickingMode LeftSidePanelPickingMode =>
            IsLeftSidePanelOpen ? PickingMode.Position : PickingMode.Ignore;

        /// <summary>
        ///     Conversion of <see cref="IsRightSidePanelOpen" /> to <see cref="PickingMode" /> to allow for click through.
        /// </summary>
        [CreateProperty]
        public PickingMode RightSidePanelPickingMode =>
            IsRightSidePanelOpen ? PickingMode.Position : PickingMode.Ignore;

        /// <summary>
        ///     Left side panel close/open button text.
        /// </summary>
        [CreateProperty]
        public string LeftSidePanelToggleButtonText => IsLeftSidePanelOpen ? "<<" : ">>";

        /// <summary>
        ///     Right side panel close/open button text.
        /// </summary>
        [CreateProperty]
        public string RightSidePanelToggleButtonText => IsRightSidePanelOpen ? ">>" : "<<";

        /// <summary>
        ///     Left side panel background color.
        /// </summary>
        /// <remarks>Used for visibility (opaque when visible, clear when not).</remarks>
        [CreateProperty]
        public Color LeftSidePanelBackgroundColor =>
            IsLeftSidePanelOpen ? PANEL_BACKGROUND_COLOR : Color.clear;

        /// <summary>
        ///     Right side panel background color.
        /// </summary>
        /// <remarks>Used for visibility (opaque when visible, clear when not).</remarks>
        [CreateProperty]
        public Color RightSidePanelBackgroundColor =>
            IsRightSidePanelOpen ? PANEL_BACKGROUND_COLOR : Color.clear;

        #endregion

        #region Mode

        /// <summary>
        ///     Mode dropdown selection index.
        /// </summary>
        public int ModeIndex;

        [CreateProperty]
        public string InspectorHeader =>
            ModeIndex switch
            {
                0 => "Probe Inspector",
                1 => "Manipulator Inspector",
                _ => "Automation Pipeline"
            };

        /// <summary>
        ///     Inspector stack visibility.
        /// </summary>
        /// <remarks>Show inspector if not in automation mode (2).</remarks>
        [CreateProperty]
        // ReSharper disable once MemberCanBePrivate.Global
        public DisplayStyle InspectorStackDisplayStyle =>
            ModeIndex != 2 ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        ///     Automation stack visibility.
        /// </summary>
        /// <remarks>Show automation stack only in automation mode (2).</remarks>
        [CreateProperty]
        public DisplayStyle AutomationStackDisplayStyle =>
            ModeIndex == 2 ? DisplayStyle.Flex : DisplayStyle.None;

        /// <summary>
        ///     Manual control stack visibility.
        /// </summary>
        /// <remarks>Don't show in planning mode (0)</remarks>
        [CreateProperty]
        public DisplayStyle ManualControlStackDisplayStyle =>
            ModeIndex != 0 ? DisplayStyle.Flex : DisplayStyle.None;

        #endregion

        #region Inspector Header

        /// <summary>
        ///     Selected probe color.
        /// </summary>
        [CreateProperty]
        public Color ProbeColor =>
            ProbeManager.ActiveProbeManager ? ProbeManager.ActiveProbeManager.Color : Color.black;

        /// <summary>
        ///     Selected probe name.
        /// </summary>
        [CreateProperty]
        public string ProbeName =>
            ProbeManager.ActiveProbeManager
                ? ProbeManager.ActiveProbeManager.name
                : "No Probe Selected";

        #endregion
    }
}
