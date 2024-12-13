using UI.States;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    /// <summary>
    ///     UI handler for main panels.
    /// </summary>
    public class UIPanelHandler : MonoBehaviour
    {
        #region Components

        #region State

        /// <summary>
        ///     UI panel state.
        /// </summary>
        [SerializeField]
        private UIPanelState _state;

        #endregion

        #region Document

        /// <summary>
        ///     Base UI document.
        /// </summary>
        [SerializeField]
        private UIDocument _uiDocument;

        /// <summary>
        ///     Accessor for the root visual element of the UI document.
        /// </summary>
        private VisualElement _root => _uiDocument.rootVisualElement;

        #endregion


        #region Left Side Panel

        /// <summary>
        ///     Left side panel visual element.
        /// </summary>
        private VisualElement _leftSidePanel;

        /// <summary>
        ///     Toggle button for left side panel.
        /// </summary>
        private Button _leftSidePanelToggleButton;

        #endregion

        #region Right Side Panel

        /// <summary>
        ///     Right side panel visual element.
        /// </summary>
        private VisualElement _rightSidePanel;

        /// <summary>
        ///     Toggle button for right side panel.
        /// </summary>
        private Button _rightSidePanelToggleButton;

        #endregion

        #endregion

        #region Unity

        /// <summary>
        ///     Get components and register callbacks.
        /// </summary>
        private void OnEnable()
        {
            // Get components.
            _leftSidePanel = _root.Q("left-side-panel");
            _rightSidePanel = _root.Q("right-side-panel");

            _leftSidePanelToggleButton = _leftSidePanel.Q<Button>("toggle-button");
            _rightSidePanelToggleButton = _rightSidePanel.Q<Button>("toggle-button");

            // Register callbacks.
            _leftSidePanelToggleButton.clicked += ToggleLeftSidePanel;
            _rightSidePanelToggleButton.clicked += ToggleRightSidePanel;
        }

        /// <summary>
        ///     Unregister callbacks.
        /// </summary>
        private void OnDisable()
        {
            // Unregister callbacks.
            _leftSidePanelToggleButton.clicked -= ToggleLeftSidePanel;
            _rightSidePanelToggleButton.clicked -= ToggleRightSidePanel;
        }

        #endregion

        #region UI Functions

        /// <summary>
        ///     Toggle the left side panel open or closed.
        /// </summary>
        private void ToggleLeftSidePanel()
        {
            _state.IsLeftSidePanelOpen = !_state.IsLeftSidePanelOpen;
        }

        /// <summary>
        ///     Toggle the right side panel open or closed.
        /// </summary>
        private void ToggleRightSidePanel()
        {
            _state.IsRightSidePanelOpen = !_state.IsRightSidePanelOpen;
        }

        #endregion
    }
}
