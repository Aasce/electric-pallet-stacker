using System;
using System.Collections.Generic;
using ElectricPalletStackers.Ble;
using ElectricPalletStackers.Gameplay;
using UnityEngine;

namespace ElectricPalletStackers.PalletStackers
{
    [DisallowMultipleComponent]
    public sealed class PalletStackerControlDriver : MonoBehaviour
    {
        [Header("Composition")]
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
        [SerializeField] private PalletStackerVehicleSettings _settings;
        [Tooltip("MonoBehaviours implementing IPalletStackerControlOutput.")]
        [SerializeField] private MonoBehaviour[] _outputComponents;

        [Header("Operational safety interlocks")]
        [SerializeField] private PalletStackerRigidbodyMotor _interlockMotor;
        [SerializeField] private PalletStackerMast _interlockMast;
        [Tooltip("Travel is force-stopped when the forks are more than this distance above their minimum height.")]
        [SerializeField, Min(0f)] private float _travelHeightTolerance = 0.05f;
        [Tooltip("Actual vehicle speed above this value blocks lift motion.")]
        [SerializeField, Min(0f)] private float _movingSpeedThreshold = 0.01f;

        [Header("Analog input smoothing")]
        [Tooltip("Time in seconds used to blend analog controls between received states. Set to 0 to disable smoothing.")]
        [SerializeField, Min(0f)] private float _analogSmoothingTime = 0.08f;

        private readonly List<IPalletStackerControlOutput> _outputs = new List<IPalletStackerControlOutput>();
        private bool _sourceSubscribed;
        private bool _collisionInterlock;
        private bool _gameplayInterlock;
        private bool _inputCaptureInterlock;
        private bool _phoneCallInterlock;
        private bool _hasSmoothedCommand;
        private float _smoothedTravel;
        private float _smoothedTravelInput;
        private float _smoothedSteering;
        private float _smoothedTiller;
        private float _smoothingStartTravel;
        private float _smoothingStartTravelInput;
        private float _smoothingStartSteering;
        private float _smoothingStartTiller;
        private float _smoothingElapsed;
        private bool _previouslyMoving;
        private bool _previouslyAboveTravelHeight;
        private PalletStackerDriveCommand _targetCommand = PalletStackerDriveCommand.CreateFailSafe();

        public PalletStackerControlState CurrentState { get; private set; }
        public PalletStackerDriveCommand CurrentCommand { get; private set; } = PalletStackerDriveCommand.CreateFailSafe();
        public float TravelCommand => CurrentCommand.TravelNormalized;
        public float RawTravelCommand => CurrentState?.TravelNormalized ?? 0f;
        public float SteeringDegrees => CurrentCommand.SteeringDegrees;
        public bool HornActive => CurrentCommand.Horn;
        public bool SlowMode => CurrentCommand.SlowMode;
        public bool EmergencyStop => CurrentCommand.EmergencyStop;
        public bool CollisionInterlock => _collisionInterlock;
        public bool GameplayInterlock => _gameplayInterlock;
        public bool InputCaptureInterlock => _inputCaptureInterlock;
        public bool PhoneCallInterlock => _phoneCallInterlock;

        public event Action<PalletStackerControlState, ushort> ControlUpdated;
        public event Action<PalletStackerDriveCommand> CommandApplied;
        public event Action CollisionInterlockEngaged;

        private void OnEnable()
        {
            CacheOutputs();
            Subscribe();
            CacheOperationalInterlockState();

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
            _hasSmoothedCommand = false;
            StopAllOutputs();
        }

        private void Update()
        {
            RefreshOperationalInterlocks();

            if (!_hasSmoothedCommand ||
                _analogSmoothingTime <= 0f ||
                _smoothingElapsed >= _analogSmoothingTime)
                return;

            _smoothingElapsed = Mathf.Min(
                _smoothingElapsed + Time.unscaledDeltaTime,
                _analogSmoothingTime);
            float blend = _smoothingElapsed / _analogSmoothingTime;
            float travelTarget = _targetCommand.MovementInhibited
                ? 0f
                : _targetCommand.TravelNormalized;
            _smoothedTravel = _targetCommand.MovementInhibited
                ? 0f
                : Mathf.Lerp(_smoothingStartTravel, travelTarget, blend);
            _smoothedTravelInput = Mathf.Lerp(
                _smoothingStartTravelInput,
                _targetCommand.TravelInputNormalized,
                blend);
            _smoothedSteering = Mathf.Lerp(
                _smoothingStartSteering,
                _targetCommand.SteeringDegrees,
                blend);
            _smoothedTiller = Mathf.Lerp(
                _smoothingStartTiller,
                _targetCommand.TillerDegrees,
                blend);

            ApplySmoothedCommand();
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

            ReapplyCurrentState();
        }

        public void SetGameplayInterlock(bool engaged)
        {
            if (_gameplayInterlock == engaged) return;
            _gameplayInterlock = engaged;
            ReapplyCurrentState();
        }

