using System;
using System.Collections.Generic;
using SensapexLink;
using TMPro;
using TrajectoryPlanner;
using UnityEngine;
using UnityEngine.UI;

namespace TP_Settings
{
    public class ProbeConnectionSettingsPanel : MonoBehaviour
    {
        #region Setup

        /// <summary>
        ///     Initialize components
        /// </summary>
        private void Awake()
        {
            _communicationManager = GameObject.Find("SensapexLink").GetComponent<CommunicationManager>();
            _questionDialogue = GameObject.Find("MainCanvas").transform.Find("QuestionDialoguePanel").gameObject
                .GetComponent<TP_QuestionDialogue>();
        }

        private void FixedUpdate()
        {
            connectButton.interactable = _communicationManager.IsConnected() && manipulatorIdDropdown.value > 0;
        }

        #endregion

        #region Components

        #region Serialized

        [SerializeField] private TMP_Text probeIdText;
        [SerializeField] private TMP_Dropdown manipulatorIdDropdown;
        [SerializeField] private Button connectButton;
        [SerializeField] private TMP_Text connectButtonText;
        [SerializeField] private TMP_InputField phiInputField;
        [SerializeField] private TMP_InputField thetaInputField;
        [SerializeField] private TMP_InputField spinInputField;
        [SerializeField] private TMP_InputField xInputField;
        [SerializeField] private TMP_InputField yInputField;
        [SerializeField] private TMP_InputField zInputField;
        [SerializeField] private TMP_InputField dInputField;

        #endregion

        private CommunicationManager _communicationManager;
        private ProbeManager _probeManager;
        private TP_QuestionDialogue _questionDialogue;

        #endregion

        #region Property Getters and Setters

        /// <summary>
        ///     Set probe manager reference attached to this panel.
        /// </summary>
        /// <param name="probeManager">This panel's probe's corresponding probe manager</param>
        public void SetProbeManager(ProbeManager probeManager)
        {
            _probeManager = probeManager;

            probeIdText.text = probeManager.GetID().ToString();
            probeIdText.color = probeManager.GetColor();

            connectButtonText.text = probeManager.IsConnectedToManipulator() ? "Disconnect" : "Connect";
        }

        /// <summary>
        ///     Return attached probe manager.
        /// </summary>
        /// <returns>Probe manager for related probe</returns>
        public ProbeManager GetProbeManager()
        {
            return _probeManager;
        }

        /// <summary>
        ///     Set probe angles by using the values in the input fields.
        /// </summary>
        public void SetAngles()
        {
            _probeManager.SetProbeAngles(new Vector3(
                float.Parse(phiInputField.text == "" ? "0" : phiInputField.text),
                float.Parse(thetaInputField.text == "" ? "0" : thetaInputField.text),
                float.Parse(spinInputField.text == "" ? "0" : spinInputField.text)
            ));
        }

        /// <summary>
        ///     Set probe bregma offset by using the values in the input fields.
        /// </summary>
        public void SetBregmaOffset()
        {
            _probeManager.SetBregmaOffset(new Vector4(
                float.Parse(xInputField.text == "" ? "0" : xInputField.text),
                float.Parse(yInputField.text == "" ? "0" : yInputField.text),
                float.Parse(zInputField.text == "" ? "0" : zInputField.text),
                float.Parse(dInputField.text == "" ? "0" : dInputField.text)
            ));
        }

        #endregion

        #region Component Methods

        /// <summary>
        ///     Set manipulator id dropdown options.
        /// </summary>
        /// <param name="idOptions">Available manipulators to pick from</param>
        public void SetManipulatorIdDropdownOptions(List<string> idOptions)
        {
            manipulatorIdDropdown.ClearOptions();
            manipulatorIdDropdown.AddOptions(idOptions);

            // Select the option corresponding to the current manipulator id
            var indexOfId = _probeManager.IsConnectedToManipulator()
                ? Math.Max(0, idOptions.IndexOf(_probeManager.GetManipulatorId().ToString()))
                : 0;
            manipulatorIdDropdown.SetValueWithoutNotify(indexOfId);
            connectButton.interactable = indexOfId != 0;
        }

        public void OnManipulatorDropdownValueChanged(int value)
        {
            connectButton.interactable = value != 0;
        }

        /// <summary>
        ///     Connect and register the selected manipulator.
        /// </summary>
        public void ConnectDisconnectProbeToManipulator()
        {
            // Disconnect if already connected
            if (_probeManager.IsConnectedToManipulator())
            {
                _probeManager.SetSensapexLinkMovement(false, 0, false, () => { connectButtonText.text = "Connect"; });
            }
            // Connect otherwise
            else
            {
                // Is at bregma prompt
                _questionDialogue.SetNoCallback(() => { });
                _questionDialogue.SetYesCallback(() =>
                {
                    // Connect to manipulator
                    _probeManager.SetSensapexLinkMovement(true,
                        int.Parse(manipulatorIdDropdown.options[manipulatorIdDropdown.value].text), true,
                        () => { connectButtonText.text = "Disconnect"; });
                });
                _questionDialogue.NewQuestion("Is this manipulator at Bregma?");
            }
        }

        #endregion
    }
}