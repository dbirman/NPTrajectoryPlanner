using System;
using UI.States;
using UnityEngine;
using UnityEngine.UIElements;

namespace UI
{
    public class ManualControlPanelHandler : MonoBehaviour
    {
        #region Components

        #region State

        /// <summary>
        ///     Manual control panel state.
        /// </summary>
        [SerializeField]
        private ManualControlPanelState _state;

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

        /// <summary>
        ///     Base panel.
        /// </summary>
        private VisualElement _manualControlPanel;

        #endregion

        /// <summary>
        ///     Button for returning to the reference coordinate.
        /// </summary>
        private Button _returnToReferenceCoordinateButton;

        #endregion

        #region Unity

        private void OnEnable()
        {
            // Get components.
            _manualControlPanel = _root.Q("manual-control-panel");
            _returnToReferenceCoordinateButton = _manualControlPanel.Q<Button>(
                "return-to-reference-coordinate-button"
            );

            // Register callbacks.
            _returnToReferenceCoordinateButton.clicked += ReturnToReferenceCoordinate;
        }

        private void OnDisable()
        {
            // Unregister callbacks.
            _returnToReferenceCoordinateButton.clicked -= ReturnToReferenceCoordinate;
        }

        #endregion

        #region Functions

        /// <summary>
        ///     Call MoveBackToZeroCoordinate or Stop from the active probe manager's manipulator behavior controller.
        /// </summary>
        /// <exception cref="InvalidOperationException">If manual control is not enabled on the active probe manager.</exception>
        private async void ReturnToReferenceCoordinate()
        {
            if (!_state.IsPanelEnabled)
                throw new InvalidOperationException(
                    "Cannot return to reference coordinate if manual control is not enabled on probe "
                        + ProbeManager.ActiveProbeManager.name
                );

            // Call stop or move depending on if the probe is already moving or not.
            if (ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.IsMoving)
                ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.StopReturnToReferenceCoordinate();
            else
                await ProbeManager.ActiveProbeManager.ManipulatorBehaviorController.MoveBackToReferenceCoordinate();
        }

        #endregion
    }
}
