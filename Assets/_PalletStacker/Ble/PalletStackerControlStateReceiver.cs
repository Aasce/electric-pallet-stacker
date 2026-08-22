using System;
using System.Text;
using Spaxtek.Ble.Core;
using Spaxtek.Ble.Unity;
using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerControlStateReceiver : MonoBehaviour
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        [Header("BLE source")]
        [SerializeField] private BleManager _bleManager;
        [SerializeField] private string _controlStateServiceUuid = string.Empty;
        [SerializeField] private string _controlStateCharacteristicUuid = string.Empty;

        [Header("Protocol")]
        [SerializeField, Min(1)] private int _maximumPayloadBytes = 512;
        [SerializeField] private bool _logReceivedJson;

        [Header("Current state")]
        [SerializeField] private PalletStackerControlState _currentState = new PalletStackerControlState();

        private Guid? _serviceUuid;
        private Guid? _characteristicUuid;

        public PalletStackerControlState CurrentState => _currentState;

        public event Action<PalletStackerControlState, PalletStackerControlFields> StateChanged;
        public event Action<string> PayloadRejected;

        private void Awake()
        {
            if (_currentState == null) _currentState = new PalletStackerControlState();
            RefreshCharacteristicFilter();
        }

        private void OnEnable()
        {
            if (_bleManager != null) _bleManager.OnNotificationReceived += HandleNotificationReceived;
        }

        private void OnDisable()
        {
            if (_bleManager != null) _bleManager.OnNotificationReceived -= HandleNotificationReceived;
        }

        private void OnValidate()
        {
            _maximumPayloadBytes = Mathf.Max(1, _maximumPayloadBytes);
        }

        public void ConfigureCharacteristic(string serviceUuid, string characteristicUuid)
        {
            _controlStateServiceUuid = serviceUuid ?? string.Empty;
            _controlStateCharacteristicUuid = characteristicUuid ?? string.Empty;
            RefreshCharacteristicFilter();
        }

        public void ReceiveBytes(byte[] payload)
        {
            if (payload == null)
            {
                RejectPayload("Control State payload is null.");
                return;
            }

            if (!TryApplyPayload(payload, out string error)) RejectPayload(error);
        }

        public void ReceiveJson(string json)
        {
            if (!TryApplyJson(json, out string error)) RejectPayload(error);
        }

        public bool TryApplyPayload(ReadOnlyMemory<byte> payload, out string error)
        {
            if (payload.Length == 0)
            {
                error = "Control State payload is empty.";
                return false;
            }

            if (payload.Length > _maximumPayloadBytes)
            {
                error = $"Control State payload is {payload.Length} bytes; maximum is {_maximumPayloadBytes}.";
                return false;
            }

            string json;
            try
            {
                json = StrictUtf8.GetString(payload.ToArray());
            }
            catch (DecoderFallbackException exception)
            {
                error = $"Control State payload is not valid UTF-8: {exception.Message}";
                return false;
            }

            return TryApplyJson(json, out error);
        }

        public bool TryApplyJson(string json, out string error)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Control State JSON is empty.";
                return false;
            }

            if (StrictUtf8.GetByteCount(json) > _maximumPayloadBytes)
            {
                error = $"Control State JSON exceeds the {_maximumPayloadBytes}-byte limit.";
                return false;
            }

            string trimmedJson = json.Trim();
            if (trimmedJson.Length < 2 || trimmedJson[0] != '{' || trimmedJson[trimmedJson.Length - 1] != '}')
            {
                error = "Control State JSON root must be an object.";
                return false;
            }

            PalletStackerControlState candidate = _currentState.Clone();
            try
            {
                JsonUtility.FromJsonOverwrite(trimmedJson, candidate);
            }
            catch (ArgumentException exception)
            {
                error = $"Invalid Control State JSON: {exception.Message}";
                return false;
            }

            if (!TryValidate(candidate, out error)) return false;

            PalletStackerControlFields changedFields = _currentState.GetChangedFields(candidate);
            if (changedFields == PalletStackerControlFields.None)
            {
                error = string.Empty;
                return true;
            }

            _currentState = candidate;
            if (_logReceivedJson) Debug.Log($"[BLE] Control State changed ({changedFields}): {trimmedJson}", this);
            StateChanged?.Invoke(_currentState, changedFields);
            error = string.Empty;
            return true;
        }

        private void HandleNotificationReceived(BleNotification notification)
        {
            if (!_characteristicUuid.HasValue) return;
            if (notification.CharacteristicId.CharacteristicUuid != _characteristicUuid.Value) return;
            if (_serviceUuid.HasValue && notification.CharacteristicId.ServiceUuid != _serviceUuid.Value) return;

            if (!TryApplyPayload(notification.Data, out string error)) RejectPayload(error);
        }

        private void RefreshCharacteristicFilter()
        {
            _serviceUuid = ParseOptionalUuid(_controlStateServiceUuid, "Control State service");
            _characteristicUuid = ParseOptionalUuid(_controlStateCharacteristicUuid, "Control State characteristic");
        }

        private Guid? ParseOptionalUuid(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (Guid.TryParse(value, out Guid uuid)) return uuid;

            Debug.LogError($"{label} UUID is invalid: '{value}'.", this);
            return null;
        }

        private static bool TryValidate(PalletStackerControlState state, out string error)
        {
            if (state.SteerDeg < -90 || state.SteerDeg > 90)
            {
                error = $"steerDeg must be between -90 and 90; received {state.SteerDeg}.";
                return false;
            }

            if (state.TillerDeg < 0 || state.TillerDeg > 100)
            {
                error = $"tillerDeg must be between 0 and 100; received {state.TillerDeg}.";
                return false;
            }

            if (state.TravelRaw < 0 || state.TravelRaw > 255)
            {
                error = $"travelRaw must be between 0 and 255; received {state.TravelRaw}.";
                return false;
            }

            if (state.LiftState < 0 || state.LiftState > 2)
            {
                error = $"liftState must be 0, 1 or 2; received {state.LiftState}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void RejectPayload(string error)
        {
            Debug.LogWarning($"[BLE] Rejected Control State payload: {error}", this);
            PayloadRejected?.Invoke(error);
        }
    }
}
