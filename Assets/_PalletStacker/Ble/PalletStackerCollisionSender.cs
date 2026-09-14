using System;
using System.Threading;
using System.Threading.Tasks;
using Spaxtek.Ble.Core;
using Spaxtek.Ble.Unity;
using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerCollisionSender : MonoBehaviour
    {
        [Header("BLE source")]
        [SerializeField] private BleManager _bleManager;

        [Header("Protocol UUIDs")]
        [SerializeField] private string _serviceUuid = PalletStackerBleProtocol.ServiceUuidText;
        [SerializeField] private string _collisionCharacteristicUuid = PalletStackerBleProtocol.CollisionEventUuidText;
        [SerializeField] private string _ackCharacteristicUuid = PalletStackerBleProtocol.AckUuidText;
        [SerializeField] private BleWriteMode _writeMode = BleWriteMode.WithResponse;

        [Header("Retry policy")]
        [SerializeField, Min(1)] private int _maximumAttempts = 3;
        [SerializeField, Min(1)] private int _ackTimeoutMilliseconds = 150;

        [Header("Diagnostics")]
        [SerializeField] private bool _logPackets;

        private Guid? _serviceId;
        private Guid? _ackId;
        private BleCharacteristicId? _collisionId;
        private CancellationTokenSource _lifetimeCancellation;
        private TaskCompletionSource<bool> _pendingAck;
        private ushort? _pendingSequence;
        private ushort _nextSequence;
        private bool _sendInProgress;

        public ushort NextSequence => _nextSequence;
        public ushort? PendingSequence => _pendingSequence;
        public bool IsSending => _sendInProgress;

        public event Action<ushort, int> CollisionAttempted;
        public event Action<ushort> CollisionConfirmed;
        public event Action<ushort, string> CollisionFailed;
        public event Action<ushort> UnexpectedAckReceived;
        public event Action<string> AckRejected;

        private void Awake()
        {
            RefreshCharacteristicIds();
        }

        private void OnEnable()
        {
            _lifetimeCancellation = new CancellationTokenSource();
            if (_bleManager != null) _bleManager.OnNotificationReceived += HandleNotificationReceived;
        }

        private void OnDisable()
        {
            if (_bleManager != null) _bleManager.OnNotificationReceived -= HandleNotificationReceived;

            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
            _pendingAck?.TrySetCanceled();
            _pendingAck = null;
            _pendingSequence = null;
            _sendInProgress = false;
        }

        public void Configure(BleManager bleManager)
        {
            if (ReferenceEquals(_bleManager, bleManager)) return;

            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled && _bleManager != null)
                _bleManager.OnNotificationReceived -= HandleNotificationReceived;

            _bleManager = bleManager;

            if (wasEnabled && _bleManager != null)
                _bleManager.OnNotificationReceived += HandleNotificationReceived;
        }

        public void ConfigureCharacteristics(string serviceUuid, string collisionUuid, string ackUuid)
        {
            _serviceUuid = serviceUuid ?? string.Empty;
            _collisionCharacteristicUuid = collisionUuid ?? string.Empty;
            _ackCharacteristicUuid = ackUuid ?? string.Empty;
            RefreshCharacteristicIds();
        }

        public void ConfigureWriteMode(BleWriteMode writeMode)
        {
            _writeMode = writeMode;
        }

        [ContextMenu("Send Collision Event")]
        public void SendCollision()
        {
            SendCollisionAndLogAsync();
        }

        public async Task<bool> SendCollisionAsync(CancellationToken cancellationToken = default)
        {
            if (_sendInProgress)
            {
                CollisionFailed?.Invoke(_pendingSequence ?? _nextSequence, "A collision event is already awaiting ACK.");
                return false;
            }

            ushort sequence = _nextSequence;
            _nextSequence = unchecked((ushort)(_nextSequence + 1));

            if (_bleManager == null || !_bleManager.HasConnection)
                return Fail(sequence, "There is no active BLE connection.");
            if (!_collisionId.HasValue)
                return Fail(sequence, "COLLISION_EVENT characteristic UUID is not configured.");

            _sendInProgress = true;
            _pendingSequence = sequence;
            _pendingAck = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            byte[] packet = PalletStackerBleProtocol.EncodeSequencePacket(sequence);

            using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation?.Token ?? CancellationToken.None);

            try
            {
                for (int attempt = 1; attempt <= _maximumAttempts; attempt++)
                {
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    CollisionAttempted?.Invoke(sequence, attempt);
                    if (_logPackets)
                        Debug.Log($"[BLE] COLLISION_EVENT TX seq={sequence} attempt={attempt}/{_maximumAttempts}", this);

                    BleWriteResult writeResult = await _bleManager.WriteAsync(
                        _collisionId.Value,
                        packet,
                        _writeMode,
                        linkedCancellation.Token);

                    if (!writeResult.Succeeded)
                    {
                        if (_logPackets)
                            Debug.LogWarning($"[BLE] COLLISION_EVENT write failed: {writeResult.ErrorCode}: {writeResult.Detail}", this);
                        continue;
                    }

                    Task timeout = Task.Delay(_ackTimeoutMilliseconds, linkedCancellation.Token);
                    Task completed = await Task.WhenAny(_pendingAck.Task, timeout);
                    linkedCancellation.Token.ThrowIfCancellationRequested();

                    if (completed == _pendingAck.Task && _pendingAck.Task.Status == TaskStatus.RanToCompletion)
                    {
                        if (_logPackets) Debug.Log($"[BLE] COLLISION ACK RX seq={sequence}", this);
                        CollisionConfirmed?.Invoke(sequence);
                        return true;
                    }
                }

                return Fail(sequence, $"No matching ACK after {_maximumAttempts} attempt(s).", log: true);
            }
            catch (OperationCanceledException)
            {
                return Fail(sequence, "Collision send was cancelled.", log: false);
            }
            catch (Exception exception)
            {
                return Fail(sequence, exception.Message, log: true);
            }
            finally
            {
                _pendingAck = null;
                _pendingSequence = null;
                _sendInProgress = false;
            }
        }

        private async void SendCollisionAndLogAsync()
        {
            await SendCollisionAsync();
        }

        private void HandleNotificationReceived(BleNotification notification)
        {
            if (!_serviceId.HasValue || !_ackId.HasValue) return;
            if (notification.CharacteristicId.ServiceUuid != _serviceId.Value) return;
            if (notification.CharacteristicId.CharacteristicUuid != _ackId.Value) return;

            if (!PalletStackerBleProtocol.TryDecodeSequencePacket(
                    notification.Data.Span,
                    out ushort sequence,
                    out string error))
            {
                Debug.LogWarning($"[BLE] Rejected COLLISION ACK: {error}", this);
                AckRejected?.Invoke(error);
                return;
            }

            if (!_pendingSequence.HasValue || sequence != _pendingSequence.Value)
            {
                UnexpectedAckReceived?.Invoke(sequence);
                return;
            }

            _pendingAck?.TrySetResult(true);
        }

        private bool Fail(ushort sequence, string error, bool log = true)
        {
            if (log) Debug.LogWarning($"[BLE] COLLISION_EVENT seq={sequence} failed: {error}", this);
            CollisionFailed?.Invoke(sequence, error);
            return false;
        }

        private void RefreshCharacteristicIds()
        {
            _serviceId = ParseUuid(_serviceUuid, "Service");
            Guid? collisionUuid = ParseUuid(_collisionCharacteristicUuid, "COLLISION_EVENT characteristic");
            _ackId = ParseUuid(_ackCharacteristicUuid, "ACK characteristic");
            _collisionId = _serviceId.HasValue && collisionUuid.HasValue
                ? new BleCharacteristicId(_serviceId.Value, collisionUuid.Value)
                : null;
        }

        private Guid? ParseUuid(string value, string label)
        {
            if (Guid.TryParse(value, out Guid uuid) && uuid != Guid.Empty) return uuid;
            if (Application.isPlaying) Debug.LogError($"{label} UUID is invalid: '{value}'.", this);
            return null;
        }
    }
}
