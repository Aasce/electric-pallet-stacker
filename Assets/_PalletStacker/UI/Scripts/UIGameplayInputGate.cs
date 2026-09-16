using ElectricPalletStackers.Ble;
using ElectricPalletStackers.PalletStackers;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UIGameplayInputGate : MonoBehaviour
    {
        [SerializeField] private UIController _uiController;
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private PalletStackerControlDriver _controlDriver;
        [SerializeField, Range(0f, 1f)] private float _neutralReleaseThreshold = 0.25f;

        private bool _initialDriverEnabled;
        private bool _waitingForNeutral;

        private void Awake()
        {
            if (_controlDriver == null) return;
            _initialDriverEnabled = _controlDriver.enabled;

            if (_uiController == null || _uiController.IsInputCaptured)
                _controlDriver.enabled = false;
        }

        private void OnEnable()
        {
            if (_uiController != null)
                _uiController.InputCaptureChanged += HandleInputCaptureChanged;
            if (_stateReceiver != null)
            {
                _stateReceiver.StateApplied += HandleStateApplied;
                _stateReceiver.SourceUnavailable += HandleSourceUnavailable;
            }

            ApplyCurrentCaptureState();
        }

        private void OnDisable()
        {
            if (_uiController != null)
                _uiController.InputCaptureChanged -= HandleInputCaptureChanged;
            if (_stateReceiver != null)
            {
                _stateReceiver.StateApplied -= HandleStateApplied;
                _stateReceiver.SourceUnavailable -= HandleSourceUnavailable;
            }

            _waitingForNeutral = false;
            if (_controlDriver != null) _controlDriver.enabled = _initialDriverEnabled;
        }

        private void ApplyCurrentCaptureState()
        {
            if (_controlDriver == null || _uiController == null) return;

            if (_uiController.IsInputCaptured)
            {
                _waitingForNeutral = false;
                _controlDriver.enabled = false;
                return;
            }

            _waitingForNeutral = true;
            TryReleaseControl();
        }

        private void HandleInputCaptureChanged(bool captured)
        {
            if (_controlDriver == null) return;

            if (captured)
            {
                _waitingForNeutral = false;
                _controlDriver.enabled = false;
                return;
            }

            _waitingForNeutral = true;
            TryReleaseControl();
        }

        private void HandleStateApplied(
            PalletStackerControlState state,
            ushort sequence,
            PalletStackerControlFields changedFields)
        {
            if (_uiController != null && _uiController.IsInputCaptured)
            {
                if (_controlDriver != null && _controlDriver.enabled)
                    _controlDriver.enabled = false;
                return;
            }

            TryReleaseControl();
        }

        private void HandleSourceUnavailable()
        {
            if (_uiController != null && _uiController.IsInputCaptured && _controlDriver != null)
                _controlDriver.enabled = false;
        }

        private void TryReleaseControl()
        {
            if (!_waitingForNeutral || !_initialDriverEnabled || _controlDriver == null || _stateReceiver == null)
                return;

            PalletStackerControlState state = _stateReceiver.CurrentState;
            if (state == null || Mathf.Abs(state.TravelNormalized) > _neutralReleaseThreshold) return;

            _waitingForNeutral = false;
            _controlDriver.enabled = true;
        }
    }
}
