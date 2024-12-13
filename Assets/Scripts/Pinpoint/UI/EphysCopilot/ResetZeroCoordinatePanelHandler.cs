using System;
using System.Globalization;
using EphysLink;
using TMPro;
using UnityEngine;

namespace Pinpoint.UI.EphysCopilot
{
    public class ResetZeroCoordinatePanelHandler : MonoBehaviour
    {
        #region Unity

        private void Start()
        {
            _manipulatorIDText.text =
                "Manipulator " + ProbeManager.ManipulatorBehaviorController.ManipulatorID;
            _manipulatorIDText.color = ProbeManager.Color;
        }

        #endregion

        #region UI Functions

        /// <summary>
        ///     Reset zero coordinate of the manipulator
        /// </summary>
        public void ResetZeroCoordinate()
        {
            CommunicationManager.Instance.GetPosition(
                ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                zeroCoordinate =>
                {
                    ProbeManager.ManipulatorBehaviorController.ReferenceCoordinateOffset =
                        zeroCoordinate;
                    ProbeManager.ManipulatorBehaviorController.BrainSurfaceOffset = 0;
                }
            );

            // Log event.
            OutputLog.Log(
                new[]
                {
                    "Copilot",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    "ResetZeroCoordinate",
                    ProbeManager.ManipulatorBehaviorController.ManipulatorID,
                    ProbeManager.ManipulatorBehaviorController.ReferenceCoordinateOffset.ToString()
                }
            );
        }

        #endregion

        #region Components

        [SerializeField]
        private TMP_Text _manipulatorIDText;

        public ProbeManager ProbeManager { private get; set; }

        #endregion
    }
}
