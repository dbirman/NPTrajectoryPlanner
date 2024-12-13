using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EphysLink;
using KS.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// using Process = KS.Diagnostics.Process;

namespace Pinpoint.UI.EphysLinkSettings
{
    /// <summary>
    ///     Settings menu to connect to the Ephys Link server and manage probe-manipulator bindings.
    /// </summary>
    public class EphysLinkSettings : MonoBehaviour
    {
        #region Constants

        private const string EPHYS_LINK_NAME = "EphysLink-v2.0.0b7";
        private static string EphysLinkExePath =>
            Path.Combine(
                Application.streamingAssetsPath,
                Path.Combine(EPHYS_LINK_NAME, $"{EPHYS_LINK_NAME}.exe")
            );

        #endregion

        #region Components

        // Server connection
        [SerializeField]
        private TMP_Dropdown _manipulatorTypeDropdown;

        [SerializeField]
        private TMP_InputField _pathfinderPortInputField;

        [SerializeField]
        private Button _launchEphysLinkButton;

        [SerializeField]
        private GameObject _existingServerGroup;

        [SerializeField]
        private TMP_InputField _ipAddressInputField;

        [SerializeField]
        private InputField _portInputField;

        [SerializeField]
        private GameObject _proxyServerGroup;

        [SerializeField]
        private TMP_InputField _proxyAddressInputField;

        [SerializeField]
        private TMP_InputField _pinpointIDInputField;

        [SerializeField]
        private GameObject _connectButton;

        [SerializeField]
        private Text _connectButtonText;

        [SerializeField]
        private TMP_Text _connectionErrorText;

        // Manipulators
        [SerializeField]
        private GameObject _manipulatorList;

        [SerializeField]
        private GameObject _manipulatorConnectionPanelPrefab;

        [SerializeField]
        private Toggle _copilotToggle;

        private UIManager _uiManager;

        [SerializeField] private GameObject _newUI;

        #endregion

        #region Properties
        

        private readonly Dictionary<
            string,
            (ManipulatorConnectionPanel manipulatorConnectionSettingsPanel, GameObject gameObject)
        > _manipulatorIdToManipulatorConnectionSettingsPanel = new();

        public HashSet<ProbeManager> LinkedProbes { get; } = new();
        public UnityEvent ShouldUpdateProbesListEvent { get; } = new();

        private Process _ephysLinkProcess;

        #endregion

        #region Unity

        private void Awake()
        {
            // Get/Set Components
            _uiManager = GameObject.Find("MainCanvas").GetComponent<UIManager>();
        }

        private void OnEnable()
        {
            // Update UI elements every time the settings panel is opened
            UpdateConnectionPanel();
        }

        private void OnDestroy()
        {
            KillEphysLinkProcess();
        }

        #endregion

        #region UI Functions

        public void OnTypeChanged(int type)
        {
            print("Type changed to " + type);
            // Show/hide extra groups based on connection type
            _existingServerGroup.SetActive(type == _manipulatorTypeDropdown.options.Count - 2);
            _proxyServerGroup.SetActive(type == _manipulatorTypeDropdown.options.Count - 1);
            _connectButton.SetActive(type >= _manipulatorTypeDropdown.options.Count - 2);
            _launchEphysLinkButton.gameObject.SetActive(
                type < _manipulatorTypeDropdown.options.Count - 2
            );
            _pathfinderPortInputField.gameObject.SetActive(type == 2);

            // Save settings
            Settings.EphysLinkManipulatorType = type;
        }

        public void OnPathfinderPortChanged(string port)
        {
            Settings.EphysLinkPathfinderPort = int.Parse(port);
        }