        public void SetInputCaptureInterlock(bool engaged)
        {
            if (_inputCaptureInterlock == engaged) return;
            _inputCaptureInterlock = engaged;
            ReapplyCurrentState();
        }

        public void SetPhoneCallInterlock(bool engaged)
        {
            if (_phoneCallInterlock == engaged) return;
            _phoneCallInterlock = engaged;
            ReapplyCurrentState();
        }

        private void ReapplyCurrentState()
        {

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
                SlowModeTravelMultiplier,
                _collisionInterlock,
                _gameplayInterlock || _inputCaptureInterlock || _phoneCallInterlock,
                TravelDeadzoneNormalized);
            command = ApplyOperationalInterlocks(command);
            SetTargetCommand(command);
            ControlUpdated?.Invoke(state, sequence);
        }

        private void HandleSourceUnavailable()
        {
            CurrentState = null;
            _hasSmoothedCommand = false;
            ApplyFailSafe();
        }

        private float SlowModeTravelMultiplier => _settings != null
            ? _settings.SlowModeTravelMultiplier
            : PalletStackerVehicleSettings.DefaultSlowModeTravelMultiplier;

        private float TravelDeadzoneNormalized => _settings != null
            ? _settings.TravelDeadzoneNormalized
            : PalletStackerVehicleSettings.DefaultTravelDeadzoneNormalized;

        private void HandleLocalCollision(GameFailureReason _)
        {
            if (_collisionInterlock) return;
            _collisionInterlock = true;
            ReapplyCurrentState();
            CollisionInterlockEngaged?.Invoke();
        }

        private void ApplyFailSafe()
        {
            SetTargetCommand(PalletStackerDriveCommand.CreateFailSafe(
                _collisionInterlock || _gameplayInterlock || _inputCaptureInterlock ||
                _phoneCallInterlock));
        }

        private void SetTargetCommand(PalletStackerDriveCommand command)
        {
            _targetCommand = command;

            if (!_hasSmoothedCommand || _analogSmoothingTime <= 0f)
            {
                _smoothedTravel = command.TravelNormalized;
                _smoothedTravelInput = command.TravelInputNormalized;
                _smoothedSteering = command.SteeringDegrees;
                _smoothedTiller = command.TillerDegrees;
                _hasSmoothedCommand = true;
                _smoothingElapsed = _analogSmoothingTime;
            }
            else
            {
                _smoothingStartTravel = _smoothedTravel;
                _smoothingStartTravelInput = _smoothedTravelInput;
                _smoothingStartSteering = _smoothedSteering;
                _smoothingStartTiller = _smoothedTiller;
                _smoothingElapsed = 0f;

                if (command.MovementInhibited)
                {
                    // Safety-related stops and local interlocks must never be delayed by smoothing.
                    _smoothedTravel = 0f;
                    _smoothingStartTravel = 0f;
                }
            }

            ApplySmoothedCommand();
        }

        private void ApplySmoothedCommand()
        {
            ApplyCommand(_targetCommand.WithAnalogInputs(
                _smoothedTravel,
                _smoothedTravelInput,
                _smoothedSteering,
                _smoothedTiller));
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

        private void CacheOutputs()
        {
            _outputs.Clear();
            if (_outputComponents == null) return;

            for (int index = 0; index < _outputComponents.Length; index++)
            {
                if (_outputComponents[index] is IPalletStackerControlOutput output && !_outputs.Contains(output))
                    _outputs.Add(output);
            }
        }

        private PalletStackerDriveCommand ApplyOperationalInterlocks(
            PalletStackerDriveCommand command)
        {
            bool forksAboveTravelHeight = AreForksAboveTravelHeight();
            if (forksAboveTravelHeight) command = command.WithTravelSafetyStop();

            bool travelRequested = !forksAboveTravelHeight &&
                                   Mathf.Abs(command.TravelNormalized) > 0.0001f;
            if (IsVehicleMoving() || travelRequested)
                command = command.WithLift(PalletStackerLiftState.Neutral);

            return command;
        }

        private void RefreshOperationalInterlocks()
        {
            bool moving = IsVehicleMoving();
            bool forksAboveTravelHeight = AreForksAboveTravelHeight();
            if (moving == _previouslyMoving &&
                forksAboveTravelHeight == _previouslyAboveTravelHeight)
                return;

            _previouslyMoving = moving;
            _previouslyAboveTravelHeight = forksAboveTravelHeight;
            ReapplyCurrentState();
        }

        private void CacheOperationalInterlockState()
        {
            _previouslyMoving = IsVehicleMoving();
            _previouslyAboveTravelHeight = AreForksAboveTravelHeight();
        }

        private bool IsVehicleMoving()
        {
            return _interlockMotor != null &&
                   Mathf.Abs(_interlockMotor.CurrentSpeed) > _movingSpeedThreshold;
        }

        private bool AreForksAboveTravelHeight()
        {
            return _interlockMast != null &&
                   _interlockMast.CurrentHeight >
                   _interlockMast.MinHeight + _travelHeightTolerance;
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
