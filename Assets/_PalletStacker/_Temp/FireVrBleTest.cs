using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using TMPro;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ElectricPalletStackers.Ble
{
    /// <summary>
    /// FIRE VR BLE connection test for Android/Meta Quest.
    /// Protocol: ESP32 TX notifies Unity; Unity writes to ESP32 RX.
    /// </summary>
    public sealed class FireVrBleTest : MonoBehaviour
    {
        public const string DeviceName = "VR-ESP32-FIRE";
        public const string ServiceUuid = "7a1e0001-8e88-4c19-9e6a-31b9d0a4f001";
        public const string TxUuid = "7a1e0002-8e88-4c19-9e6a-31b9d0a4f001";
        public const string RxUuid = "7a1e0003-8e88-4c19-9e6a-31b9d0a4f001";

        public enum BleConnectionState
        {
            Disconnected,
            Scanning,
            Connecting,
            Connected,
            Disconnecting
        }

        [Header("Startup")]
        [SerializeField] private bool _initializeOnStart = true;
        [SerializeField] private bool _scanAfterInitialize = true;
        [SerializeField] private bool _connectWhenFound = true;
        [SerializeField] private bool _subscribeAfterGattVerification = true;

        [Header("Test")]
        [SerializeField, Min(1f)] private float _scanTimeoutSeconds = 15f;
        [SerializeField] private bool _enableKeyboardTest = true;
        [SerializeField] private bool _disconnectWhenApplicationPauses = true;

        [Header("Runtime (read only)")]
        [SerializeField] private BleConnectionState _state = BleConnectionState.Disconnected;
        [SerializeField] private bool _isInitialized;
        [SerializeField] private bool _serviceVerified;
        [SerializeField] private bool _txVerified;
        [SerializeField] private bool _rxVerified;
        [SerializeField] private bool _txSubscribed;
        [SerializeField] private string _deviceAddress = string.Empty;
        [SerializeField] private string _lastHex = string.Empty;
        [SerializeField] private string _lastText = string.Empty;

        [Header("Test UI")]
        [SerializeField] private TMP_Text _statusText = null;
        [SerializeField] private TMP_Text _gattText = null;
        [SerializeField] private TMP_Text _receiveText = null;
        [SerializeField] private TMP_Text _logText = null;
        [SerializeField] private TMP_InputField _commandInput = null;

        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private const int UiLogCharacterLimit = 12000;

        private byte[] _lastRawBytes = Array.Empty<byte>();
        private bool _physicalLinkConnected;
        private int _scanGeneration;
        private readonly StringBuilder _uiLog = new StringBuilder();

#if UNITY_ANDROID && !UNITY_EDITOR
        private const string BluetoothScanPermission = "android.permission.BLUETOOTH_SCAN";
        private const string BluetoothConnectPermission = "android.permission.BLUETOOTH_CONNECT";
        private const string FineLocationPermission = "android.permission.ACCESS_FINE_LOCATION";
        private const string JavaBridgeClass = "com.firevr.ble.FireVrBleBridge";

        private AndroidJavaObject _bridge;
        private BleListenerProxy _listener;
        private PermissionCallbacks _permissionCallbacks;
#endif

        public BleConnectionState State => _state;
        public bool IsInitialized => _isInitialized;
        public bool IsConnected => _physicalLinkConnected;
        public bool IsReady => _state == BleConnectionState.Connected
                               && _serviceVerified
                               && _txVerified
                               && _rxVerified;
        public bool IsTxSubscribed => _txSubscribed;
        public string DeviceAddress => _deviceAddress;
        public byte[] LastRawBytes => (byte[])_lastRawBytes.Clone();
        public string LastHex => _lastHex;
        public string LastText => _lastText;

        /// <summary>Raised on the Unity main thread. Subscribers receive their own byte-array copy.</summary>
        public event Action<byte[]> NotificationReceived;

        private void Start()
        {
            RefreshUi();
            if (_initializeOnStart)
                InitializeBle();
        }

        private void Update()
        {
            while (_mainThreadActions.TryDequeue(out Action action))
                action.Invoke();

            HandleKeyboardTest();
        }

        /// <summary>Initializes the Android BLE adapter and requests Android runtime permissions.</summary>
        public void InitializeBle()
        {
            Log("[BLE] Initializing...");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (_isInitialized)
            {
                Log("[PASS] BLE Initialized");
                if (_scanAfterInitialize && _state == BleConnectionState.Disconnected)
                    StartScan();
                return;
            }

            RequestBlePermissionsThenInitialize();
#else
            Fail("FIRE VR BLE requires an Android Player build (for example Meta Quest). It does not run in the Unity Editor.");
#endif
        }

        /// <summary>Scans only for the fixed FIRE VR device name.</summary>
        public void StartScan()
        {
            if (!RequireInitialized("start scan"))
                return;

            if (_state == BleConnectionState.Connecting ||
                _state == BleConnectionState.Connected ||
                _state == BleConnectionState.Disconnecting)
            {
                Fail($"Cannot scan while BLE state is {_state}.");
                return;
            }

            _deviceAddress = string.Empty;
            ResetGattFlags();
            SetState(BleConnectionState.Scanning);
            Log($"[SCAN] Searching: {DeviceName}");

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridge.Call("startScan");
                int generation = ++_scanGeneration;
                StartCoroutine(StopScanAfterTimeout(generation));
            }
            catch (Exception exception)
            {
                HandleOperationException("start scan", exception);
            }
#endif
        }

        /// <summary>Stops an active BLE scan.</summary>
        public void StopScan()
        {
            if (_state != BleConnectionState.Scanning)
                return;

            ++_scanGeneration;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridge?.Call("stopScan");
            }
            catch (Exception exception)
            {
                HandleOperationException("stop scan", exception);
                return;
            }
#endif
            SetState(BleConnectionState.Disconnected);
        }

        /// <summary>Connects to the exact device selected by the preceding scan.</summary>
        public void Connect()
        {
            if (!RequireInitialized("connect"))
                return;

            if (string.IsNullOrWhiteSpace(_deviceAddress))
            {
                Fail($"Cannot connect: {DeviceName} has not been found. Run StartScan first.");
                return;
            }

            if (_state == BleConnectionState.Connected || _state == BleConnectionState.Connecting)
            {
                Fail($"Cannot connect while BLE state is {_state}.");
                return;
            }

            ++_scanGeneration;
            ResetGattFlags();
            SetState(BleConnectionState.Connecting);
            Log("[CONNECT] Connecting...");

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridge.Call("connect");
            }
            catch (Exception exception)
            {
                HandleOperationException("connect", exception);
            }
