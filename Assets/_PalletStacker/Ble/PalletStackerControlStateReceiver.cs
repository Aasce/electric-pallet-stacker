using System;
using System.Threading;
using System.Threading.Tasks;
using Spaxtek.Ble.Core;
using Spaxtek.Ble.Unity;
using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerControlStateReceiver : MonoBehaviour
    {
        [Header("BLE source")]
        [SerializeField] private BleManager _bleManager;

        [Header("Protocol UUIDs")]
        [SerializeField] private string _serviceUuid = PalletStackerBleProtocol.ServiceUuidText;
        [SerializeField] private string _controlStateCharacteristicUuid = PalletStackerBleProtocol.ControlStateUuidText;
        [SerializeField] private string _ackCharacteristicUuid = PalletStackerBleProtocol.AckUuidText;
        [SerializeField] private BleWriteMode _ackWriteMode = BleWriteMode.WithResponse;

        [Header("Diagnostics")]
        [SerializeField] private bool _logPackets;

        [Header("Current state")]
        [SerializeField] private PalletStackerControlState _currentState = new PalletStackerControlState();

        private Guid? _serviceId;
        private Guid? _controlStateId;
        private BleCharacteristicId? _ackId;
        private CancellationTokenSource _lifetimeCancellation;
        private ushort? _lastAppliedSequence;

        public PalletStackerControlState CurrentState => _currentState;
        public ushort? LastAppliedSequence => _lastAppliedSequence;

        public event Action<PalletStackerControlState, PalletStackerControlFields> StateChanged;
        public event Action<PalletStackerControlState, ushort, PalletStackerControlFields> StateApplied;
        public event Action<ushort> DuplicateReceived;
        public event Action<ushort, ushort> SequenceGapDetected;
        public event Action<ushort> ControlAcknowledged;
        public event Action<ushort, string> ControlAckFailed;
        public event Action<string> PayloadRejected;
        public event Action SourceUnavailable;

        private void Awake()
        {
            if (_currentState == null) _currentState = new PalletStackerControlState();
            RefreshCharacteristicIds();
        }

        private void OnEnable()
        {
            _lifetimeCancellation = new CancellationTokenSource();
            if (_bleManager == null) return;

            _bleManager.OnNotificationReceived += HandleNotificationReceived;
            _bleManager.OnConnectionStateChanged += HandleConnectionStateChanged;
        }

        private void OnDisable()
        {
            if (_bleManager != null)
            {
                _bleManager.OnNotificationReceived -= HandleNotificationReceived;
                _bleManager.OnConnectionStateChanged -= HandleConnectionStateChanged;
            }

            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
        }

        private void OnValidate()
        {
            RefreshCharacteristicIds();
        }

        public void Configure(BleManager bleManager)
        {
            if (ReferenceEquals(_bleManager, bleManager)) return;

            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled && _bleManager != null)
            {
                _bleManager.OnNotificationReceived -= HandleNotificationReceived;
                _bleManager.OnConnectionStateChanged -= HandleConnectionStateChanged;
            }

            _bleManager = bleManager;

            if (wasEnabled && _bleManager != null)
            {
                _bleManager.OnNotificationReceived += HandleNotificationReceived;
                _bleManager.OnConnectionStateChanged += HandleConnectionStateChanged;
            }
        }

        public void ConfigureCharacteristics(string serviceUuid, string controlStateUuid, string ackUuid)
        {
            _serviceUuid = serviceUuid ?? string.Empty;
            _controlStateCharacteristicUuid = controlStateUuid ?? string.Empty;
            _ackCharacteristicUuid = ackUuid ?? string.Empty;
            RefreshCharacteristicIds();
        }

        public void ConfigureAckWriteMode(BleWriteMode writeMode)
        {
            _ackWriteMode = writeMode;
        }

        public void ResetSequence()
        {
            _lastAppliedSequence = null;
        }

        public void ReceiveBytes(byte[] payload)
        {
            if (payload == null)
            {
                RejectPayload("CONTROL_STATE payload is null.");
                return;
            }

            ProcessControlPayload(payload);
        }

        public bool TryApplyPayload(
            ReadOnlyMemory<byte> payload,
            out ushort sequence,
            out bool duplicate,
            out string error)
        {
            if (!PalletStackerBleProtocol.TryDecodeControlState(
                    payload.Span,
                    out sequence,
                    out PalletStackerControlState candidate,
                    out error))
            {
                duplicate = false;
                return false;
            }

            duplicate = _lastAppliedSequence.HasValue && _lastAppliedSequence.Value == sequence;
            if (duplicate)
            {
                error = string.Empty;
                return true;
            }

            if (_lastAppliedSequence.HasValue)
            {
                ushort expected = unchecked((ushort)(_lastAppliedSequence.Value + 1));
                if (sequence != expected) SequenceGapDetected?.Invoke(expected, sequence);
            }

            PalletStackerControlFields changedFields = _currentState.GetChangedFields(candidate);
            _currentState = candidate;
            _lastAppliedSequence = sequence;

            if (changedFields != PalletStackerControlFields.None)
                StateChanged?.Invoke(_currentState, changedFields);
            StateApplied?.Invoke(_currentState, sequence, changedFields);

            error = string.Empty;
            return true;
        }

        private void HandleNotificationReceived(BleNotification notification)
        {
            if (!_controlStateId.HasValue || !_serviceId.HasValue) return;
            if (notification.CharacteristicId.ServiceUuid != _serviceId.Value) return;
            if (notification.CharacteristicId.CharacteristicUuid != _controlStateId.Value) return;

            ProcessControlPayload(notification.Data);
        }

        private void HandleConnectionStateChanged(BleConnectionState state)
        {
            if (state == BleConnectionState.Connected) return;
            ResetSequence();
            SourceUnavailable?.Invoke();
        }

        private void ProcessControlPayload(ReadOnlyMemory<byte> payload)
        {
            if (!TryApplyPayload(payload, out ushort sequence, out bool duplicate, out string error))
            {
                RejectPayload(error);
                return;
            }

            if (_logPackets)
            {
                string kind = duplicate ? "DUPLICATE" : "NEW";
                Debug.Log($"[BLE] CONTROL_STATE {kind} seq={sequence}: {ToHex(payload.Span)}", this);
            }

            if (duplicate) DuplicateReceived?.Invoke(sequence);

            // Every valid CONTROL_STATE is acknowledged, including retransmitted duplicates.
            _ = SendControlAckAsync(sequence);
        }

        private async Task SendControlAckAsync(ushort sequence)
        {
            if (_bleManager == null || !_bleManager.HasConnection)
            {
                ReportAckFailure(sequence, "There is no active BLE connection.");
                return;
            }

            if (!_ackId.HasValue)
            {
                ReportAckFailure(sequence, "ACK characteristic UUID is not configured.");
                return;
            }

            try
            {
                CancellationToken cancellationToken = _lifetimeCancellation?.Token ?? CancellationToken.None;
                byte[] packet = PalletStackerBleProtocol.EncodeSequencePacket(sequence);
                BleWriteResult result = await _bleManager.WriteAsync(
                    _ackId.Value,
                    packet,
                    _ackWriteMode,
                    cancellationToken);

                if (!result.Succeeded)
                {
                    ReportAckFailure(sequence, $"{result.ErrorCode}: {result.Detail}");
                    return;
                }

                if (_logPackets)
                    Debug.Log($"[BLE] CONTROL ACK TX seq={sequence}: {ToHex(packet)}", this);
                ControlAcknowledged?.Invoke(sequence);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                ReportAckFailure(sequence, exception.Message);
            }
        }

        private void RefreshCharacteristicIds()
        {
            _serviceId = ParseUuid(_serviceUuid, "Service");
            _controlStateId = ParseUuid(_controlStateCharacteristicUuid, "CONTROL_STATE characteristic");
            Guid? ackUuid = ParseUuid(_ackCharacteristicUuid, "ACK characteristic");
            _ackId = _serviceId.HasValue && ackUuid.HasValue
                ? new BleCharacteristicId(_serviceId.Value, ackUuid.Value)
                : null;
        }

        private Guid? ParseUuid(string value, string label)
        {
            if (Guid.TryParse(value, out Guid uuid) && uuid != Guid.Empty) return uuid;
            if (Application.isPlaying) Debug.LogError($"{label} UUID is invalid: '{value}'.", this);
            return null;
        }

        private void RejectPayload(string error)
        {
            Debug.LogWarning($"[BLE] Rejected CONTROL_STATE: {error}", this);
            PayloadRejected?.Invoke(error);
        }

        private void ReportAckFailure(ushort sequence, string error)
        {
            Debug.LogWarning($"[BLE] Failed to ACK CONTROL_STATE seq={sequence}: {error}", this);
            ControlAckFailed?.Invoke(sequence, error);
        }

        private static string ToHex(ReadOnlySpan<byte> data)
        {
            if (data.Length == 0) return string.Empty;

            char[] characters = new char[data.Length * 3 - 1];
            const string hex = "0123456789ABCDEF";
            for (int index = 0; index < data.Length; index++)
            {
                int outputIndex = index * 3;
                characters[outputIndex] = hex[data[index] >> 4];
                characters[outputIndex + 1] = hex[data[index] & 0x0F];
                if (index < data.Length - 1) characters[outputIndex + 2] = ' ';
            }

            return new string(characters);
        }
    }
}
