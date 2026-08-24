using System;
using System.Collections.Generic;
using ElectricPalletStackers.Ble;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerControlDriver : MonoBehaviour
    {
        [Header("Composition")]
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
        [Tooltip("MonoBehaviours implementing IPalletStackerControlOutput.")]
        [SerializeField] private MonoBehaviour[] _outputComponents;

        [Header("Command policy")]
        [SerializeField, Range(0.05f, 1f)] private float _slowModeTravelMultiplier = 0.4f;

        private readonly List<IPalletStackerControlOutput> _outputs = new List<IPalletStackerControlOutput>();
        private bool _sourceSubscribed;
        private bool _collisionInterlock;

        public PalletStackerControlState CurrentState { get; private set; }
        public PalletStackerDriveCommand CurrentCommand { get; private set; } = PalletStackerDriveCommand.CreateFailSafe();
        public float TravelCommand => CurrentCommand.TravelNormalized;
        public float RawTravelCommand => CurrentState?.TravelNormalized ?? 0f;
        public float SteeringDegrees => CurrentCommand.SteeringDegrees;
        public bool HornActive => CurrentCommand.Horn;
        public bool SlowMode => CurrentCommand.SlowMode;
        public bool EmergencyStop => CurrentCommand.EmergencyStop;
        public bool CollisionInterlock => _collisionInterlock;

        public event Action<PalletStackerControlState, ushort> ControlUpdated;
        public event Action<PalletStackerDriveCommand> CommandApplied;
        public event Action CollisionInterlockEngaged;

        private void Awake()
        {
            ResolveDependencies();
            CacheOutputs();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            CacheOutputs();
            Subscribe();

            if (_stateReceiver != null && _stateReceiver.LastAppliedSequence.HasValue)
            {
                HandleStateApplied(
                    _stateReceiver.CurrentState,
                    _stateReceiver.LastAppliedSequence.Value,
                    PalletStackerControlFields.None);
            }
            else
            {
                ApplyFailSafe();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopAllOutputs();
        }

        private void OnValidate()
        {
            _slowModeTravelMultiplier = Mathf.Clamp(_slowModeTravelMultiplier, 0.05f, 1f);
        }

        public void Bind(PalletStackerControlStateReceiver stateReceiver)
        {
            if (ReferenceEquals(_stateReceiver, stateReceiver)) return;
            Unsubscribe();
            _stateReceiver = stateReceiver;
            if (isActiveAndEnabled) Subscribe();
        }

        [ContextMenu("Clear Collision Interlock")]
        public void ClearCollisionInterlock()
        {
            if (!_collisionInterlock) return;
            _collisionInterlock = false;

            if (_stateReceiver != null && _stateReceiver.LastAppliedSequence.HasValue)
            {
                HandleStateApplied(
                    _stateReceiver.CurrentState,
                    _stateReceiver.LastAppliedSequence.Value,
                    PalletStackerControlFields.None);
            }
            else
            {
                ApplyFailSafe();
            }
        }

        private void HandleStateApplied(
            PalletStackerControlState state,
            ushort sequence,
            PalletStackerControlFields changedFields)
        {
            CurrentState = state;
            PalletStackerDriveCommand command = PalletStackerDriveCommand.FromControlState(
                state,
                sequence,
                _slowModeTravelMultiplier,
                _collisionInterlock);
            ApplyCommand(command);
            ControlUpdated?.Invoke(state, sequence);
        }

        private void HandleSourceUnavailable()
        {
            CurrentState = null;
            ApplyFailSafe();
        }

        private void HandleLocalCollision()
        {
            if (_collisionInterlock) return;
            _collisionInterlock = true;
            ApplyCommand(PalletStackerDriveCommand.CreateFailSafe(localInterlock: true));
            CollisionInterlockEngaged?.Invoke();
        }

        private void ApplyFailSafe()
        {
            ApplyCommand(PalletStackerDriveCommand.CreateFailSafe(_collisionInterlock));
        }

        private void ApplyCommand(PalletStackerDriveCommand command)
        {
            CurrentCommand = command;
            for (int index = 0; index < _outputs.Count; index++) _outputs[index].Apply(command);
            CommandApplied?.Invoke(command);
        }

        private void StopAllOutputs()
        {
            for (int index = 0; index < _outputs.Count; index++) _outputs[index].StopImmediately();
        }

        private void ResolveDependencies()
        {
            if (_stateReceiver == null)
                _stateReceiver = FindFirstObjectByType<PalletStackerControlStateReceiver>();
            if (_collisionReporter == null)
                _collisionReporter = GetComponent<PalletStackerCollisionReporter>();
        }

        private void CacheOutputs()
        {
            _outputs.Clear();
            MonoBehaviour[] candidates = _outputComponents;
            if (candidates == null || candidates.Length == 0) candidates = GetComponents<MonoBehaviour>();

            for (int index = 0; index < candidates.Length; index++)
            {
                if (candidates[index] is IPalletStackerControlOutput output && !_outputs.Contains(output))
                    _outputs.Add(output);
            }
        }

        private void Subscribe()
        {
            if (_sourceSubscribed) return;
            if (_stateReceiver != null)
            {
                _stateReceiver.StateApplied += HandleStateApplied;
                _stateReceiver.SourceUnavailable += HandleSourceUnavailable;
            }

            if (_collisionReporter != null)
                _collisionReporter.CollisionDetected += HandleLocalCollision;
            _sourceSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_sourceSubscribed) return;
            if (_stateReceiver != null)
            {
                _stateReceiver.StateApplied -= HandleStateApplied;
                _stateReceiver.SourceUnavailable -= HandleSourceUnavailable;
            }

            if (_collisionReporter != null)
                _collisionReporter.CollisionDetected -= HandleLocalCollision;
            _sourceSubscribed = false;
        }
    }
}