#endif
        }

        /// <summary>Subscribes to ESP32 TX notifications after successful GATT verification.</summary>
        public void SubscribeTX()
        {
            if (!IsReady)
            {
                Fail("Cannot subscribe TX before connection and GATT verification are complete.");
                return;
            }

            Log("[NOTIFY] Subscribe TX...");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridge.Call("subscribeTx");
            }
            catch (Exception exception)
            {
                HandleOperationException("subscribe TX", exception);
            }
#endif
        }

        /// <summary>Sends a UTF-8 command through the ESP32 RX characteristic.</summary>
        public void SendCommand(string command)
        {
            if (string.IsNullOrEmpty(command))
            {
                Fail("Cannot send an empty command.");
                return;
            }

            SendBytes(Encoding.UTF8.GetBytes(command), $"'{EscapeText(command)}'");
        }

        /// <summary>Sends the first entered character, matching the Python BLE test behavior.</summary>
        public void SendCommandFromInput()
        {
            if (_commandInput == null)
            {
                Fail("Command Input UI field is not assigned.");
                return;
            }

            string command = _commandInput.text?.Trim();
            if (string.IsNullOrEmpty(command))
            {
                Fail("Enter a command before pressing Send.");
                return;
            }

            SendCommand(command.Substring(0, 1));
            _commandInput.text = string.Empty;
            _commandInput.ActivateInputField();
        }

        /// <summary>Clears only the on-panel test log.</summary>
        public void ClearUiLog()
        {
            _uiLog.Clear();
            if (_logText != null)
                _logText.text = "Waiting for BLE activity...";
        }

        /// <summary>Sends raw bytes through RX without interpreting or changing them.</summary>
        public void SendBytes(byte[] bytes)
        {
            SendBytes(bytes, $"RAW HEX {ToHex(bytes)}");
        }

        /// <summary>Disconnects from the ESP32 and leaves the test ready for another scan.</summary>
        public void Disconnect()
        {
            ++_scanGeneration;

            if (_state == BleConnectionState.Disconnected && !_physicalLinkConnected)
            {
                Log("[PASS] DISCONNECTED");
                return;
            }

            SetState(BleConnectionState.Disconnecting);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridge?.Call("disconnect");
            }
            catch (Exception exception)
            {
                HandleOperationException("disconnect", exception);
            }
