using System;
using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UIInputRouter : MonoBehaviour
    {
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField, Min(0.1f)] private float _resetMenuHoldSeconds = 5f;

        private bool _emergencyStopPressed;
        private bool _emergencyStopHoldRaised;
        private float _emergencyStopPressedAt;
        public event Action EmergencyStopHeld;

        private void OnEnable()
        {
            PalletStackerControlState currentState = _stateReceiver != null
                ? _stateReceiver.CurrentState
                : null;
            _emergencyStopPressed = currentState != null && currentState.EmergencyStop;
            _emergencyStopHoldRaised = false;
            _emergencyStopPressedAt = Time.unscaledTime;

            if (_stateReceiver == null) return;
            _stateReceiver.StateApplied += HandleStateApplied;
            _stateReceiver.SourceUnavailable += HandleSourceUnavailable;
        }

        private void Update()
        {
            if (!_emergencyStopPressed || _emergencyStopHoldRaised) return;
            if (Time.unscaledTime - _emergencyStopPressedAt < _resetMenuHoldSeconds) return;

            _emergencyStopHoldRaised = true;
            EmergencyStopHeld?.Invoke();
        }

        private void OnDisable()
        {
            if (_stateReceiver == null) return;
            _stateReceiver.StateApplied -= HandleStateApplied;
            _stateReceiver.SourceUnavailable -= HandleSourceUnavailable;
        }

        private void HandleStateApplied(
            PalletStackerControlState state,
            ushort sequence,
            PalletStackerControlFields changedFields)
        {
            if (state == null ||
                (changedFields & PalletStackerControlFields.EmergencyStop) == 0) return;

            if (!state.EmergencyStop)
            {
                _emergencyStopPressed = false;
                _emergencyStopHoldRaised = false;
                return;
            }

            _emergencyStopPressed = true;
            _emergencyStopHoldRaised = false;
            _emergencyStopPressedAt = Time.unscaledTime;
        }

        private void HandleSourceUnavailable()
        {
            _emergencyStopPressed = false;
            _emergencyStopHoldRaised = false;
        }

    }
}