        private void UpdateManipulatorPanels()
        {
            // Default Copilot to be disabled unless the right manipulator type is found

            if (CommunicationManager.Instance.IsConnected)
            {
                CommunicationManager.Instance.GetManipulators(
                    (response) =>
                    {
                        // Keep track of handled manipulator panels
                        var handledManipulatorIds = new HashSet<string>();

                        // Add any new manipulators in scene to list
                        foreach (var manipulatorID in response.Manipulators)
                        {
                            // Create new manipulator connection settings panel if the manipulator is new
                            if (
                                !_manipulatorIdToManipulatorConnectionSettingsPanel.ContainsKey(
                                    manipulatorID
                                )
                            )
                            {
                                // Instantiate panel
                                var manipulatorConnectionSettingsPanelGameObject = Instantiate(
                                    _manipulatorConnectionPanelPrefab,
                                    _manipulatorList.transform
                                );
                                var manipulatorConnectionSettingsPanel =
                                    manipulatorConnectionSettingsPanelGameObject.GetComponent<ManipulatorConnectionPanel>();

                                // Set manipulator id
                                manipulatorConnectionSettingsPanel.Initialize(
                                    this,
                                    manipulatorID,
                                    response.NumAxes
                                );

                                // Add to dictionary
                                _manipulatorIdToManipulatorConnectionSettingsPanel.Add(
                                    manipulatorID,
                                    new ValueTuple<ManipulatorConnectionPanel, GameObject>(
                                        manipulatorConnectionSettingsPanel,
                                        manipulatorConnectionSettingsPanelGameObject
                                    )
                                );
                            }

                            // Mark ID as handled
                            handledManipulatorIds.Add(manipulatorID);
                        }

                        // Remove any manipulators that are not connected anymore
                        foreach (
                            var disconnectedManipulator in _manipulatorIdToManipulatorConnectionSettingsPanel
                                .Keys.Except(handledManipulatorIds)
                                .ToList()
                        )
                        {
                            _manipulatorIdToManipulatorConnectionSettingsPanel.Remove(
                                disconnectedManipulator
                            );
                            Destroy(
                                _manipulatorIdToManipulatorConnectionSettingsPanel[
                                    disconnectedManipulator
                                ].gameObject
                            );
                        }

                        // Reorder panels to match order of availableIds
                        foreach (var manipulatorId in response.Manipulators)
                            _manipulatorIdToManipulatorConnectionSettingsPanel[manipulatorId]
                                .gameObject.transform.SetAsLastSibling();
                    }
                );
            }
            else
            {
                // Clear manipulator panels if not connected
                foreach (
                    var manipulatorPanel in _manipulatorIdToManipulatorConnectionSettingsPanel.Values.Select(
                        value => value.gameObject
                    )
                )
                    Destroy(manipulatorPanel);
                _manipulatorIdToManipulatorConnectionSettingsPanel.Clear();
            }
        }

        /// <summary>
        ///     Launch bundled Ephys Link executable.
        /// </summary>
        public void OnLaunchEphysLinkPressed()
        {
            // Parse manipulator type string arg (invariant: custom connection should never happen).
            var manipulatorTypeString = _manipulatorTypeDropdown.value switch
            {
                1 => "ump-3",
                2 => "pathfinder-mpm",
                3 => "new-scale",
                _ => "ump-4"
            };

            // Make args string (ignore updates, select type).
            var args = $"-i -t {manipulatorTypeString}";

            // Add Pathfinder port if selected.
            if (_manipulatorTypeDropdown.value == 2)
                args += $" --mpm-port {_pathfinderPortInputField.text}";

            _ephysLinkProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = EphysLinkExePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = false,
                    CreateNoWindow = false
                }
            };
            _ephysLinkProcess.Start();

            // Configure UI (disable type dropdown and launch button, enable [dis]connect button).
            _manipulatorTypeDropdown.interactable = false;
            _launchEphysLinkButton.interactable = false;
            _connectButton.SetActive(true);
            _connectButtonText.text = "Connecting...";

            // Attempt to connect to server.
            var attempts = 0;
            ConnectToServer();
            return;

