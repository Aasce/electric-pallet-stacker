using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Spaxtek.Ble.Core;
using Spaxtek.Ble.Unity;
using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    [DisallowMultipleComponent]
    [RequireComponent(
        typeof(BleManager),
        typeof(PalletStackerControlStateReceiver),
        typeof(PalletStackerCollisionSender))]
    public sealed class PalletStackerBleClient : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField] private BleManager _bleManager;
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private PalletStackerCollisionSender _collisionSender;

        [Header("Connection")]
        [SerializeField] private bool _connectOnEnable = true;
        [SerializeField] private bool _disconnectOnDisable = true;
        [SerializeField] private string _deviceName = PalletStackerBleProtocol.DeviceName;
        [SerializeField] private string _serviceUuid = PalletStackerBleProtocol.ServiceUuidText;

        [Header("Steering recenter")]
        [Tooltip("Retry count for a lost RECENTER_RSP. Every retry reuses the same request_id.")]
        [SerializeField, Min(1)] private int _recenterMaximumAttempts = 2;
        [SerializeField, Min(100)] private int _recenterResponseTimeoutMilliseconds = 1000;
        [SerializeField, Min(100)] private int _recenterControlTimeoutMilliseconds = 1000;
        [SerializeField, Min(0)] private int _recenterConfirmationToleranceDegrees = 2;

        [Header("Diagnostics")]
        [SerializeField] private bool _logLifecycle = true;

        private CancellationTokenSource _lifetimeCancellation;
        private CancellationTokenSource _connectionCancellation;
        private UniTaskCompletionSource<BleAdvertisement> _advertisementCompletion;
        private UniTaskCompletionSource<bool> _recenterResponseCompletion;
        private UniTaskCompletionSource<bool> _recenterControlCompletion;
        private BleCharacteristicId? _recenterRequestId;
        private BleCharacteristicId? _recenterResponseId;
        private ushort? _recenterBaselineControlSequence;
        private byte _nextRecenterRequestId;
        private byte _pendingRecenterRequestId;
        private bool _operationInProgress;
        private bool _recenterInProgress;

        public bool IsReady { get; private set; }
        public bool IsRecenterInProgress => _recenterInProgress;
        public bool CanRecenter => IsReady && !_recenterInProgress &&
                                   _bleManager != null && _bleManager.HasConnection &&
                                   _recenterRequestId.HasValue && _recenterResponseId.HasValue;

        public event Action Ready;
        public event Action<BleConnectionState> ConnectionStateChanged;
        public event Action<string> ClientFailed;
        public event Action<bool> RecenterAvailabilityChanged;
        public event Action<byte> SteeringRecenterSucceeded;
        public event Action<byte, string> SteeringRecenterFailed;

        private void OnEnable()
        {
            _lifetimeCancellation = new CancellationTokenSource();

            if (_bleManager != null)
            {
                _bleManager.OnAdvertisementReceived += HandleAdvertisementReceived;
                _bleManager.OnConnectionStateChanged += HandleConnectionStateChanged;
                _bleManager.OnNotificationReceived += HandleNotificationReceived;
            }

            if (_stateReceiver != null)
                _stateReceiver.StateApplied += HandleControlStateApplied;

            ConfigureProtocolComponents();
            if (_connectOnEnable) StartClient();
        }

        private void OnDisable()
        {
            if (_bleManager != null)
            {
                _bleManager.OnAdvertisementReceived -= HandleAdvertisementReceived;
                _bleManager.OnConnectionStateChanged -= HandleConnectionStateChanged;
                _bleManager.OnNotificationReceived -= HandleNotificationReceived;
            }

            if (_stateReceiver != null)
                _stateReceiver.StateApplied -= HandleControlStateApplied;

            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
            EndConnectionLifetime();
            _advertisementCompletion?.TrySetCanceled();
            _advertisementCompletion = null;
            CancelPendingRecenter();
            _operationInProgress = false;
            SetReady(false);

            if (_disconnectOnDisable && _bleManager != null && _bleManager.HasConnection)
                DisconnectAndLogAsync().Forget();
        }

        [ContextMenu("Start BLE Client")]
        public void StartClient()
        {
            StartClientAndLogAsync().Forget();
        }

        [ContextMenu("Disconnect BLE Client")]
        public void Disconnect()
        {
            DisconnectAndLogAsync().Forget();
        }

        [ContextMenu("Recenter Steering")]
        public void RecenterSteering()
        {
            RecenterSteeringAsync().Forget();
        }

        public async UniTask ConnectAndSubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (_operationInProgress) return;
            if (_bleManager == null) throw new InvalidOperationException("BleManager is not assigned.");
            if (!Guid.TryParse(_serviceUuid, out Guid serviceUuid) || serviceUuid == Guid.Empty)
                throw new InvalidOperationException($"Service UUID is invalid: '{_serviceUuid}'.");

            _operationInProgress = true;
            SetReady(false);

            using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation?.Token ?? CancellationToken.None);

            try
            {
                ConfigureProtocolComponents();
                await EnsureAdapterReadyAsync(linkedCancellation.Token);
                BleAdvertisement advertisement = await FindDeviceAsync(linkedCancellation.Token);

                if (_logLifecycle)
                    Debug.Log($"[BLE] Connecting to {advertisement.Name} ({advertisement.DeviceId})", this);

                await _bleManager.ConnectAsync(advertisement.DeviceId, linkedCancellation.Token).AsUniTask();
                EnsureConnectionLifetime();
                IReadOnlyList<BleGattService> services = await _bleManager.DiscoverServicesAsync(
                    BleCacheMode.Uncached,
                    linkedCancellation.Token).AsUniTask();

                BleGattService service = FindService(services, serviceUuid);
                BleCharacteristicId controlId = RequireCharacteristic(
                    service,
                    PalletStackerBleProtocol.ControlStateUuid,
                    BleCharacteristicProperties.Notify,
                    "CONTROL_STATE");
                RequireWritableCharacteristic(
                    service,
                    PalletStackerBleProtocol.CollisionEventUuid,
                    "COLLISION_EVENT");
                BleGattCharacteristic ack = RequireAckCharacteristic(service);
                BleGattCharacteristic recenterRequest = RequireWritableCharacteristic(
                    service,
                    PalletStackerBleProtocol.RecenterRequestUuid,
                    "RECENTER_REQ");
                BleCharacteristicId recenterResponseId = RequireCharacteristic(
                    service,
                    PalletStackerBleProtocol.RecenterResponseUuid,
                    BleCharacteristicProperties.Notify,
                    "RECENTER_RSP");

                _stateReceiver?.ConfigureAckWriteMode(BleWriteMode.WithResponse);
                _collisionSender?.ConfigureWriteMode(BleWriteMode.WithResponse);
                await _bleManager.SubscribeAsync(controlId, linkedCancellation.Token).AsUniTask();
                await _bleManager.SubscribeAsync(ack.Id, linkedCancellation.Token).AsUniTask();
                await _bleManager.SubscribeAsync(recenterResponseId, linkedCancellation.Token).AsUniTask();

                _recenterRequestId = recenterRequest.Id;
                _recenterResponseId = recenterResponseId;
                _nextRecenterRequestId = 0;
                _stateReceiver?.ResetSequence();
                SetReady(true);
                if (_logLifecycle) Debug.Log("[BLE] Pallet stacker V20 client is ready.", this);
                Ready?.Invoke();
            }
            finally
            {
                _operationInProgress = false;
            }
        }

        public async UniTask<bool> RecenterSteeringAsync(CancellationToken cancellationToken = default)
        {
            if (_recenterInProgress)
                return FailRecenter(_pendingRecenterRequestId, "A steering recenter request is already in progress.");
            if (!IsReady || _bleManager == null || !_bleManager.HasConnection ||
                !_recenterRequestId.HasValue || !_recenterResponseId.HasValue)
                return FailRecenter(_nextRecenterRequestId, "BLE is not ready for steering recenter.");

            byte requestId = _nextRecenterRequestId;
            _nextRecenterRequestId = unchecked((byte)(_nextRecenterRequestId + 1));
            _pendingRecenterRequestId = requestId;
            _recenterBaselineControlSequence = _stateReceiver?.LastAppliedSequence;
            _recenterControlCompletion = new UniTaskCompletionSource<bool>();
            SetRecenterInProgress(true);

            using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation?.Token ?? CancellationToken.None,
                _connectionCancellation?.Token ?? CancellationToken.None);

            try
            {
                byte[] packet = PalletStackerBleProtocol.EncodeRecenterRequest(requestId);
                bool accepted = false;
                _recenterResponseCompletion = new UniTaskCompletionSource<bool>();

                for (int attempt = 1; attempt <= Mathf.Max(1, _recenterMaximumAttempts); attempt++)
                {
                    linkedCancellation.Token.ThrowIfCancellationRequested();

                    BleWriteResult writeResult = await PalletStackerBleWriteGate.WriteAsync(
                        _bleManager,
                        _recenterRequestId.Value,
                        packet,
                        BleWriteMode.WithResponse,
                        linkedCancellation.Token);

                    if (!writeResult.Succeeded)
                    {
                        if (_logLifecycle)
                        {
                            Debug.LogWarning(
                                $"[BLE] RECENTER_REQ id={requestId} attempt={attempt} write failed: " +
                                $"{writeResult.ErrorCode}: {writeResult.Detail}",
                                this);
                        }
                        continue;
                    }

                    if (_logLifecycle)
                        Debug.Log($"[BLE] RECENTER_REQ TX id={requestId} attempt={attempt}.", this);

                    (bool hasResponse, bool responseAccepted) = await UniTask.WhenAny(
                        _recenterResponseCompletion.Task,
                        UniTask.Delay(
                            Mathf.Max(100, _recenterResponseTimeoutMilliseconds),
                            DelayType.Realtime,
                            cancellationToken: linkedCancellation.Token));
                    linkedCancellation.Token.ThrowIfCancellationRequested();

                    if (!hasResponse)
                    {
                        if (_logLifecycle)
                            Debug.LogWarning($"[BLE] RECENTER_RSP timeout for id={requestId}; retrying the same id.", this);
                        continue;
                    }

                    if (!responseAccepted)
                        return FailRecenter(requestId, "ESP32 rejected recenter because steering IMU data is invalid or stale.");

                    accepted = true;
                    break;
                }

                if (!accepted)
                    return FailRecenter(requestId, "No matching RECENTER_RSP was received.");

                (bool controlConfirmed, _) = await UniTask.WhenAny(
                    _recenterControlCompletion.Task,
                    UniTask.Delay(
                        Mathf.Max(100, _recenterControlTimeoutMilliseconds),
                        DelayType.Realtime,
                        cancellationToken: linkedCancellation.Token));
                linkedCancellation.Token.ThrowIfCancellationRequested();

                if (!controlConfirmed)
                    return FailRecenter(requestId, "ESP32 accepted recenter, but no fresh near-zero CONTROL packet was received.");

                if (_logLifecycle)
                    Debug.Log($"[BLE] Steering recenter id={requestId} confirmed by a fresh CONTROL packet.", this);
                SteeringRecenterSucceeded?.Invoke(requestId);
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception exception)
            {
                return FailRecenter(requestId, exception.Message);
            }
            finally
            {
                _recenterResponseCompletion = null;
                _recenterControlCompletion = null;
                _recenterBaselineControlSequence = null;
                SetRecenterInProgress(false);
            }
        }

        private async UniTask<BleAdvertisement> FindDeviceAsync(CancellationToken cancellationToken)
        {
            UniTaskCompletionSource<BleAdvertisement> completion = new UniTaskCompletionSource<BleAdvertisement>();
            _advertisementCompletion = completion;
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_logLifecycle) Debug.Log($"[BLE] Scanning for {_deviceName}...", this);
                await _bleManager.StartScanAsync(new BleScanFilter(_deviceName), cancellationToken).AsUniTask();
                return await completion.Task;
            }
            finally
            {
                if (ReferenceEquals(_advertisementCompletion, completion))
                    _advertisementCompletion = null;

                try
                {
                    await _bleManager.StopScanAsync(CancellationToken.None).AsUniTask();
                }
                catch (Exception exception)
                {
                    if (_logLifecycle) Debug.LogWarning($"[BLE] Could not stop scan cleanly: {exception.Message}", this);
                }
            }
        }

        private async UniTask EnsureAdapterReadyAsync(CancellationToken cancellationToken)
        {
            IBleAdapter adapter = _bleManager.Adapter;
            for (int frame = 0; adapter == null && frame < 10; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.NextFrame(cancellationToken);
                adapter = _bleManager.Adapter;
            }

            if (adapter == null)
                throw new InvalidOperationException("BLE adapter was not created after waiting for BleManager initialization.");

            if (adapter.State == BleAdapterState.Uninitialized)
                await _bleManager.InitializeAsync(cancellationToken).AsUniTask();
            else if (adapter.State == BleAdapterState.Initializing)
                await WaitForAdapterInitializationAsync(cancellationToken);

            if (adapter.State != BleAdapterState.Ready)
                throw new InvalidOperationException($"BLE adapter is not ready. Current state: {adapter.State}.");
        }

        private async UniTask WaitForAdapterInitializationAsync(CancellationToken cancellationToken)
        {
            UniTaskCompletionSource<BleAdapterState> completion = new UniTaskCompletionSource<BleAdapterState>();

            void HandleState(BleAdapterState state)
            {
                if (state != BleAdapterState.Initializing) completion.TrySetResult(state);
            }

            _bleManager.OnAdapterStateChanged += HandleState;
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => completion.TrySetCanceled(cancellationToken));

            try
            {
                BleAdapterState currentState = _bleManager.Adapter.State;
                if (currentState != BleAdapterState.Initializing) completion.TrySetResult(currentState);
                await completion.Task;
            }
            finally
            {
                _bleManager.OnAdapterStateChanged -= HandleState;
            }
        }

        private void ConfigureProtocolComponents()
        {
            _stateReceiver?.Configure(_bleManager);
            _collisionSender?.Configure(_bleManager);
            _stateReceiver?.ConfigureCharacteristics(
                _serviceUuid,
                PalletStackerBleProtocol.ControlStateUuidText,
                PalletStackerBleProtocol.AckUuidText);
            _collisionSender?.ConfigureCharacteristics(
                _serviceUuid,
                PalletStackerBleProtocol.CollisionEventUuidText,
                PalletStackerBleProtocol.AckUuidText);
        }

        private void HandleAdvertisementReceived(BleAdvertisement advertisement)
        {
            if (!string.Equals(advertisement.Name, _deviceName, StringComparison.Ordinal)) return;
            _advertisementCompletion?.TrySetResult(advertisement);
        }

        private void HandleConnectionStateChanged(BleConnectionState state)
        {
            if (state == BleConnectionState.Connected)
            {
                EnsureConnectionLifetime();
            }
            else
            {
                EndConnectionLifetime();
                SetReady(false);
                _recenterRequestId = null;
                _recenterResponseId = null;
                _nextRecenterRequestId = 0;
                CancelPendingRecenter();
            }

            ConnectionStateChanged?.Invoke(state);
        }

        private void EnsureConnectionLifetime()
        {
            if (_connectionCancellation != null && !_connectionCancellation.IsCancellationRequested) return;
            _connectionCancellation?.Dispose();
            _connectionCancellation = new CancellationTokenSource();
        }

        private void EndConnectionLifetime()
        {
            _connectionCancellation?.Cancel();
            _connectionCancellation?.Dispose();
            _connectionCancellation = null;
        }

        private void HandleNotificationReceived(BleNotification notification)
        {
            if (!_recenterInProgress || !_recenterResponseId.HasValue || _recenterResponseCompletion == null)
                return;
            if (notification.CharacteristicId != _recenterResponseId.Value) return;

            if (!PalletStackerBleProtocol.TryDecodeRecenterResponse(
                    notification.Data.Span,
                    _pendingRecenterRequestId,
                    out bool accepted,
                    out string error))
            {
                if (_logLifecycle) Debug.LogWarning($"[BLE] Ignored RECENTER_RSP: {error}", this);
                return;
            }

            _recenterResponseCompletion.TrySetResult(accepted);
        }

        private void HandleControlStateApplied(
            PalletStackerControlState state,
            ushort sequence,
            PalletStackerControlFields changedFields)
        {
            if (!_recenterInProgress || _recenterControlCompletion == null || state == null) return;
            if (_recenterBaselineControlSequence.HasValue && sequence == _recenterBaselineControlSequence.Value) return;
            if (Mathf.Abs(state.SteerDeg) > Mathf.Max(0, _recenterConfirmationToleranceDegrees)) return;

            _recenterControlCompletion.TrySetResult(true);
        }

        private async UniTask StartClientAndLogAsync()
        {
            try
            {
                await ConnectAndSubscribeAsync();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                SetReady(false);
                Debug.LogError($"[BLE] Pallet stacker client failed: {exception.Message}", this);
                ClientFailed?.Invoke(exception.Message);
            }
        }

        private async UniTask DisconnectAndLogAsync()
        {
            try
            {
                await _bleManager.DisconnectAsync().AsUniTask();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BLE] Disconnect failed: {exception.Message}", this);
            }
        }

        private bool FailRecenter(byte requestId, string error)
        {
            Debug.LogWarning($"[BLE] Steering recenter id={requestId} failed: {error}", this);
            SteeringRecenterFailed?.Invoke(requestId, error);
            return false;
        }

        private void CancelPendingRecenter()
        {
            _recenterResponseCompletion?.TrySetCanceled();
            _recenterControlCompletion?.TrySetCanceled();
            _recenterResponseCompletion = null;
            _recenterControlCompletion = null;
            _recenterBaselineControlSequence = null;
            SetRecenterInProgress(false);
        }

        private void SetReady(bool ready)
        {
            if (IsReady == ready) return;
            IsReady = ready;
            RecenterAvailabilityChanged?.Invoke(CanRecenter);
        }

        private void SetRecenterInProgress(bool inProgress)
        {
            if (_recenterInProgress == inProgress) return;
            _recenterInProgress = inProgress;
            RecenterAvailabilityChanged?.Invoke(CanRecenter);
        }

        private static BleGattService FindService(IReadOnlyList<BleGattService> services, Guid serviceUuid)
        {
            for (int index = 0; index < services.Count; index++)
            {
                if (services[index].Uuid == serviceUuid) return services[index];
            }

            throw new InvalidOperationException($"Required BLE service was not found: {serviceUuid:D}.");
        }

        private static BleCharacteristicId RequireCharacteristic(
            BleGattService service,
            Guid uuid,
            BleCharacteristicProperties requiredProperties,
            string label)
        {
            for (int index = 0; index < service.Characteristics.Count; index++)
            {
                BleGattCharacteristic characteristic = service.Characteristics[index];
                if (characteristic.Id.CharacteristicUuid != uuid) continue;

                if ((characteristic.Properties & requiredProperties) != requiredProperties)
                    throw new InvalidOperationException($"{label} does not expose required properties: {requiredProperties}.");
                return characteristic.Id;
            }

            throw new InvalidOperationException($"Required {label} characteristic was not found: {uuid:D}.");
        }

        private static BleGattCharacteristic RequireWritableCharacteristic(BleGattService service, Guid uuid, string label)
        {
            for (int index = 0; index < service.Characteristics.Count; index++)
            {
                BleGattCharacteristic characteristic = service.Characteristics[index];
                if (characteristic.Id.CharacteristicUuid != uuid) continue;

                if ((characteristic.Properties & BleCharacteristicProperties.Write) == 0)
                    throw new InvalidOperationException($"{label} must support writes with response.");
                return characteristic;
            }

            throw new InvalidOperationException($"Required {label} characteristic was not found: {uuid:D}.");
        }

        private static BleGattCharacteristic RequireAckCharacteristic(BleGattService service)
        {
            for (int index = 0; index < service.Characteristics.Count; index++)
            {
                BleGattCharacteristic characteristic = service.Characteristics[index];
                if (characteristic.Id.CharacteristicUuid != PalletStackerBleProtocol.AckUuid) continue;

                if ((characteristic.Properties & BleCharacteristicProperties.Notify) == 0 ||
                    (characteristic.Properties & BleCharacteristicProperties.Write) == 0)
                {
                    throw new InvalidOperationException("ACK characteristic must support Notify and writes with response.");
                }

                return characteristic;
            }

            throw new InvalidOperationException(
                $"Required ACK characteristic was not found: {PalletStackerBleProtocol.AckUuid:D}.");
        }
    }
}
