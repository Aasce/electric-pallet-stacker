using System;
using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.UI
{
    [DisallowMultipleComponent]
    public sealed class UIInputRouter : MonoBehaviour
    {
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField, Range(0f, 1f)] private float _navigationEnterThreshold = 0.60f;
        [SerializeField, Range(0f, 1f)] private float _navigationReleaseThreshold = 0.25f;
        [SerializeField, Min(0f)] private float _confirmDebounceSeconds = 0.20f;

        private bool _navigationArmed;
        private bool _hasObservedAppliedState;
        private float _lastConfirmTime = float.NegativeInfinity;

        public float LatestTravelAxis { get; private set; }

        public event Action NavigateNext;
        public event Action NavigatePrevious;
        public event Action Confirm;

        private void OnEnable()
        {
            LatestTravelAxis = _stateReceiver != null && _stateReceiver.CurrentState != null
                ? _stateReceiver.CurrentState.TravelNormalized
                : 0f;
            _navigationArmed = Mathf.Abs(LatestTravelAxis) <= _navigationReleaseThreshold;
            _hasObservedAppliedState = _stateReceiver != null && _stateReceiver.LastAppliedSequence.HasValue;

            if (_stateReceiver == null) return;
            _stateReceiver.StateApplied += HandleStateApplied;
            _stateReceiver.SourceUnavailable += HandleSourceUnavailable;
        }

        private void OnDisable()
        {
            if (_stateReceiver == null) return;
            _stateReceiver.StateApplied -= HandleStateApplied;
            _stateReceiver.SourceUnavailable -= HandleSourceUnavailable;
        }

        public void ResetForPanel()
        {
            _navigationArmed = Mathf.Abs(LatestTravelAxis) <= _navigationReleaseThreshold;
        }

        private void HandleStateApplied(
            PalletStackerControlState state,
            ushort sequence,
            PalletStackerControlFields changedFields)
        {
            if (state == null) return;

            if ((changedFields & PalletStackerControlFields.TravelRaw) != 0)
            {
                LatestTravelAxis = state.TravelNormalized;
                ProcessNavigation(LatestTravelAxis);
            }

            bool mayConfirm = _hasObservedAppliedState;
            _hasObservedAppliedState = true;
            if (!mayConfirm || (changedFields & PalletStackerControlFields.EmergencyStop) == 0) return;

            float now = Time.unscaledTime;
            if (now - _lastConfirmTime < _confirmDebounceSeconds) return;

            _lastConfirmTime = now;
            Confirm?.Invoke();
        }

        private void ProcessNavigation(float travelAxis)
        {
            float magnitude = Mathf.Abs(travelAxis);
            if (magnitude <= _navigationReleaseThreshold)
            {
                _navigationArmed = true;
                return;
            }

            if (!_navigationArmed || magnitude < _navigationEnterThreshold) return;

            _navigationArmed = false;
            if (travelAxis > 0f)
                NavigatePrevious?.Invoke();
            else
                NavigateNext?.Invoke();
        }

        private void HandleSourceUnavailable()
        {
            LatestTravelAxis = 0f;
            _navigationArmed = true;
        }

        private void OnValidate()
        {
            _navigationReleaseThreshold = Mathf.Min(_navigationReleaseThreshold, _navigationEnterThreshold);
        }
    }
}