            void ConnectToServer()
            {
                CommunicationManager.Instance.ConnectToServer(
                    "localhost",
                    3000,
                    HandleSuccessfulConnection,
                    err =>
                    {
                        attempts++;
                        if (attempts > 10)
                        {
                            _connectionErrorText.text = err;
                            _connectButtonText.text = "Connect";

                            _manipulatorTypeDropdown.interactable = !CommunicationManager
                                .Instance
                                .IsConnected;
                            _launchEphysLinkButton.interactable = !CommunicationManager
                                .Instance
                                .IsConnected;
                        }
                        else
                        {
                            ConnectToServer();
                        }
                    }
                );
            }
        }

        /// <summary>
        ///     Handle when connect/disconnect button is pressed.
        /// </summary>
        public void OnConnectDisconnectPressed()
        {
            if (!CommunicationManager.Instance.IsConnected)
            {
                // Check if ID is empty if using proxy server
                if (
                    Settings.EphysLinkManipulatorType == _manipulatorTypeDropdown.options.Count - 1
                    && string.IsNullOrEmpty(_pinpointIDInputField.text)
                )
                {
                    _connectionErrorText.text = "Please enter a Pinpoint ID.";
                    return;
                }

                // Attempt to connect to server
                try
                {
                    _connectButtonText.text = "Connecting...";

                    // Provide default values for IP and port if empty then connect to proxy or server.
                    if (
                        Settings.EphysLinkManipulatorType
                        == _manipulatorTypeDropdown.options.Count - 1
                    )
                    {
                        if (string.IsNullOrEmpty(_proxyAddressInputField.text))
                            _proxyAddressInputField.text = "proxy2.virtualbrainlab.org";

                        CommunicationManager.Instance.ConnectToProxy(
                            _proxyAddressInputField.text,
                            _pinpointIDInputField.text,
                            HandleSuccessfulConnection,
                            err =>
                            {
                                _connectionErrorText.text = err;
                                _connectButtonText.text = "Connect";
                            }
                        );
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(_ipAddressInputField.text))
                            _ipAddressInputField.text = "localhost";
                        if (string.IsNullOrEmpty(_portInputField.text))
                            _portInputField.text = "3000";

                        CommunicationManager.Instance.ConnectToServer(
                            _ipAddressInputField.text,
                            int.Parse(_portInputField.text),
                            HandleSuccessfulConnection,
                            err =>
                            {
                                _connectionErrorText.text = err;
                                _connectButtonText.text = "Connect";
                            }
                        );
                    }
                }
                catch (Exception e)
                {
                    _connectionErrorText.text = e.Message;
                }
            }
            else
            {
                // Disconnect from server
                QuestionDialogue.Instance.YesCallback = HandleDisconnectingFromServer;

                QuestionDialogue.Instance.NewQuestion(
                    "Are you sure you want to disconnect?\nAll incomplete movements will be canceled."
                );
            }
        }

        /// <summary>
        ///     Toggle Ephys Copilot panel
        /// </summary>
        public void ToggleCopilotPanel(bool isEnabled)
        {
            if (_uiManager != null)
                _uiManager.EnableEphysCopilotPanel(isEnabled);
        }

        public void InvokeShouldUpdateProbesListEvent()
        {
            ShouldUpdateProbesListEvent.Invoke();
        }
        
        public void ToggleNewUI(bool isEnabled)
        {
            _newUI.SetActive(isEnabled);
        }

        #endregion

        #region Internal functions

        private void HandleSuccessfulConnection()
        {
            // Check Ephys Link version
            CommunicationManager.Instance.VerifyVersion(
                () =>
                {
                    // Ephys Link is current enough
                    CommunicationManager.Instance.IsEphysLinkCompatible = true;
                    UpdateConnectionPanel();
                },
                () =>
                {
                    CommunicationManager.Instance.DisconnectFromServer(() =>
                    {
                        _connectionErrorText.text =
                            "Ephys Link is outdated. Please update to "
                            + CommunicationManager.EPHYS_LINK_MIN_VERSION_STRING;
                        _connectButtonText.text = "Connect";
                    });
                }
            );
        }

        private void HandleDisconnectingFromServer()
        {
            foreach (
                var probeManager in ProbeManager.Instances.Where(probeManager =>
                    probeManager.IsEphysLinkControlled
                )
            )
            {
                probeManager.SetIsEphysLinkControlled(
                    false,
                    probeManager.ManipulatorBehaviorController.ManipulatorID
                );
            }

            CommunicationManager.Instance.DisconnectFromServer(() =>
            {
                KillEphysLinkProcess();
                UpdateConnectionPanel();
            });
        }

        /// <summary>
        ///     Populate UI elements with current connection settings.
        /// </summary>
        private void UpdateConnectionPanel()
        {
            if (
                CommunicationManager.Instance.IsConnected
                && !CommunicationManager.Instance.IsEphysLinkCompatible
            )
            {
                _connectionErrorText.text =
                    "Ephys Link is outdated. Please update to "
                    + CommunicationManager.EPHYS_LINK_MIN_VERSION_STRING;
                _connectButtonText.text = "Connect";
                CommunicationManager.Instance.DisconnectFromServer();
                return;
            }

            // Connection UI
            _connectionErrorText.text = "";
            _connectButtonText.text = CommunicationManager.Instance.IsConnected
                ? "Disconnect"
                : "Connect";
            _connectButton.SetActive(
                CommunicationManager.Instance.IsConnected
                    || _manipulatorTypeDropdown.value >= _manipulatorTypeDropdown.options.Count - 2
            );

            _manipulatorTypeDropdown.interactable = !CommunicationManager.Instance.IsConnected;
            _launchEphysLinkButton.interactable = !CommunicationManager.Instance.IsConnected;

            // Update Manipulator Panels
            UpdateManipulatorPanels();
        }

        private void KillEphysLinkProcess()
        {
            if (_ephysLinkProcess == null)
                return;
            _ephysLinkProcess.Kill(true);
            _ephysLinkProcess.Dispose();
            _ephysLinkProcess = null;
        }

        #endregion
    }
}