#else
            CompleteDisconnect("Editor state reset.");
#endif
        }

        private void SendBytes(byte[] bytes, string displayValue)
        {
            if (bytes == null || bytes.Length == 0)
            {
                Fail("Cannot send an empty byte array.");
                return;
            }

            if (!IsReady)
            {
                Fail("Cannot send: BLE is not connected or GATT verification is incomplete.");
                return;
            }

            Log($"[TX TO ESP32] {displayValue}");
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Java byte is signed. Unity's JNI bridge maps Java byte[] to C# sbyte[].
                _bridge.Call("writeRx", ToSignedBytes(bytes));
            }
            catch (Exception exception)
            {
                HandleOperationException("write RX", exception, false);
            }
#endif
        }

        private IEnumerator StopScanAfterTimeout(int generation)
        {
            yield return new WaitForSecondsRealtime(_scanTimeoutSeconds);

            if (generation != _scanGeneration || _state != BleConnectionState.Scanning)
                yield break;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridge?.Call("stopScan");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
#endif
            ++_scanGeneration;
            SetState(BleConnectionState.Disconnected);
            Fail($"Device {DeviceName} not found within {_scanTimeoutSeconds:0.#} seconds. " +
                 "ESP32 may not be advertising or powered, Bluetooth may be disabled, " +
                 "or another device may already be connected.");
        }

        private bool RequireInitialized(string operation)
        {
            if (_isInitialized)
                return true;

            Fail($"Cannot {operation}: BLE has not initialized successfully.");
            return false;
        }

        private void HandleDeviceFound(string name, string address)
        {
            if (_state != BleConnectionState.Scanning || !string.Equals(name, DeviceName, StringComparison.Ordinal))
                return;

            ++_scanGeneration;
            _deviceAddress = address ?? string.Empty;
            SetState(BleConnectionState.Disconnected);
            Log($"[FOUND]\nName   : {name}\nAddress: {_deviceAddress}");

            if (_connectWhenFound)
                Connect();
        }

        private void HandleScanFailed(string reason)
        {
            ++_scanGeneration;
            SetState(BleConnectionState.Disconnected);
            Fail($"Scan failed: {reason}");
        }

        private void HandleConnectionState(bool connected, string detail)
        {
            if (connected)
            {
                _physicalLinkConnected = true;
                Log($"[PASS] CONNECTED = True{FormatDetail(detail)}");
                return;
            }

            bool expected = _state == BleConnectionState.Disconnecting;
            CompleteDisconnect(detail);
            if (!expected)
                Fail($"Unexpected BLE disconnect{FormatDetail(detail)}");
        }

        private void HandleGattVerified(bool servicePass, bool txPass, bool rxPass, string detail)
        {
            _serviceVerified = servicePass;
            _txVerified = txPass;
            _rxVerified = rxPass;

            Log($"[GATT]\nSERVICE = {(servicePass ? "PASS" : "FAIL")}\n" +
                $"TX      = {(txPass ? "PASS" : "FAIL")}\n" +
                $"RX      = {(rxPass ? "PASS" : "FAIL")}{FormatDetail(detail)}");

            if (!servicePass)
                Fail("FIRE VR Service does not exist or has the wrong UUID.");
            if (!txPass)
                Fail("TX Characteristic does not exist or lacks READ/NOTIFY properties.");
            if (!rxPass)
                Fail("RX Characteristic does not exist or lacks WRITE/WRITE WITHOUT RESPONSE properties.");

            if (!servicePass || !txPass || !rxPass)
            {
                RefreshUi();
                SetState(BleConnectionState.Disconnecting);
#if UNITY_ANDROID && !UNITY_EDITOR
                _bridge?.Call("disconnect");
#endif
                return;
            }

            SetState(BleConnectionState.Connected);
            RefreshUi();
            if (_subscribeAfterGattVerification)
                SubscribeTX();
        }

        private void HandleSubscription(bool success, string detail)
        {
            _txSubscribed = success;
            RefreshUi();
            if (success)
                Log($"[PASS] TX Notification subscribed{FormatDetail(detail)}");
            else
                Fail($"TX notification subscription failed{FormatDetail(detail)}");
        }

        private void HandleNotification(byte[] data)
        {
            data ??= Array.Empty<byte>();
            _lastRawBytes = (byte[])data.Clone();
            _lastHex = ToHex(data);

            bool hasText = TryDecodeUtf8(data, out string text);
            _lastText = hasText ? text : string.Empty;
            RefreshUi();

            string log = $"[RX NOTIFY FROM ESP32]\nRAW HEX : {_lastHex}";
            log += hasText ? $"\nTEXT    : '{EscapeText(text)}'" : "\nTEXT    : <not valid UTF-8>";
            Log(log);

            NotificationReceived?.Invoke((byte[])data.Clone());
        }

        private void HandleWrite(bool success, string detail)
        {
            if (success)
                Log($"[WRITE PASS]{FormatDetail(detail)}");
            else
                Fail($"Write failed{FormatDetail(detail)}");
        }

        private void CompleteDisconnect(string detail)
        {
            _physicalLinkConnected = false;
            ResetGattFlags();
            SetState(BleConnectionState.Disconnected);
            Log($"[PASS] DISCONNECTED{FormatDetail(detail)}");
        }

        private void ResetGattFlags()
        {
            _physicalLinkConnected = false;
            _serviceVerified = false;
            _txVerified = false;
            _rxVerified = false;
            _txSubscribed = false;
        }

        private void SetState(BleConnectionState state)
        {
            _state = state;
            RefreshUi();
        }

        /// <summary>Refreshes all optional scene UI fields from the current BLE state.</summary>
        public void RefreshUi()
        {
            if (_statusText != null)
            {
                _statusText.text =
                    $"STATE: {_state}\n" +
                    $"CONNECTED: {(_physicalLinkConnected ? "TRUE" : "FALSE")}\n" +
                    $"DEVICE: {DeviceName}\n" +
                    $"ADDRESS: {(string.IsNullOrWhiteSpace(_deviceAddress) ? "--" : _deviceAddress)}";

                _statusText.color = _state switch
                {
                    BleConnectionState.Connected => new Color(0.35f, 1f, 0.55f),
                    BleConnectionState.Scanning or BleConnectionState.Connecting => new Color(1f, 0.78f, 0.25f),
                    BleConnectionState.Disconnecting => new Color(1f, 0.55f, 0.25f),
                    _ => new Color(0.78f, 0.82f, 0.9f)
                };
            }

            if (_gattText != null)
            {
                _gattText.text =
                    $"SERVICE: {PassLabel(_serviceVerified)}\n" +
                    $"TX READ + NOTIFY: {PassLabel(_txVerified)}\n" +
                    $"RX WRITE + NO RESPONSE: {PassLabel(_rxVerified)}\n" +
                    $"TX SUBSCRIBED: {PassLabel(_txSubscribed)}";
            }

            if (_receiveText != null)
            {
                string textValue = string.IsNullOrEmpty(_lastText) ? "--" : $"'{EscapeText(_lastText)}'";
                _receiveText.text =
                    "RX NOTIFY FROM ESP32\n" +
                    $"RAW HEX: {(string.IsNullOrEmpty(_lastHex) ? "--" : _lastHex)}\n" +
                    $"TEXT: {textValue}";
            }
        }

        private void HandleOperationException(string operation, Exception exception, bool disconnect = true)
        {
            Fail($"Unable to {operation}: {exception.Message}");
            if (disconnect)
            {
                ResetGattFlags();
                SetState(BleConnectionState.Disconnected);
            }
        }

        private void HandleKeyboardTest()
        {
            if (!_enableKeyboardTest)
                return;

#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.hKey.wasPressedThisFrame)
                SendCommand("H");
            else if (keyboard.sKey.wasPressedThisFrame)
                SendCommand("S");
            else if (keyboard.rKey.wasPressedThisFrame)
                SendCommand("R");
            else if (keyboard.qKey.wasPressedThisFrame)
                Disconnect();
#endif
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && _disconnectWhenApplicationPauses &&
                (_physicalLinkConnected || _state == BleConnectionState.Scanning))
            {
                Disconnect();
            }
        }

        private void OnDestroy()
        {
            ++_scanGeneration;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _bridge?.Call("dispose");
                _bridge?.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }

            _bridge = null;
            _listener = null;
            _permissionCallbacks = null;
