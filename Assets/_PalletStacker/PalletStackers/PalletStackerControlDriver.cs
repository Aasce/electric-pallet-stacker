using System;
using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerControlDriver : MonoBehaviour
    {
        [Header("State source")]
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private PalletStacker _palletStacker;

        [Header("Optional visuals")]
        [SerializeField] private Transform _steeringVisual;
        [SerializeField] private Vector3 _steeringLocalAxis = Vector3.up;
        [SerializeField] private Transform _tillerVisual;
        [SerializeField] private Vector3 _tillerLocalAxis = Vector3.right;

        private Quaternion _steeringRestRotation;
        private Quaternion _tillerRestRotation;

        public PalletStackerControlState CurrentState { get; private set; }
        public float TravelCommand => CurrentState?.SafeTravelNormalized ?? 0f;
        public float RawTravelCommand => CurrentState?.TravelNormalized ?? 0f;
        public float SteeringDegrees => CurrentState?.SteerDeg ?? 0f;
        public bool HornActive => CurrentState?.Horn ?? false;
        public bool SlowMode => CurrentState?.SlowMode ?? false;
        public bool EmergencyStop => CurrentState?.EmergencyStop ?? false;

        public event Action<PalletStackerControlState, ushort> ControlUpdated;

        private void Awake()
        {
            if (_palletStacker == null) _palletStacker = GetComponent<PalletStacker>();
            if (_steeringVisual != null) _steeringRestRotation = _steeringVisual.localRotation;
            if (_tillerVisual != null) _tillerRestRotation = _tillerVisual.localRotation;
        }

        private void OnEnable()
        {
            if (_stateReceiver == null) return;
            _stateReceiver.StateApplied += HandleStateApplied;

            if (_stateReceiver.LastAppliedSequence.HasValue)
            {
                HandleStateApplied(
                    _stateReceiver.CurrentState,
                    _stateReceiver.LastAppliedSequence.Value,
                    PalletStackerControlFields.None);
            }
        }

        private void OnDisable()
        {
            if (_stateReceiver != null) _stateReceiver.StateApplied -= HandleStateApplied;
        }

        private void HandleStateApplied(
            PalletStackerControlState state,
            ushort sequence,
            PalletStackerControlFields changedFields)
        {
            CurrentState = state;

            if (_palletStacker != null)
            {
                float liftDirection = state.Lift switch
                {
                    PalletStackerLiftState.Up => 1f,
                    PalletStackerLiftState.Down => -1f,
                    _ => 0f
                };
                _palletStacker.SetLiftDirection(liftDirection);
            }

            if (_steeringVisual != null)
            {
                Vector3 axis = SafeAxis(_steeringLocalAxis, Vector3.up);
                _steeringVisual.localRotation = _steeringRestRotation * Quaternion.AngleAxis(state.SteerDeg, axis);
            }

            if (_tillerVisual != null)
            {
                Vector3 axis = SafeAxis(_tillerLocalAxis, Vector3.right);
                _tillerVisual.localRotation = _tillerRestRotation * Quaternion.AngleAxis(state.TillerDeg, axis);
            }

            ControlUpdated?.Invoke(state, sequence);
        }

        private static Vector3 SafeAxis(Vector3 value, Vector3 fallback)
        {
            return value.sqrMagnitude > 0.0001f ? value.normalized : fallback;
        }
    }
}
