using System;
using System.Collections.Generic;
using ElectricPalletStackers.PalletStackers;
using Spaxtek.Ble.Core;
using Spaxtek.Ble.Unity;
using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PalletStackerBleClient))]
    public sealed class PalletStackerBleDebugPanel : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private BleManager _bleManager;
        [SerializeField] private PalletStackerBleClient _client;
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private PalletStackerCollisionSender _collisionSender;
        [SerializeField] private PalletStackerControlDriver _controlDriver;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
        [SerializeField] private PalletStackerKeyboardSimulator _keyboardSimulator;

        [Header("Display")]
        [SerializeField] private bool _showOverlay = true;
        [SerializeField] private bool _mirrorEventsToConsole = true;
        [SerializeField, Min(1)] private int _maximumLogEntries = 16;
        [SerializeField] private Rect _windowRect = new Rect(12f, 12f, 560f, 650f);

        private readonly List<string> _eventLog = new List<string>();
        private Vector2 _logScroll;
        private byte[] _lastInjectedPacket;
        private ushort _nextInjectedSequence;
        private bool _subscribed;

        public bool ShowOverlay
        {
            get => _showOverlay;
            set => _showOverlay = value;
        }

        private void Awake()
        {
            CacheComponents();
        }

        private void OnEnable()
        {
            CacheComponents();
            Subscribe();
            AddLog("Debug panel enabled.");
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnValidate()
        {
            _maximumLogEntries = Mathf.Max(1, _maximumLogEntries);
            _windowRect.width = Mathf.Max(360f, _windowRect.width);
            _windowRect.height = Mathf.Max(300f, _windowRect.height);
        }

        private void OnGUI()
        {
            if (!_showOverlay) return;

            _windowRect.width = Mathf.Min(_windowRect.width, Mathf.Max(360f, Screen.width - 24f));
            _windowRect.height = Mathf.Min(_windowRect.height, Mathf.Max(300f, Screen.height - 24f));
            _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, "ESP32 Pallet Stacker BLE Debug");
        }

        [ContextMenu("Inject Valid CONTROL_STATE")]
        public void InjectValidControlState()
        {
            if (_bleManager != null && _bleManager.HasConnection)
            {
                AddLog("Local injection blocked while BLE is connected.");
                return;
            }

            ushort sequence = _nextInjectedSequence++;
            bool horn = (sequence & 1) != 0;
            byte flags = (byte)(0x01 | (horn ? 0x08 : 0x00));
            sbyte steer = (sbyte)(horn ? -30 : 30);
            byte travel = horn ? (byte)80 : (byte)200;
            byte lift = horn ? (byte)PalletStackerLiftState.Down : (byte)PalletStackerLiftState.Up;

            _lastInjectedPacket = new[]
            {
                PalletStackerBleProtocol.Version,
                (byte)(sequence & 0xFF),
                (byte)(sequence >> 8),
                flags,
                unchecked((byte)steer),
                (byte)55,
                travel,
                lift
            };

            ApplyLocalPacket(_lastInjectedPacket, "VALID");
        }

        [ContextMenu("Inject Duplicate CONTROL_STATE")]
        public void InjectDuplicateControlState()
        {
            if (_bleManager != null && _bleManager.HasConnection)
            {
                AddLog("Local injection blocked while BLE is connected.");
                return;
            }

            if (_lastInjectedPacket == null)
            {
                AddLog("Inject a valid packet before testing duplicate handling.");
                return;
            }

            ApplyLocalPacket(_lastInjectedPacket, "DUPLICATE");
        }

        [ContextMenu("Inject Invalid CONTROL_STATE")]
        public void InjectInvalidControlState()
        {
            if (_bleManager != null && _bleManager.HasConnection)
            {
                AddLog("Local injection blocked while BLE is connected.");
                return;
            }

            byte[] invalidPacket =
            {
                PalletStackerBleProtocol.Version,
                0,
                0,
                0x20, // Reserved flag bit: must be rejected.
                0,
                55,
                127,
                0
            };
            ApplyLocalPacket(invalidPacket, "INVALID");
        }

        [ContextMenu("Clear Debug Log")]
        public void ClearLog()
        {
            _eventLog.Clear();
        }

        private void DrawWindow(int windowId)
        {
            bool connected = _bleManager != null && _bleManager.HasConnection;
            string adapterState = _bleManager?.Adapter?.State.ToString() ?? "-";
            GUILayout.Label($"Adapter: {adapterState}    Connection: {(connected ? "CONNECTED" : "DISCONNECTED")}    Protocol ready: {(_client != null && _client.IsReady)}");
            GUILayout.Label($"Last CONTROL seq: {FormatSequence(_stateReceiver?.LastAppliedSequence)}    Pending collision seq: {FormatSequence(_collisionSender?.PendingSequence)}");
            if (_controlDriver != null)
            {
                GUILayout.Label($"Drive={_controlDriver.TravelCommand:0.000}    Steering={_controlDriver.SteeringDegrees:0.#}°    Collision interlock={_controlDriver.CollisionInterlock}");
            }

            if (_keyboardSimulator != null)
            {
                string owner = _keyboardSimulator.BleOwnsControl
                    ? "BLE"
                    : _keyboardSimulator.IsSimulationActive ? "KEYBOARD" : "FAIL-SAFE";
                GUILayout.Label($"Control owner: {owner}    Keyboard simulation: {(_keyboardSimulator.SimulationEnabled ? "ON" : "OFF")}    Last sim seq: {FormatSequence(_keyboardSimulator.LastInjectedSequence)}");
                GUILayout.Label("Keys: W/S travel, A/D steer, Up/Down tiller, R/F forks, H horn, Shift slow");
                GUILayout.Label("      Space stop, E E-stop, X enable, Tab keyboard on/off, C collision, V clear stop");
            }

            PalletStackerControlState state = _stateReceiver?.CurrentState;
            if (state != null)
            {
                GUILayout.Space(4f);
                GUILayout.Label($"Enabled={state.Enabled}  Stop={state.Stop}  E-Stop={state.EmergencyStop}  Horn={state.Horn}  Slow={state.SlowMode}");
                GUILayout.Label($"Steer={state.SteerDeg}°  Tiller={state.TillerDeg}°  TravelRaw={state.TravelRaw}  Travel={state.SafeTravelNormalized:0.000}  Lift={state.Lift}");
            }

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Connect / Scan")) _client?.StartClient();
            if (GUILayout.Button("Disconnect")) _client?.Disconnect();
            GUI.enabled = connected;
            if (GUILayout.Button("Send Collision")) _collisionSender?.SendCollision();
            GUI.enabled = true;
            if (GUILayout.Button("Simulate Vehicle Collision")) _collisionReporter?.ReportCollision();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = !connected;
            if (GUILayout.Button("Inject Valid")) InjectValidControlState();
            if (GUILayout.Button("Inject Duplicate")) InjectDuplicateControlState();
            if (GUILayout.Button("Inject Invalid")) InjectInvalidControlState();
            GUI.enabled = true;
            if (GUILayout.Button("Clear Collision Stop")) _controlDriver?.ClearCollisionInterlock();
            if (GUILayout.Button("Clear Log")) ClearLog();
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label("Event log");
            _logScroll = GUILayout.BeginScrollView(_logScroll, GUI.skin.box, GUILayout.ExpandHeight(true));
            for (int index = 0; index < _eventLog.Count; index++) GUILayout.Label(_eventLog[index]);
            GUILayout.EndScrollView();

            GUILayout.Label("Tip: local packet injection is disabled while connected so it cannot disturb the real CONTROL sequence.");
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void ApplyLocalPacket(byte[] packet, string label)
        {
            if (_stateReceiver == null)
            {
                AddLog($"{label}: StateReceiver is missing.");
                return;
            }

            bool accepted = _stateReceiver.TryApplyPayload(
                packet,
                out ushort sequence,
                out bool duplicate,
                out string error);

            AddLog(accepted
                ? $"{label} RX seq={sequence}, duplicate={duplicate}, raw={BitConverter.ToString(packet)}"
                : $"{label} REJECTED: {error}, raw={BitConverter.ToString(packet)}");
        }

        private void CacheComponents()
        {
            if (_client == null) _client = GetComponent<PalletStackerBleClient>();
            if (_bleManager == null) _bleManager = GetComponent<BleManager>();
            if (_stateReceiver == null) _stateReceiver = GetComponent<PalletStackerControlStateReceiver>();
            if (_collisionSender == null) _collisionSender = GetComponent<PalletStackerCollisionSender>();
            if (_controlDriver == null) _controlDriver = FindFirstObjectByType<PalletStackerControlDriver>();
            if (_collisionReporter == null) _collisionReporter = FindFirstObjectByType<PalletStackerCollisionReporter>();
            if (_keyboardSimulator == null) _keyboardSimulator = GetComponent<PalletStackerKeyboardSimulator>();
        }

        private void Subscribe()
        {
            if (_subscribed) return;

            if (_client != null)
            {
                _client.Ready += HandleReady;
                _client.ConnectionStateChanged += HandleConnectionStateChanged;
                _client.ClientFailed += HandleClientFailed;
            }

            if (_bleManager != null)
            {
                _bleManager.OnAdapterStateChanged += HandleAdapterStateChanged;
                _bleManager.OnError += HandleBleError;
            }

            if (_stateReceiver != null)
            {
                _stateReceiver.StateApplied += HandleStateApplied;
                _stateReceiver.DuplicateReceived += HandleControlDuplicate;
                _stateReceiver.SequenceGapDetected += HandleSequenceGap;
                _stateReceiver.ControlAcknowledged += HandleControlAcknowledged;
                _stateReceiver.ControlAckFailed += HandleControlAckFailed;
                _stateReceiver.PayloadRejected += HandlePayloadRejected;
            }

            if (_collisionSender != null)
            {
                _collisionSender.CollisionAttempted += HandleCollisionAttempted;
                _collisionSender.CollisionConfirmed += HandleCollisionConfirmed;
                _collisionSender.CollisionFailed += HandleCollisionFailed;
                _collisionSender.UnexpectedAckReceived += HandleUnexpectedAck;
                _collisionSender.AckRejected += HandleAckRejected;
            }

            if (_controlDriver != null)
                _controlDriver.CollisionInterlockEngaged += HandleCollisionInterlockEngaged;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (_client != null)
            {
                _client.Ready -= HandleReady;
                _client.ConnectionStateChanged -= HandleConnectionStateChanged;
                _client.ClientFailed -= HandleClientFailed;
            }

            if (_bleManager != null)
            {
                _bleManager.OnAdapterStateChanged -= HandleAdapterStateChanged;
                _bleManager.OnError -= HandleBleError;
            }

            if (_stateReceiver != null)
            {
                _stateReceiver.StateApplied -= HandleStateApplied;
                _stateReceiver.DuplicateReceived -= HandleControlDuplicate;
                _stateReceiver.SequenceGapDetected -= HandleSequenceGap;
                _stateReceiver.ControlAcknowledged -= HandleControlAcknowledged;
                _stateReceiver.ControlAckFailed -= HandleControlAckFailed;
                _stateReceiver.PayloadRejected -= HandlePayloadRejected;
            }

            if (_collisionSender != null)
            {
                _collisionSender.CollisionAttempted -= HandleCollisionAttempted;
                _collisionSender.CollisionConfirmed -= HandleCollisionConfirmed;
                _collisionSender.CollisionFailed -= HandleCollisionFailed;
                _collisionSender.UnexpectedAckReceived -= HandleUnexpectedAck;
                _collisionSender.AckRejected -= HandleAckRejected;
            }

            if (_controlDriver != null)
                _controlDriver.CollisionInterlockEngaged -= HandleCollisionInterlockEngaged;

            _subscribed = false;
        }

        private void HandleReady() => AddLog("BLE client READY; CONTROL_STATE and ACK subscribed.");
        private void HandleAdapterStateChanged(BleAdapterState state) => AddLog($"Adapter state: {state}");
        private void HandleBleError(BleError error) => AddLog($"BLE ERROR: {error}");
        private void HandleConnectionStateChanged(BleConnectionState state) => AddLog($"Connection state: {state}");
        private void HandleClientFailed(string error) => AddLog($"CLIENT FAILED: {error}");

        private void HandleStateApplied(PalletStackerControlState state, ushort sequence, PalletStackerControlFields changed)
        {
            AddLog($"CONTROL APPLIED seq={sequence}, changed={changed}, travel={state.TravelRaw}, lift={state.Lift}");
        }

        private void HandleControlDuplicate(ushort sequence) => AddLog($"CONTROL DUPLICATE seq={sequence}; ACK again, not applied.");
        private void HandleSequenceGap(ushort expected, ushort actual) => AddLog($"CONTROL GAP expected={expected}, actual={actual}");
        private void HandleControlAcknowledged(ushort sequence) => AddLog($"CONTROL ACK TX seq={sequence}");
        private void HandleControlAckFailed(ushort sequence, string error) => AddLog($"CONTROL ACK FAILED seq={sequence}: {error}");
        private void HandlePayloadRejected(string error) => AddLog($"CONTROL REJECTED: {error}");
        private void HandleCollisionAttempted(ushort sequence, int attempt) => AddLog($"COLLISION TX seq={sequence}, attempt={attempt}");
        private void HandleCollisionConfirmed(ushort sequence) => AddLog($"COLLISION CONFIRMED seq={sequence}");
        private void HandleCollisionFailed(ushort sequence, string error) => AddLog($"COLLISION FAILED seq={sequence}: {error}");
        private void HandleUnexpectedAck(ushort sequence) => AddLog($"UNEXPECTED COLLISION ACK seq={sequence}");
        private void HandleAckRejected(string error) => AddLog($"COLLISION ACK REJECTED: {error}");
        private void HandleCollisionInterlockEngaged() => AddLog("LOCAL COLLISION INTERLOCK ENGAGED; vehicle stopped before BLE ACK.");

        private void AddLog(string message)
        {
            string entry = $"[{Time.realtimeSinceStartup:0.000}] {message}";
            _eventLog.Add(entry);
            while (_eventLog.Count > _maximumLogEntries) _eventLog.RemoveAt(0);
            _logScroll.y = float.MaxValue;

            if (_mirrorEventsToConsole) Debug.Log($"[BLE DEBUG] {message}", this);
        }

        private static string FormatSequence(ushort? sequence)
        {
            return sequence.HasValue ? sequence.Value.ToString() : "-";
        }
    }
}
