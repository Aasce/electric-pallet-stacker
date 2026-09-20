using System;
using System.Collections.Generic;
using ElectricPalletStackers.PalletStackers;
using Spaxtek.Ble.Core;
using Spaxtek.Ble.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ElectricPalletStackers.Ble
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerBleDebugPanelView : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private BleManager _bleManager;
        [SerializeField] private PalletStackerBleClient _client;
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private PalletStackerCollisionSender _collisionSender;
        [SerializeField] private PalletStackerControlDriver _controlDriver;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
        [SerializeField] private PalletStackerKeyboardSimulator _keyboardSimulator;

        [Header("View")]
        [SerializeField] private Canvas _canvas;
        [SerializeField] private RectTransform _panelRect;
        [SerializeField] private GameObject _contentRoot;
        [SerializeField] private Button _contentToggleButton;
        [SerializeField] private TMP_Text _contentToggleButtonLabel;
        [SerializeField] private Button _closeButton;
        [SerializeField] private TMP_Text _connectionText;
        [SerializeField] private TMP_Text _rawDataText;
        [SerializeField] private TMP_Text _mappedLabels1;
        [SerializeField] private TMP_Text _mappedValues1;
        [SerializeField] private TMP_Text _mappedLabels2;
        [SerializeField] private TMP_Text _mappedValues2;
        [SerializeField] private TMP_Text _mappedLabels3;
        [SerializeField] private TMP_Text _mappedValues3;
        [SerializeField] private TMP_Text _keysLabels1;
        [SerializeField] private TMP_Text _keysValues1;
        [SerializeField] private TMP_Text _keysLabels2;
        [SerializeField] private TMP_Text _keysValues2;
        [SerializeField] private TMP_Text _keysLabels3;
        [SerializeField] private TMP_Text _keysValues3;
        [SerializeField] private TMP_Text _eventLogText;
        [SerializeField] private ScrollRect _eventLogScrollRect;
        [SerializeField] private Button _connectButton;
        [SerializeField] private Button _disconnectButton;
        [SerializeField] private Button _sendCollisionButton;
        [SerializeField] private Button _simulateCollisionButton;
        [SerializeField] private Button _injectValidButton;
        [SerializeField] private Button _injectDuplicateButton;
        [SerializeField] private Button _injectInvalidButton;
        [SerializeField] private Button _clearCollisionButton;
        [SerializeField] private Button _clearLogButton;

        [Header("Display")]
        [SerializeField] private bool _showOverlay = true;
        [SerializeField] private bool _mirrorEventsToConsole = true;
        [SerializeField, Min(1)] private int _maximumLogEntries = 24;
        [SerializeField, Min(1f)] private float _expandedPanelHeight = 900f;
        [SerializeField, Min(1f)] private float _collapsedPanelHeight = 46f;

        private readonly List<string> _eventLog = new List<string>();
        private byte[] _lastRawPacket = Array.Empty<byte>();
        private byte[] _lastInjectedPacket;
        private ushort _nextInjectedSequence;
        private bool _subscribed;
        private bool _scrollLogToBottom;
        private bool _contentVisible = true;

        public bool ShowOverlay
        {
            get => _showOverlay;
            set
            {
                _showOverlay = value;
                ApplyVisibility();
            }
        }

        private void Awake()
        {
            ResolveDependencies();
            BindButtons();
            ApplyVisibility();
            ApplyContentVisibility();
            RefreshView();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            Subscribe();
            AddLog("Debug panel enabled.");
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (_showOverlay) RefreshView();
        }

        private void LateUpdate()
        {
            if (!_scrollLogToBottom || _eventLogScrollRect == null) return;

            Canvas.ForceUpdateCanvases();
            _eventLogScrollRect.verticalNormalizedPosition = 0f;
            _scrollLogToBottom = false;
        }

        [ContextMenu("Inject Valid CONTROL_STATE")]
        public void InjectValidControlState()
        {
            if (IsConnected())
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
            if (IsConnected())
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
            if (IsConnected())
            {
                AddLog("Local injection blocked while BLE is connected.");
                return;
            }

            byte[] invalidPacket =
            {
                PalletStackerBleProtocol.Version,
                0,
                0,
                0x20,
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
            RefreshEventLog();
        }

        private void ResolveDependencies()
        {
            if (_bleManager == null) _bleManager = FindAnyObjectByType<BleManager>();
            if (_client == null) _client = FindAnyObjectByType<PalletStackerBleClient>();
            if (_stateReceiver == null) _stateReceiver = FindAnyObjectByType<PalletStackerControlStateReceiver>();
            if (_collisionSender == null) _collisionSender = FindAnyObjectByType<PalletStackerCollisionSender>();
            if (_controlDriver == null) _controlDriver = FindAnyObjectByType<PalletStackerControlDriver>();
            if (_collisionReporter == null) _collisionReporter = FindAnyObjectByType<PalletStackerCollisionReporter>();
            if (_keyboardSimulator == null) _keyboardSimulator = FindAnyObjectByType<PalletStackerKeyboardSimulator>();
        }

        private void BindButtons()
        {
            if (_connectButton != null) _connectButton.onClick.AddListener(() => _client?.StartClient());
            if (_disconnectButton != null) _disconnectButton.onClick.AddListener(() => _client?.Disconnect());
            if (_sendCollisionButton != null) _sendCollisionButton.onClick.AddListener(() => _collisionSender?.SendCollision());
            if (_simulateCollisionButton != null) _simulateCollisionButton.onClick.AddListener(() => _collisionReporter?.ReportCollision());
            if (_injectValidButton != null) _injectValidButton.onClick.AddListener(InjectValidControlState);
            if (_injectDuplicateButton != null) _injectDuplicateButton.onClick.AddListener(InjectDuplicateControlState);
            if (_injectInvalidButton != null) _injectInvalidButton.onClick.AddListener(InjectInvalidControlState);
            if (_clearCollisionButton != null) _clearCollisionButton.onClick.AddListener(() => _controlDriver?.ClearCollisionInterlock());
            if (_clearLogButton != null) _clearLogButton.onClick.AddListener(ClearLog);
            if (_contentToggleButton != null) _contentToggleButton.onClick.AddListener(ToggleContentVisibility);
            if (_closeButton != null) _closeButton.onClick.AddListener(Close);
        }

        private void ApplyVisibility()
        {
            if (_canvas != null) _canvas.enabled = _showOverlay;
        }

        public void ToggleContentVisibility()
        {
            _contentVisible = !_contentVisible;
            ApplyContentVisibility();
        }

        public void Close()
        {
            ShowOverlay = false;
        }

        private void ApplyContentVisibility()
        {
            if (_contentRoot != null) _contentRoot.SetActive(_contentVisible);
            if (_contentToggleButtonLabel != null)
                _contentToggleButtonLabel.text = _contentVisible ? "HIDE" : "SHOW";

            if (_panelRect != null)
            {
                Vector2 size = _panelRect.sizeDelta;
                size.y = _contentVisible ? _expandedPanelHeight : _collapsedPanelHeight;
                _panelRect.sizeDelta = size;
            }
        }

        private void RefreshView()
        {
            bool connected = IsConnected();
            PalletStackerControlState state = _stateReceiver?.CurrentState;

            if (_connectionText != null)
            {
                string adapterState = _bleManager?.Adapter?.State.ToString() ?? "-";
                string owner = _keyboardSimulator == null
                    ? "-"
                    : _keyboardSimulator.BleOwnsControl
                        ? "BLE"
                        : _keyboardSimulator.IsSimulationActive ? "KEYBOARD" : "FAIL-SAFE";

                _connectionText.text =
                    $"Adapter                 {adapterState}\n" +
                    $"Connection              {(connected ? "CONNECTED" : "DISCONNECTED")}\n" +
                    $"Protocol ready          {FormatBool(_client != null && _client.IsReady)}\n" +
                    $"Control owner           {owner}\n" +
                    $"CONTROL / Collision seq {FormatSequence(_stateReceiver?.LastAppliedSequence)} / {FormatSequence(_collisionSender?.PendingSequence)}";
            }

            RefreshRawData();
            RefreshMappedData(state);
            RefreshKeys();

            if (_connectButton != null) _connectButton.interactable = !connected;
            if (_disconnectButton != null) _disconnectButton.interactable = connected;
            if (_sendCollisionButton != null) _sendCollisionButton.interactable = connected;
            if (_injectValidButton != null) _injectValidButton.interactable = !connected;
            if (_injectDuplicateButton != null) _injectDuplicateButton.interactable = !connected;
            if (_injectInvalidButton != null) _injectInvalidButton.interactable = !connected;

            ApplyFixedWidthSpacing(_connectionText);
            ApplyFixedWidthSpacing(_rawDataText);
        }

        private void RefreshRawData()
        {
            if (_rawDataText == null) return;
            if (_lastRawPacket == null || _lastRawPacket.Length == 0)
            {
                _rawDataText.text = "Waiting for CONTROL_STATE packet…";
                return;
            }

            byte[] packet = _lastRawPacket;
            if (packet.Length != PalletStackerBleProtocol.ControlStatePacketLength)
            {
                _rawDataText.text =
                    $"Packet hex    {ToHex(packet)}\n" +
                    $"Length        {packet.Length} bytes (expected {PalletStackerBleProtocol.ControlStatePacketLength})";
                return;
            }

            ushort sequence = (ushort)(packet[1] | (packet[2] << 8));
            _rawDataText.text =
                $"Packet hex    {ToHex(packet)}\n" +
                $"Version       {packet[0]}\n" +
                $"Sequence      {sequence} (0x{sequence:X4})\n" +
                $"Flags         0x{packet[3]:X2} ({Convert.ToString(packet[3], 2).PadLeft(8, '0')})\n" +
                $"Steer byte    {unchecked((sbyte)packet[4])}     Tiller byte {packet[5]}\n" +
                $"Travel byte   {packet[6]}     Lift byte   {packet[7]}";
        }

        private void RefreshMappedData(PalletStackerControlState state)
        {
            if (state == null)
            {
                SetColumn(_mappedLabels1, "State");
                SetColumn(_mappedValues1, "Unavailable");
                SetColumn(_mappedLabels2, string.Empty);
                SetColumn(_mappedValues2, string.Empty);
                SetColumn(_mappedLabels3, string.Empty);
                SetColumn(_mappedValues3, string.Empty);
                return;
            }

            string collisionInterlock = _controlDriver == null ? "-" : FormatBool(_controlDriver.CollisionInterlock);
            string drive = _controlDriver == null ? "-" : _controlDriver.TravelCommand.ToString("0.000");
            string steering = _controlDriver == null ? "-" : $"{_controlDriver.SteeringDegrees:0.#}°";

            SetColumn(_mappedLabels1, "Enabled:\nTiller stop:\nHorn:\nCollision lock:");
            SetColumn(_mappedValues1,
                $"{FormatBool(state.Enabled)}\n{FormatBool(state.TillerStop)}\n{FormatBool(state.Horn)}\n{collisionInterlock}");

            SetColumn(_mappedLabels2, "Stop:\nE-stop:\nSlow mode:\nDrive output:");
            SetColumn(_mappedValues2,
                $"{FormatBool(state.Stop)}\n{FormatBool(state.EmergencyStop)}\n{FormatBool(state.SlowMode)}\n{drive}");

            SetColumn(_mappedLabels3, "Steering:\nTiller:\nTravel:\nLift:\nSteering out:");
            SetColumn(_mappedValues3,
                $"{state.SteerDeg}°\n{state.TillerDeg}°\n{state.SafeTravelNormalized:0.000}\n{state.Lift}\n{steering}");
        }

        private void RefreshKeys()
        {
            string simulationState = _keyboardSimulator != null && _keyboardSimulator.SimulationEnabled ? "ON" : "OFF";

            SetColumn(_keysLabels1, "Simulation:\nTravel:\nForks:\nStop:\nKeyboard on/off:");
            SetColumn(_keysValues1, $"{simulationState}\nW / S\nR / F\nSpace\nTab");

            SetColumn(_keysLabels2, "Last seq:\nSteer:\nHorn:\nE-stop:\nCollision:");
            SetColumn(_keysValues2,
                $"{FormatSequence(_keyboardSimulator?.LastInjectedSequence)}\nA / D\nH\nE\nC");

            SetColumn(_keysLabels3, "\nTiller:\nSlow:\nEnable:\nClear collision stop:");
            SetColumn(_keysValues3, "\nUp / Down\nShift\nX\nV");
        }

        private static void SetColumn(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private void ApplyLocalPacket(byte[] packet, string label)
        {
            _lastRawPacket = (byte[])packet.Clone();
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
            _lastRawPacket = EncodeControlState(state, sequence);
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
            RefreshEventLog();
            _scrollLogToBottom = true;

            if (_mirrorEventsToConsole) Debug.Log($"[BLE DEBUG] {message}", this);
        }

        private void RefreshEventLog()
        {
            if (_eventLogText != null) _eventLogText.text = string.Join("\n", _eventLog);
        }

        private bool IsConnected() => _bleManager != null && _bleManager.HasConnection;

        private static byte[] EncodeControlState(PalletStackerControlState state, ushort sequence)
        {
            byte flags = 0;
            if (state.Enabled) flags |= 1 << 0;
            if (state.Stop) flags |= 1 << 1;
            if (state.EmergencyStop) flags |= 1 << 2;
            if (state.Horn) flags |= 1 << 3;
            if (state.SlowMode) flags |= 1 << 4;

            return new[]
            {
                PalletStackerBleProtocol.Version,
                (byte)(sequence & 0xFF),
                (byte)(sequence >> 8),
                flags,
                unchecked((byte)(sbyte)state.SteerDeg),
                (byte)state.TillerDeg,
                (byte)state.TravelRaw,
                (byte)state.LiftState
            };
        }

        private static string FormatSequence(ushort? sequence) => sequence.HasValue ? sequence.Value.ToString() : "-";
        private static string FormatBool(bool value) => value ? "YES" : "NO";
        private static string ToHex(byte[] data) => data == null || data.Length == 0 ? "-" : BitConverter.ToString(data).Replace('-', ' ');

        private static void ApplyFixedWidthSpacing(TMP_Text text)
        {
            if (text != null) text.text = $"<mspace=9px>{text.text}</mspace>";
        }
    }
}
