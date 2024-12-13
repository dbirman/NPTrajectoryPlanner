using System;
using System.Globalization;
using System.IO;
using System.Linq;
using BestHTTP.SocketIO3;
using UnityEngine;

namespace EphysLink
{
    /// <summary>
    ///     WebSocket connection manager between the Trajectory Planner and a running Ephys Link server.
    /// </summary>
    public class CommunicationManager : MonoBehaviour
    {
        #region Components

        public static CommunicationManager Instance;
        private SocketManager _connectionManager;
        private Socket _socket;

        #endregion

        #region Properties

        private static readonly int[] EPHYS_LINK_MIN_VERSION = { 2, 0, 0 };

        public static readonly string EPHYS_LINK_MIN_VERSION_STRING =
            $"≥ v{string.Join(".", EPHYS_LINK_MIN_VERSION)}";

        private const string UNKOWN_EVENT = "{\"error\": \"Unknown event.\"}";

        /// <summary>
        ///     The current state of the connection to Ephys Link.
        /// </summary>
        public bool IsConnected { get; private set; }

        public bool IsEphysLinkCompatible { get; set; }

        #endregion


        #region Unity

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(this);
            }
            else if (Instance != this)
            {
                Destroy(this);
            }
        }

        #endregion

        #region Connection Handler

        public void ServerSettingsLoaded()
        {
            // Automatically connect if the server credentials are possible
            if (
                !IsConnected
                && !string.IsNullOrEmpty(Settings.EphysLinkServerIp)
                && Settings.EphysLinkServerPort >= 1025
            )
                ConnectToServer(
                    Settings.EphysLinkServerIp,
                    Settings.EphysLinkServerPort,
                    () =>
                    {
                        // Verify Ephys Link version
                        VerifyVersion(
                            () => IsEphysLinkCompatible = true,
                            () => Instance.DisconnectFromServer()
                        );
                    }
                );
        }

        /// <summary>
        ///     Create a connection to the server.
        /// </summary>
        /// <param name="ip">IP address of the server</param>
        /// <param name="port">Port of the server</param>
        /// <param name="onConnected">Callback function to handle a successful connection</param>
        /// <param name="onError"></param>
        public void ConnectToServer(
            string ip,
            int port,
            Action onConnected = null,
            Action<string> onError = null
        )
        {
            // Disconnect the old connection if needed
            if (_connectionManager != null && _connectionManager.Socket.IsOpen)
                _connectionManager.Close();

            // Create new connection
            var options = new SocketOptions { Timeout = new TimeSpan(0, 0, 2) };

            // Try to open a connection
            try
            {
                // Create a new socket
                _connectionManager = new SocketManager(new Uri($"http://{ip}:{port}"), options);
                _socket = _connectionManager.Socket;

                // On successful connection
                _socket.Once(
                    "connect",
                    () =>
                    {
                        Debug.Log($"Connected to Ephys Link server at {ip}:{port}");
                        IsConnected = true;

                        // Save settings
                        Settings.EphysLinkServerIp = ip;
                        Settings.EphysLinkServerPort = port;

                        // Invoke connected callback
                        onConnected?.Invoke();
                    }
                );

                // On error
                _socket.Once(
                    "error",
                    () =>
                    {
                        var connectionErrorMessage =
                            $"Error connecting to server at {ip}:{port}. Check server for details.";
                        Debug.LogWarning(connectionErrorMessage);
                        IsConnected = false;
                        _connectionManager.Close();
                        _connectionManager = null;
                        _socket = null;
                        onError?.Invoke(connectionErrorMessage);
                    }
                );

                // On timeout
                _socket.Once(
                    "connect_timeout",
                    () =>
                    {
                        var connectionTimeoutMessage =
                            $"Connection to server at {ip}:{port} timed out";
                        Debug.LogWarning(connectionTimeoutMessage);
                        IsConnected = false;
                        _connectionManager.Close();
                        _connectionManager = null;
                        _socket = null;
                        onError?.Invoke(connectionTimeoutMessage);
                    }
                );
            }
            catch (Exception e)
            {
                // On socket generation error
                var connectionErrorMessage =
                    "Error connecting to server at {ip}:{port}. Check server for details.";
                Debug.LogWarning(connectionErrorMessage);
                Debug.LogWarning("Exception: " + e);
                IsConnected = false;
                _connectionManager = null;
                _socket = null;
                onError?.Invoke(connectionErrorMessage);
            }
        }

        public void ConnectToProxy(
            string proxyAddress,
            string pinpointID,
            Action onConnected = null,
            Action<string> onError = null
        )
        {
            // Disconnect the old connection if needed
            if (_connectionManager != null && _connectionManager.Socket.IsOpen)
                _connectionManager.Close();

            // Create new connection
            var options = new SocketOptions { Timeout = new TimeSpan(0, 0, 2) };

            // Try to open a connection
            try
            {
                // Create a new socket
                _connectionManager = new SocketManager(
                    new Uri($"http://{proxyAddress}:3000"),
                    options
                );
                _socket = _connectionManager.Socket;

                // On successful connection
                _socket.Once(
                    "connect",
                    () =>
                    {
                        Debug.Log($"Connected to proxy server at {proxyAddress}:3000");
                        IsConnected = true;

                        // Save settings and clear Server IP (don't allow auto reconnect)
                        Settings.EphysLinkServerIp = "";
                        Settings.EphysLinkProxyAddress = proxyAddress;
                    }
                );

                _socket.Once(
                    "get_pinpoint_id",
                    () =>
                    {
                        _socket.EmitAck(ToJson(new PinpointIdResponse(pinpointID, true)));
                        onConnected?.Invoke();
                    }
                );

                // On error
                _socket.Once(
                    "error",
                    () =>
                    {
                        var connectionErrorMessage =
                            $"Error connecting to proxy at {proxyAddress}:3000. Check proxy for details.";
                        Debug.LogWarning(connectionErrorMessage);
                        IsConnected = false;
                        _connectionManager.Close();
                        _connectionManager = null;
                        _socket = null;
                        onError?.Invoke(connectionErrorMessage);
                    }
                );

                // On timeout
                _socket.Once(
                    "connect_timeout",
                    () =>
                    {
                        var connectionTimeoutMessage =
                            $"Connection to proxy at {proxyAddress}:3000 timed out";
                        Debug.LogWarning(connectionTimeoutMessage);
                        IsConnected = false;
                        _connectionManager.Close();
                        _connectionManager = null;
                        _socket = null;
                        onError?.Invoke(connectionTimeoutMessage);
                    }
                );
            }
            catch (Exception e)
            {
                // On socket generation error
                var connectionErrorMessage =
                    $"Error connecting to proxy at {proxyAddress}:3000. Check proxy for details.";
                Debug.LogWarning(connectionErrorMessage);
                Debug.LogWarning("Exception: " + e);
                IsConnected = false;
                _connectionManager = null;
                _socket = null;
                onError?.Invoke(connectionErrorMessage);
            }
        }

        /// <summary>
        ///     Disconnect client from WebSocket server.
        /// </summary>
        /// <param name="onDisconnected">Callback function to handle post disconnection behavior</param>
        public void DisconnectFromServer(Action onDisconnected = null)
        {
            _connectionManager.Close();
            IsConnected = false;
            onDisconnected?.Invoke();
        }

        public void VerifyVersion(Action onSuccess, Action onFailure)
        {
            GetVersion(
                versionString =>
                {
                    var versionNumbers = versionString
                        .Split(".")
                        .Select(values => values.TakeWhile(char.IsDigit).ToArray())
                        .TakeWhile(numbers => numbers.Length > 0)
                        .Select(nonEmpty => int.Parse(new string(nonEmpty)))
                        .ToArray();
                    print(versionNumbers[0] + "." + versionNumbers[1] + "." + versionNumbers[2]);

                    // Fail if major version mismatch (breaking changes).
                    if (versionNumbers[0] != EPHYS_LINK_MIN_VERSION[0])
                    {
                        onFailure.Invoke();
                        return;
                    }

                    // Fail if minor version is too small (missing features).
                    if (versionNumbers[1] < EPHYS_LINK_MIN_VERSION[1])
                    {
                        onFailure.Invoke();
                        return;
                    }

                    // Fail if patch version is too small and minor version is not greater (bug fixes).
                    if (
                        versionNumbers[1] == EPHYS_LINK_MIN_VERSION[1]
                        && versionNumbers[2] < EPHYS_LINK_MIN_VERSION[2]
                    )
                    {
                        onFailure.Invoke();
                        return;
                    }

                    // Passed checks.
                    onSuccess.Invoke();
                },
                onFailure.Invoke
            );
        }

        #endregion

        #region Event Handlers

        /// <summary>
        ///     Get Ephys Link version.
        /// </summary>
        /// <param name="onSuccessCallback">Returns the version number in the format "x.y.z"</param>
        /// <param name="onErrorCallback">If the version number is empty or failed to return</param>
        private void GetVersion(Action<string> onSuccessCallback, Action onErrorCallback = null)
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                        onSuccessCallback?.Invoke(data);
                    else
                        onErrorCallback?.Invoke();
                })
                .Emit("get_version");
        }

        /// <summary>
        ///     Get Ephys Link version.
        /// </summary>
        /// <returns>Version number.</returns>
        private async Awaitable<string> GetVersion()
        {
            return await EmitAndGetStringResponse<object>("get_version", null);
        }

        /// <summary>
        ///     Get the platform type.
        /// </summary>
        /// <param name="onSuccessCallback">Callback function to handle incoming platform type info.</param>
        /// <param name="onErrorCallback">Callback function to handle errors.</param>
        public void GetPlatformType(Action<string> onSuccessCallback, Action onErrorCallback = null)
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                        onSuccessCallback?.Invoke(data);
                    else
                        onErrorCallback?.Invoke();
                })
                .Emit("get_platform_type");
        }

        /// <summary>
        ///     Get the platform type.
        /// </summary>
        /// <returns>Platform type.</returns>
        public async Awaitable<string> GetPlatformType()
        {
            return await EmitAndGetStringResponse<object>("get_platform_type", null);
        }

        /// <summary>
        ///     Get manipulators event sender.
        /// </summary>
        /// <param name="onSuccessCallback">Callback function to handle incoming manipulator ID's</param>
        /// <param name="onErrorCallback">Callback function to handle errors</param>
        public void GetManipulators(
            Action<GetManipulatorsResponse> onSuccessCallback,
            Action<string> onErrorCallback = null
        )
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                    {
                        var parsedData = ParseJson<GetManipulatorsResponse>(data);

                        if (string.IsNullOrEmpty(parsedData.Error))
                            onSuccessCallback?.Invoke(parsedData);
                        else
                            onErrorCallback?.Invoke(parsedData.Error);
                    }
                    else
                    {
                        onErrorCallback?.Invoke($"get_manipulators invalid response: {data}");
                    }
                })
                .Emit("get_manipulators");
        }

        /// <summary>
        ///     Get connected manipulators and some basic information about them.
        /// </summary>
        /// <returns>Manipulators and their information.</returns>
        public async Awaitable<GetManipulatorsResponse> GetManipulators()
        {
            return await EmitAndGetResponse<GetManipulatorsResponse, object>(
                "get_manipulators",
                null
            );
        }

        /// <summary>
        ///     Request the current position of a manipulator (mm).
        /// </summary>
        /// <param name="manipulatorId">ID of the manipulator to get the position of</param>
        /// <param name="onSuccessCallback">Callback function to pass manipulator position to</param>
        /// <param name="onErrorCallback">Callback function to handle errors</param>
        public void GetPosition(
            string manipulatorId,
            Action<Vector4> onSuccessCallback,
            Action<string> onErrorCallback = null
        )
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                    {
                        var parsedData = ParseJson<PositionalResponse>(data);

                        if (string.IsNullOrEmpty(parsedData.Error))
                            onSuccessCallback?.Invoke(parsedData.Position);
                        else
                            onErrorCallback?.Invoke(parsedData.Error);
                    }
                    else
                    {
                        onErrorCallback?.Invoke($"get_pos invalid response: {data}");
                    }
                })
                .Emit("get_position", manipulatorId);
        }

        /// <summary>
        ///     Request the current position of a manipulator (mm).
        /// </summary>
        /// <param name="manipulatorId">ID of the manipulator to get teh position of.</param>
        /// <returns><see cref="PositionalResponse" /> with manipulator's position.</returns>
        public async Awaitable<PositionalResponse> GetPosition(string manipulatorId)
        {
            return await EmitAndGetResponse<PositionalResponse, string>(
                "get_position",
                manipulatorId
            );
        }

        /// <summary>
        ///     Request the current angles of a manipulator.
        /// </summary>
        /// <param name="manipulatorId">ID of the manipulator to get the position of</param>
        /// <param name="onSuccessCallback">Callback function to pass manipulator angles to</param>
        /// <param name="onErrorCallback">Callback function to handle errors</param>
        public void GetAngles(
            string manipulatorId,
            Action<Vector3> onSuccessCallback,
            Action<string> onErrorCallback = null
        )
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                    {
                        var parsedData = ParseJson<AngularResponse>(data);
                        if (string.IsNullOrEmpty(parsedData.Error))
                            onSuccessCallback?.Invoke(parsedData.Angles);
                        else
                            onErrorCallback?.Invoke(parsedData.Error);
                    }
                    else
                    {
                        onErrorCallback?.Invoke($"get_angles invalid response: {data}");
                    }
                })
                .Emit("get_angles", manipulatorId);
        }

        /// <summary>
        ///     Request the current angles of a manipulator.
        /// </summary>
        /// <param name="manipulatorId">ID of the manipulator to get the position of</param>
        /// <returns><see cref="AngularResponse" /> with manipulator's angles.</returns>
        public async Awaitable<AngularResponse> GetAngles(string manipulatorId)
        {
            return await EmitAndGetResponse<AngularResponse, string>("get_angles", manipulatorId);
        }

        public void GetShankCount(
            string manipulatorId,
            Action<int> onSuccessCallback,
            Action<string> onErrorCallback = null
        )
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                    {
                        var parsedData = ParseJson<ShankCountResponse>(data);
                        if (string.IsNullOrEmpty(parsedData.Error))
                            onSuccessCallback?.Invoke(parsedData.ShankCount);
                        else
                            onErrorCallback?.Invoke(parsedData.Error);
                    }
                    else
                    {
                        onErrorCallback?.Invoke($"get_shank_count invalid response: {data}");
                    }
                })
                .Emit("get_shank_count", manipulatorId);
        }

        /// <summary>
        ///     Request the number of shanks on a manipulator.
        /// </summary>
        /// <param name="manipulatorId">ID of the manipulator to get the shank count of.</param>
        /// <returns><see cref="ShankCountResponse" /> with the number of shanks.</returns>
        public async Awaitable<ShankCountResponse> GetShankCount(string manipulatorId)
        {
            return await EmitAndGetResponse<ShankCountResponse, string>(
                "get_shank_count",
                manipulatorId
            );
        }

        /// <summary>
        ///     Request a manipulator be moved to a specific position.
        /// </summary>
        /// <remarks>Position is defined by a Vector4</remarks>
        /// <param name="request">Goto position request object</param>
        /// <param name="onSuccessCallback">Callback function to handle successful manipulator movement</param>
        /// <param name="onErrorCallback">Callback function to handle errors</param>
        public void SetPosition(
            SetPositionRequest request,
            Action<Vector4> onSuccessCallback,
            Action<string> onErrorCallback = null
        )
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                    {
                        var parsedData = ParseJson<PositionalResponse>(data);
                        if (string.IsNullOrEmpty(parsedData.Error))
                            onSuccessCallback?.Invoke(parsedData.Position);
                        else
                            onErrorCallback?.Invoke(parsedData.Error);
                    }
                    else
                    {
                        onErrorCallback?.Invoke($"goto_pos invalid response: {data}");
                    }
                })
                .Emit("set_position", ToJson(request));
        }

        /// <summary>
        ///     Request a manipulator be moved to a specific position.
        /// </summary>
        /// <param name="request">Goto position request object</param>
        /// <returns><see cref="PositionalResponse" /> with the manipulator's new position.</returns>
        public async Awaitable<PositionalResponse> SetPosition(SetPositionRequest request)
        {
            return await EmitAndGetResponse<PositionalResponse, SetPositionRequest>(
                "set_position",
                request
            );
        }

        /// <summary>
        ///     Request a manipulator drive down to a specific depth.
        /// </summary>
        /// <param name="request">Drive to depth request</param>
        /// <param name="onSuccessCallback">Callback function to handle successful manipulator movement</param>
        /// <param name="onErrorCallback">Callback function to handle errors</param>
        public void SetDepth(
            SetDepthRequest request,
            Action<float> onSuccessCallback,
            Action<string> onErrorCallback
        )
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                    {
                        var parsedData = ParseJson<SetDepthResponse>(data);
                        if (string.IsNullOrEmpty(parsedData.Error))
                            onSuccessCallback?.Invoke(parsedData.Depth);
                        else
                            onErrorCallback?.Invoke(parsedData.Error);
                    }
                    else
                    {
                        onErrorCallback?.Invoke($"drive_to_depth invalid response: {data}");
                    }
                })
                .Emit("set_depth", ToJson(request));
        }

        /// <summary>
        ///     Request a manipulator drive down to a specific depth.
        /// </summary>
        /// <param name="request">Drive to depth request</param>
        /// <returns><see cref="SetDepthResponse" /> with the manipulator's new depth.</returns>
        public async Awaitable<SetDepthResponse> SetDepth(SetDepthRequest request)
        {
            return await EmitAndGetResponse<SetDepthResponse, SetDepthRequest>(
                "set_depth",
                request
            );
        }

        /// <summary>
        ///     Set the inside brain state of a manipulator.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="onSuccessCallback">Callback function to handle setting inside_brain state successfully</param>
        /// <param name="onErrorCallback">Callback function to handle errors</param>
        public void SetInsideBrain(
            SetInsideBrainRequest request,
            Action<bool> onSuccessCallback,
            Action<string> onErrorCallback = null
        )
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                    {
                        var parsedData = ParseJson<BooleanStateResponse>(data);
                        if (string.IsNullOrEmpty(parsedData.Error))
                            onSuccessCallback?.Invoke(parsedData.State);
                        else
                            onErrorCallback?.Invoke(parsedData.Error);
                    }
                    else
                    {
                        onErrorCallback?.Invoke($"set_inside_brain invalid response: {data}");
                    }
                })
                .Emit("set_inside_brain", ToJson(request));
        }

        /// <summary>
        ///     Set the inside brain state of a manipulator.
        /// </summary>
        /// <param name="request">Set inside brain request.</param>
        /// <returns><see cref="BooleanStateResponse" /> with the manipulator's new inside brain state.</returns>
        public async Awaitable<BooleanStateResponse> SetInsideBrain(SetInsideBrainRequest request)
        {
            return await EmitAndGetResponse<BooleanStateResponse, SetInsideBrainRequest>(
                "set_inside_brain",
                request
            );
        }

        /// <summary>
        ///     Request a manipulator stops moving.
        /// </summary>
        /// <param name="manipulatorId"></param>
        /// <param name="onSuccessCallback"></param>
        /// <param name="onErrorCallback"></param>
        public void Stop(
            string manipulatorId,
            Action onSuccessCallback,
            Action<string> onErrorCallback
        )
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                        // Non-empty response means error.
                        onErrorCallback?.Invoke(data);
                    else
                        // Empty response means success.
                        onSuccessCallback?.Invoke();
                })
                .Emit("stop", manipulatorId);
        }

        /// <summary>
        ///     Request a manipulator stops moving.
        /// </summary>
        /// <param name="manipulatorId">ID of the manipulator to stop</param>
        /// <returns>Empty string if successful, error message if failed.</returns>
        public async Awaitable<string> Stop(string manipulatorId)
        {
            return await EmitAndGetStringResponse("stop", manipulatorId);
        }

        /// <summary>
        ///     Request all movement to stop.
        /// </summary>
        /// <param name="onSuccessCallback">Handle successful stop.</param>
        /// <param name="onErrorCallback">Handle failed stops.</param>
        public void StopAll(Action onSuccessCallback, Action<string> onErrorCallback)
        {
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data =>
                {
                    if (DataKnownAndNotEmpty(data))
                        // Non-empty response means error.
                        onErrorCallback?.Invoke(data);
                    else
                        // Empty response means success.
                        onSuccessCallback?.Invoke();
                })
                .Emit("stop_all");
        }

        /// <summary>
        ///     Request all manipulators to stop.
        /// </summary>
        /// <returns>Empty string if successful, error message if failed.</returns>
        public async Awaitable<string> StopAll()
        {
            return await EmitAndGetStringResponse<object>("stop_all", null);
        }

        #endregion

        #region Utility Functions

        /// <summary>
        ///     Quick error handler to log the error string if it exists.
        /// </summary>
        /// <param name="error">Error response to check.</param>
        /// <returns>True if there was an error, false otherwise.</returns>
        public static bool HasError(string error)
        {
            // Shortcut exit if there was no error.
            if (string.IsNullOrEmpty(error))
                return false;

            // Log the error.
            Debug.LogError(error);
            OutputLog.Log(
                new[]
                {
                    "ephys_link",
                    DateTime.Now.ToString(CultureInfo.InvariantCulture),
                    $"ERROR: {error}"
                }
            );

            // Return true to indicate an error.
            return true;
        }

        #endregion

        #region Helper functions

        /// <summary>
        ///     Generic function to emit and event and get a response from the server.
        /// </summary>
        /// <param name="eventName">Event to emit to.</param>
        /// <param name="requestParameter">Parameter to send with the event.</param>
        /// <typeparam name="T">Expected (parsed) response type.</typeparam>
        /// <typeparam name="TR">Type of the request parameter.</typeparam>
        /// <returns>Response from server. Parsed to <see cref="T" /> if it's not a string.</returns>
        /// <exception cref="InvalidDataException">Invalid response from server (empty or unknown).</exception>
        private async Awaitable<T> EmitAndGetResponse<T, TR>(string eventName, TR requestParameter)
        {
            // Query server and capture response.
            var dataCompletionSource = new AwaitableCompletionSource<string>();
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data => dataCompletionSource.SetResult(data))
                .Emit(
                    eventName,
                    typeof(TR) == typeof(string) ? requestParameter : ToJson(requestParameter)
                );

            // Wait for data.
            var data = await dataCompletionSource.Awaitable;

            // Return data if it exists. Parse if return type is not string.
            if (DataKnownAndNotEmpty(data))
                return ParseJson<T>(data);

            // Throw exception if data is empty.
            throw new InvalidDataException($"{eventName} invalid response: {data}");
        }

        /// <summary>
        ///     Emit an event and get a string response from the server.
        /// </summary>
        /// <param name="eventName">Event to emit to.</param>
        /// <param name="requestParameter">Parameter to send with the event.</param>
        /// <typeparam name="TR">Type of the request parameter.</typeparam>
        /// <returns>Response from server as a string.</returns>
        private async Awaitable<string> EmitAndGetStringResponse<TR>(
            string eventName,
            TR requestParameter
        )
        {
            // Query server and capture response.
            var dataCompletionSource = new AwaitableCompletionSource<string>();
            _connectionManager
                .Socket.ExpectAcknowledgement<string>(data => dataCompletionSource.SetResult(data))
                .Emit(
                    eventName,
                    typeof(TR) == typeof(string) ? requestParameter : ToJson(requestParameter)
                );

            // Wait for data.
            var data = await dataCompletionSource.Awaitable;

            // Return data.
            return data;
        }

        /// <summary>
        ///     Check if data is not empty and is not the "unkown event" error.
        /// </summary>
        /// <param name="data">Data to check.</param>
        /// <returns>True if data is not empty and not the "unkown event" error, false otherwise.</returns>
        private static bool DataKnownAndNotEmpty(string data)
        {
            return !string.IsNullOrEmpty(data) && !data.Equals(UNKOWN_EVENT);
        }

        /// <summary>
        ///     Parse a JSON string into a data object.
        /// </summary>
        /// <param name="json">JSON string to parse.</param>
        /// <typeparam name="T">Type of the data object.</typeparam>
        /// <returns>Parsed data object.</returns>
        private static T ParseJson<T>(string json)
        {
            return JsonUtility.FromJson<T>(json);
        }

        /// <summary>
        ///     Convert a data object into a JSON string.
        /// </summary>
        /// <param name="data">Data object to convert.</param>
        /// <typeparam name="T">Type of the data object.</typeparam>
        /// <returns>JSON string.</returns>
        private static string ToJson<T>(T data)
        {
            return JsonUtility.ToJson(data);
        }

        #endregion
    }
}