#endif
        }

        private static bool TryDecodeUtf8(byte[] data, out string text)
        {
            try
            {
                text = StrictUtf8.GetString(data);
                return true;
            }
            catch (DecoderFallbackException)
            {
                text = string.Empty;
                return false;
            }
        }

        private static string ToHex(byte[] data)
        {
            return data == null || data.Length == 0
                ? string.Empty
                : BitConverter.ToString(data).Replace("-", " ");
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private static sbyte[] ToSignedBytes(byte[] data)
        {
            sbyte[] result = new sbyte[data.Length];
            for (int index = 0; index < data.Length; index++)
                result[index] = unchecked((sbyte)data[index]);
            return result;
        }

        private static byte[] ToUnsignedBytes(sbyte[] data)
        {
            if (data == null)
                return Array.Empty<byte>();

            byte[] result = new byte[data.Length];
            for (int index = 0; index < data.Length; index++)
                result[index] = unchecked((byte)data[index]);
            return result;
        }
#endif

        private static string EscapeText(string text)
        {
            return text?.Replace("\\", "\\\\")
                       .Replace("\r", "\\r")
                       .Replace("\n", "\\n")
                       .Replace("\t", "\\t") ?? string.Empty;
        }

        private static string FormatDetail(string detail)
        {
            return string.IsNullOrWhiteSpace(detail) ? string.Empty : $"\n{detail}";
        }

        private static string PassLabel(bool value)
        {
            return value ? "PASS" : "WAIT";
        }

        private void Log(string message)
        {
            Debug.Log(message, this);
            AppendUiLog(message);
        }

        private void Fail(string message)
        {
            Debug.LogError($"[FAIL] {message}", this);
            AppendUiLog($"[FAIL] {message}");
        }

        private void AppendUiLog(string message)
        {
            _uiLog.Append('[')
                  .Append(DateTime.Now.ToString("HH:mm:ss"))
                  .Append("] ")
                  .AppendLine(message);

            if (_uiLog.Length > UiLogCharacterLimit)
                _uiLog.Remove(0, _uiLog.Length - UiLogCharacterLimit);

            if (_logText != null)
                _logText.text = _uiLog.ToString();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private void RequestBlePermissionsThenInitialize()
        {
            string[] permissions;
            int sdkVersion;
            using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdkVersion = version.GetStatic<int>("SDK_INT");

            if (sdkVersion >= 31)
                permissions = new[] { BluetoothScanPermission, BluetoothConnectPermission };
            else
                permissions = new[] { FineLocationPermission };

            bool allGranted = true;
            foreach (string permission in permissions)
                allGranted &= Permission.HasUserAuthorizedPermission(permission);

            if (allGranted)
            {
                BeginAndroidInitialization();
                return;
            }

            _permissionCallbacks = new PermissionCallbacks();
            _permissionCallbacks.PermissionGranted += OnPermissionGranted;
            _permissionCallbacks.PermissionDenied += OnPermissionDenied;
            Permission.RequestUserPermissions(permissions, _permissionCallbacks);
        }

        private void OnPermissionGranted(string permission)
        {
            bool scanGranted = Permission.HasUserAuthorizedPermission(BluetoothScanPermission);
            bool connectGranted = Permission.HasUserAuthorizedPermission(BluetoothConnectPermission);
            bool locationGranted = Permission.HasUserAuthorizedPermission(FineLocationPermission);

            int sdkVersion;
            using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdkVersion = version.GetStatic<int>("SDK_INT");

            if ((sdkVersion >= 31 && scanGranted && connectGranted) || (sdkVersion < 31 && locationGranted))
                BeginAndroidInitialization();
        }

        private void OnPermissionDenied(string permission)
        {
            _isInitialized = false;
            SetState(BleConnectionState.Disconnected);
            Fail($"Android BLE permission denied: {permission}. Grant it in system settings, then retry InitializeBle().");
        }

        private void BeginAndroidInitialization()
        {
            try
            {
                if (_bridge == null)
                {
                    using AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                    AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                    _listener = new BleListenerProxy(this);
                    _bridge = new AndroidJavaObject(JavaBridgeClass, activity, _listener);
                    activity.Dispose();
                }

                _bridge.Call("initialize");
            }
            catch (Exception exception)
            {
                _isInitialized = false;
                HandleOperationException("initialize BLE", exception);
            }
        }

        private void Enqueue(Action action)
        {
            if (action != null)
                _mainThreadActions.Enqueue(action);
        }

        private sealed class BleListenerProxy : AndroidJavaProxy
        {
            private readonly FireVrBleTest _owner;

            public BleListenerProxy(FireVrBleTest owner)
                : base("com.firevr.ble.FireVrBleBridge$Listener")
            {
                _owner = owner;
            }

            public void onInitialized(bool success, string detail)
            {
                _owner.Enqueue(() =>
                {
                    _owner._isInitialized = success;
                    if (!success)
                    {
                        _owner.SetState(BleConnectionState.Disconnected);
                        _owner.Fail($"BLE initialization failed{FormatDetail(detail)}");
                        return;
                    }

                    _owner.Log($"[PASS] BLE Initialized{FormatDetail(detail)}");
                    if (_owner._scanAfterInitialize)
                        _owner.StartScan();
                });
            }

            public void onScanStarted()
            {
                _owner.Enqueue(() => _owner.Log("[SCAN] Android scanner started"));
            }

            public void onDeviceFound(string name, string address)
            {
                _owner.Enqueue(() => _owner.HandleDeviceFound(name, address));
            }

            public void onScanFailed(string reason)
            {
                _owner.Enqueue(() => _owner.HandleScanFailed(reason));
            }

            public void onConnectionState(bool connected, string detail)
            {
                _owner.Enqueue(() => _owner.HandleConnectionState(connected, detail));
            }

            public void onGattVerified(bool servicePass, bool txPass, bool rxPass, string detail)
            {
                _owner.Enqueue(() => _owner.HandleGattVerified(servicePass, txPass, rxPass, detail));
            }

            public void onSubscribed(bool success, string detail)
            {
                _owner.Enqueue(() => _owner.HandleSubscription(success, detail));
            }

            public void onNotification(sbyte[] data)
            {
                byte[] copy = ToUnsignedBytes(data);
                _owner.Enqueue(() => _owner.HandleNotification(copy));
            }

            public void onWrite(bool success, string detail)
            {
                _owner.Enqueue(() => _owner.HandleWrite(success, detail));
            }
        }
#endif
    }
}
