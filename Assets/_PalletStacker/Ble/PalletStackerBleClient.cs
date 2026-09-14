using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
        [SerializeField, Min(1f)] private float _scanTimeoutSeconds = 10f;
        [SerializeField] private string _deviceName = PalletStackerBleProtocol.DeviceName;
        [SerializeField] private string _serviceUuid = PalletStackerBleProtocol.ServiceUuidText;

        [Header("Diagnostics")]
        [SerializeField] private bool _logLifecycle = true;

        private CancellationTokenSource _lifetimeCancellation;
        private TaskCompletionSource<BleAdvertisement> _advertisementCompletion;
        private bool _operationInProgress;

        public bool IsReady { get; private set; }

        public event Action Ready;
        public event Action<BleConnectionState> ConnectionStateChanged;
        public event Action<string> ClientFailed;

        private void OnEnable()
        {
            _lifetimeCancellation = new CancellationTokenSource();

            if (_bleManager != null)
            {
                _bleManager.OnAdvertisementReceived += HandleAdvertisementReceived;
                _bleManager.OnConnectionStateChanged += HandleConnectionStateChanged;
            }

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

            if (_connectOnEnable) StartClient();
        }

        private void OnDisable()
        {
            if (_bleManager != null)
            {
                _bleManager.OnAdvertisementReceived -= HandleAdvertisementReceived;
                _bleManager.OnConnectionStateChanged -= HandleConnectionStateChanged;
            }

            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
            _advertisementCompletion?.TrySetCanceled();
            _advertisementCompletion = null;
            _operationInProgress = false;
            IsReady = false;

            if (_disconnectOnDisable && _bleManager != null && _bleManager.HasConnection)
                DisconnectAndLogAsync();
        }

        [ContextMenu("Start BLE Client")]
        public void StartClient()
        {
            StartClientAndLogAsync();
        }

        [ContextMenu("Disconnect BLE Client")]
        public void Disconnect()
        {
            DisconnectAndLogAsync();
        }

        public async Task ConnectAndSubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (_operationInProgress) return;
            if (_bleManager == null) throw new InvalidOperationException("BleManager is not assigned.");
            if (!Guid.TryParse(_serviceUuid, out Guid serviceUuid) || serviceUuid == Guid.Empty)
                throw new InvalidOperationException($"Service UUID is invalid: '{_serviceUuid}'.");

            _operationInProgress = true;
            IsReady = false;

            using CancellationTokenSource linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _lifetimeCancellation?.Token ?? CancellationToken.None);

            try
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

                await EnsureAdapterReadyAsync(linkedCancellation.Token);
                BleAdvertisement advertisement = await FindDeviceAsync(linkedCancellation.Token);

                if (_logLifecycle)
                    Debug.Log($"[BLE] Connecting to {advertisement.Name} ({advertisement.DeviceId})", this);

                await _bleManager.ConnectAsync(advertisement.DeviceId, linkedCancellation.Token);
                IReadOnlyList<BleGattService> services = await _bleManager.DiscoverServicesAsync(
                    BleCacheMode.Uncached,
                    linkedCancellation.Token);

                BleGattService service = FindService(services, serviceUuid);
                BleCharacteristicId controlId = RequireCharacteristic(
                    service,
                    PalletStackerBleProtocol.ControlStateUuid,
                    BleCharacteristicProperties.Notify,
                    "CONTROL_STATE");
                BleGattCharacteristic collision = RequireWritableCharacteristic(
                    service,
                    PalletStackerBleProtocol.CollisionEventUuid,
                    "COLLISION_EVENT");
                BleGattCharacteristic ack = RequireAckCharacteristic(service);

                _stateReceiver?.ConfigureAckWriteMode(SelectWriteMode(ack.Properties));
                _collisionSender?.ConfigureWriteMode(SelectWriteMode(collision.Properties));
                await _bleManager.SubscribeAsync(controlId, linkedCancellation.Token);
                await _bleManager.SubscribeAsync(ack.Id, linkedCancellation.Token);

                _stateReceiver?.ResetSequence();
                IsReady = true;
                if (_logLifecycle) Debug.Log("[BLE] Pallet stacker client is ready.", this);
                Ready?.Invoke();
            }
            finally
            {
                _operationInProgress = false;
            }
        }

        private async Task<BleAdvertisement> FindDeviceAsync(CancellationToken cancellationToken)
        {
            _advertisementCompletion = new TaskCompletionSource<BleAdvertisement>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            using CancellationTokenSource scanCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            scanCancellation.CancelAfter(TimeSpan.FromSeconds(_scanTimeoutSeconds));

            try
            {
                if (_logLifecycle) Debug.Log($"[BLE] Scanning for {_deviceName}...", this);
                await _bleManager.StartScanAsync(
                    new BleScanFilter(_deviceName),
                    scanCancellation.Token);

                Task cancelled = Task.Delay(Timeout.Infinite, scanCancellation.Token);
                Task completed = await Task.WhenAny(_advertisementCompletion.Task, cancelled);
                if (completed != _advertisementCompletion.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException($"BLE device '{_deviceName}' was not found within {_scanTimeoutSeconds:0.#} seconds.");
                }

                return await _advertisementCompletion.Task;
            }
            finally
            {
                _advertisementCompletion = null;
                try
                {
                    await _bleManager.StopScanAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    if (_logLifecycle) Debug.LogWarning($"[BLE] Could not stop scan cleanly: {exception.Message}", this);
                }
            }
        }

        private async Task EnsureAdapterReadyAsync(CancellationToken cancellationToken)
        {
            IBleAdapter adapter = _bleManager.Adapter;
            for (int frame = 0; adapter == null && frame < 10; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                adapter = _bleManager.Adapter;
            }

            if (adapter == null)
                throw new InvalidOperationException("BLE adapter was not created after waiting for BleManager initialization.");

            if (adapter.State == BleAdapterState.Uninitialized)
                await _bleManager.InitializeAsync(cancellationToken);
            else if (adapter.State == BleAdapterState.Initializing)
                await WaitForAdapterInitializationAsync(cancellationToken);

            if (adapter.State != BleAdapterState.Ready)
                throw new InvalidOperationException($"BLE adapter is not ready. Current state: {adapter.State}.");
        }

        private async Task WaitForAdapterInitializationAsync(CancellationToken cancellationToken)
        {
            TaskCompletionSource<BleAdapterState> completion = new TaskCompletionSource<BleAdapterState>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void HandleState(BleAdapterState state)
            {
                if (state != BleAdapterState.Initializing) completion.TrySetResult(state);
            }

            _bleManager.OnAdapterStateChanged += HandleState;
            using CancellationTokenRegistration registration = cancellationToken.Register(
                () => completion.TrySetCanceled());

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

        private void HandleAdvertisementReceived(BleAdvertisement advertisement)
        {
            if (!string.Equals(advertisement.Name, _deviceName, StringComparison.Ordinal)) return;
            _advertisementCompletion?.TrySetResult(advertisement);
        }

        private void HandleConnectionStateChanged(BleConnectionState state)
        {
            if (state != BleConnectionState.Connected) IsReady = false;
            ConnectionStateChanged?.Invoke(state);
        }

        private async void StartClientAndLogAsync()
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
                IsReady = false;
                Debug.LogError($"[BLE] Pallet stacker client failed: {exception.Message}", this);
                ClientFailed?.Invoke(exception.Message);
            }
        }

        private async void DisconnectAndLogAsync()
        {
            try
            {
                await _bleManager.DisconnectAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[BLE] Disconnect failed: {exception.Message}", this);
            }
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

                BleCharacteristicProperties writable = BleCharacteristicProperties.Write |
                                                        BleCharacteristicProperties.WriteWithoutResponse;
                if ((characteristic.Properties & writable) == 0)
                    throw new InvalidOperationException($"{label} is not writable.");
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

                BleCharacteristicProperties writable = BleCharacteristicProperties.Write |
                                                        BleCharacteristicProperties.WriteWithoutResponse;
                if ((characteristic.Properties & BleCharacteristicProperties.Notify) == 0 ||
                    (characteristic.Properties & writable) == 0)
                {
                    throw new InvalidOperationException("ACK characteristic must support Notify and Write or WriteWithoutResponse.");
                }

                return characteristic;
            }

            throw new InvalidOperationException(
                $"Required ACK characteristic was not found: {PalletStackerBleProtocol.AckUuid:D}.");
        }

        private static BleWriteMode SelectWriteMode(BleCharacteristicProperties properties)
        {
            return (properties & BleCharacteristicProperties.Write) != 0
                ? BleWriteMode.WithResponse
                : BleWriteMode.WithoutResponse;
        }
    }
}
