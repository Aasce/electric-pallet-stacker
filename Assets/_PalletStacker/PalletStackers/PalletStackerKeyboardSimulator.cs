using System;
using ElectricPalletStackers.Ble;
using Spaxtek.Ble.Core;
using Spaxtek.Ble.Unity;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Development-only input adapter. It emits the same CONTROL_STATE packet
    /// consumed by BLE so gameplay never needs a keyboard-specific code path.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PalletStackerKeyboardSimulator : MonoBehaviour
    {
        private const byte EnabledFlag = 1 << 0;
        private const byte StopFlag = 1 << 1;
        private const byte EmergencyStopFlag = 1 << 2;
        private const byte HornFlag = 1 << 3;
        private const byte SlowModeFlag = 1 << 4;
        private const byte NeutralTravel = 127;

        [Header("Pipeline")]
        [SerializeField] private PalletStackerControlStateReceiver _stateReceiver;
        [SerializeField] private BleManager _bleManager;
        [SerializeField] private PalletStackerCollisionReporter _collisionReporter;
        [SerializeField] private PalletStackerControlDriver _controlDriver;

        [Header("Simulation")]
        [SerializeField] private bool _simulationEnabled = true;
        [SerializeField] private bool _yieldToBleConnection = true;
        [SerializeField] private bool _vehicleEnabled = true;
        [SerializeField, Range(1, 90)] private int _maximumSteeringDegrees = 30;
        [SerializeField, Min(1f)] private float _steeringSpeedDegreesPerSecond = 90f;
        [SerializeField, Min(1f)] private float _steeringReturnSpeedDegreesPerSecond = 120f;
        [SerializeField, Range(0, 100)] private int _tillerDegrees;
        [SerializeField, Min(1f)] private float _tillerSpeedDegreesPerSecond = 80f;

        private KeyboardStateSnapshot _lastSnapshot;
        private bool _hasLastSnapshot;
        private bool _emergencyStop;
        private bool _bleOwnsControl;
        private float _simulatedSteeringDegrees;
        private float _simulatedTillerDegrees;

        public bool SimulationEnabled => _simulationEnabled;
        public bool BleOwnsControl => _bleOwnsControl;
        public bool IsSimulationActive => _simulationEnabled && !_bleOwnsControl;
        public ushort? LastInjectedSequence { get; private set; }

        private void Awake()
        {
            ResolveDependencies();
        }

        private void OnEnable()
        {
            ResolveDependencies();
            if (_bleManager != null)
            {
                _bleManager.OnConnectionStateChanged += HandleConnectionStateChanged;
                _bleOwnsControl = _yieldToBleConnection && _bleManager.HasConnection;
            }

            _simulatedSteeringDegrees = 0f;
            _simulatedTillerDegrees = _tillerDegrees;
            _hasLastSnapshot = false;
        }

        private void OnDisable()
        {
            if (_bleManager != null)
                _bleManager.OnConnectionStateChanged -= HandleConnectionStateChanged;

            if (!_bleOwnsControl) InjectFailSafeState();
        }

        private void OnValidate()
        {
            _maximumSteeringDegrees = Mathf.Clamp(_maximumSteeringDegrees, 1, 90);
            _steeringSpeedDegreesPerSecond = Mathf.Max(1f, _steeringSpeedDegreesPerSecond);
            _steeringReturnSpeedDegreesPerSecond = Mathf.Max(1f, _steeringReturnSpeedDegreesPerSecond);
            _tillerDegrees = Mathf.Clamp(_tillerDegrees, 0, 100);
            _tillerSpeedDegreesPerSecond = Mathf.Max(1f, _tillerSpeedDegreesPerSecond);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            HandleSafetyShortcuts(keyboard);

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                _simulationEnabled = !_simulationEnabled;
                _hasLastSnapshot = false;
                if (!_simulationEnabled && !_bleOwnsControl) InjectFailSafeState();
            }

            if (keyboard.xKey.wasPressedThisFrame)
            {
                _vehicleEnabled = !_vehicleEnabled;
                _hasLastSnapshot = false;
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                _emergencyStop = !_emergencyStop;
                _hasLastSnapshot = false;
            }

            if (!IsSimulationActive) return;
            UpdateSteering(keyboard);
            UpdateTiller(keyboard);

            KeyboardStateSnapshot snapshot = ReadSnapshot(keyboard);
            bool stillOwnsLatestState = LastInjectedSequence.HasValue &&
                                        _stateReceiver != null &&
                                        _stateReceiver.LastAppliedSequence == LastInjectedSequence;
            if (_hasLastSnapshot && snapshot.Equals(_lastSnapshot) && stillOwnsLatestState) return;

            InjectSnapshot(snapshot);
            _lastSnapshot = snapshot;
            _hasLastSnapshot = true;
        }

        private void HandleSafetyShortcuts(Keyboard keyboard)
        {
            if (keyboard.cKey.wasPressedThisFrame) _collisionReporter?.ReportCollision();
            if (keyboard.vKey.wasPressedThisFrame) _controlDriver?.ClearCollisionInterlock();
        }

        private KeyboardStateSnapshot ReadSnapshot(Keyboard keyboard)
        {
            int travelRaw = ResolveAxis(keyboard.wKey.isPressed, keyboard.sKey.isPressed, 255, 0, NeutralTravel);
            int steering = Mathf.RoundToInt(_simulatedSteeringDegrees);

            PalletStackerLiftState lift = PalletStackerLiftState.Neutral;
            bool liftUp = keyboard.rKey.isPressed;
            bool liftDown = keyboard.fKey.isPressed;
            if (liftUp != liftDown) lift = liftUp ? PalletStackerLiftState.Up : PalletStackerLiftState.Down;

            bool slowMode = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
            return new KeyboardStateSnapshot(
                _vehicleEnabled,
                keyboard.spaceKey.isPressed,
                _emergencyStop,
                keyboard.hKey.isPressed,
                slowMode,
                steering,
                _tillerDegrees,
                travelRaw,
                lift);
        }

        private void UpdateSteering(Keyboard keyboard)
        {
            bool steerRight = keyboard.dKey.isPressed;
            bool steerLeft = keyboard.aKey.isPressed;
            float target = 0f;
            if (steerRight != steerLeft) target = steerRight ? _maximumSteeringDegrees : -_maximumSteeringDegrees;

            float response = Mathf.Approximately(target, 0f)
                ? _steeringReturnSpeedDegreesPerSecond
                : _steeringSpeedDegreesPerSecond;
            _simulatedSteeringDegrees = Mathf.MoveTowards(
                _simulatedSteeringDegrees,
                target,
                response * Time.deltaTime);
        }

        private void UpdateTiller(Keyboard keyboard)
        {
            bool raise = keyboard.upArrowKey.isPressed;
            bool lower = keyboard.downArrowKey.isPressed;
            if (raise == lower) return;

            _simulatedTillerDegrees = Mathf.MoveTowards(
                _simulatedTillerDegrees,
                raise ? 0f : 100f,
                _tillerSpeedDegreesPerSecond * Time.deltaTime);
            _tillerDegrees = Mathf.RoundToInt(_simulatedTillerDegrees);
        }

        private void InjectSnapshot(KeyboardStateSnapshot snapshot)
        {
            if (_stateReceiver == null)
            {
                Debug.LogWarning("[KEYBOARD SIM] CONTROL_STATE receiver is missing.", this);
                return;
            }

            byte flags = 0;
            if (snapshot.Enabled) flags |= EnabledFlag;
            if (snapshot.Stop) flags |= StopFlag;
            if (snapshot.EmergencyStop) flags |= EmergencyStopFlag;
            if (snapshot.Horn) flags |= HornFlag;
            if (snapshot.SlowMode) flags |= SlowModeFlag;

            ushort sequence = NextSequence();
            byte[] packet =
            {
                PalletStackerBleProtocol.Version,
                (byte)(sequence & 0xFF),
                (byte)(sequence >> 8),
                flags,
                unchecked((byte)(sbyte)snapshot.SteeringDegrees),
                (byte)snapshot.TillerDegrees,
                (byte)snapshot.TravelRaw,
                (byte)snapshot.Lift
            };

            if (!_stateReceiver.TryApplyPayload(
                    packet,
                    out ushort appliedSequence,
                    out _,
                    out string error))
            {
                Debug.LogWarning($"[KEYBOARD SIM] CONTROL_STATE rejected: {error}.", this);
                return;
            }

            LastInjectedSequence = appliedSequence;
        }

        private void InjectFailSafeState()
        {
            InjectSnapshot(new KeyboardStateSnapshot(
                enabled: false,
                stop: true,
                emergencyStop: false,
                horn: false,
                slowMode: false,
                steeringDegrees: 0,
                tillerDegrees: _tillerDegrees,
                travelRaw: NeutralTravel,
                lift: PalletStackerLiftState.Neutral));
        }

        private ushort NextSequence()
        {
            ushort current = _stateReceiver?.LastAppliedSequence ?? LastInjectedSequence ?? ushort.MaxValue;
            return unchecked((ushort)(current + 1));
        }

        private void HandleConnectionStateChanged(BleConnectionState state)
        {
            if (!_yieldToBleConnection) return;

            if (state == BleConnectionState.Connected)
            {
                if (!_bleOwnsControl) InjectFailSafeState();
                _bleOwnsControl = true;
                _hasLastSnapshot = false;
                return;
            }

            _bleOwnsControl = false;
            _hasLastSnapshot = false;
        }

        private void ResolveDependencies()
        {
            if (_stateReceiver == null) _stateReceiver = GetComponent<PalletStackerControlStateReceiver>();
            if (_bleManager == null) _bleManager = GetComponent<BleManager>();
            if (_controlDriver == null) _controlDriver = FindFirstObjectByType<PalletStackerControlDriver>();
            if (_collisionReporter == null) _collisionReporter = FindFirstObjectByType<PalletStackerCollisionReporter>();
        }

        private static int ResolveAxis(bool positive, bool negative, int positiveValue, int negativeValue, int neutralValue)
        {
            if (positive == negative) return neutralValue;
            return positive ? positiveValue : negativeValue;
        }

        private readonly struct KeyboardStateSnapshot : IEquatable<KeyboardStateSnapshot>
        {
            public KeyboardStateSnapshot(
                bool enabled,
                bool stop,
                bool emergencyStop,
                bool horn,
                bool slowMode,
                int steeringDegrees,
                int tillerDegrees,
                int travelRaw,
                PalletStackerLiftState lift)
            {
                Enabled = enabled;
                Stop = stop;
                EmergencyStop = emergencyStop;
                Horn = horn;
                SlowMode = slowMode;
                SteeringDegrees = steeringDegrees;
                TillerDegrees = tillerDegrees;
                TravelRaw = travelRaw;
                Lift = lift;
            }

            public bool Enabled { get; }
            public bool Stop { get; }
            public bool EmergencyStop { get; }
            public bool Horn { get; }
            public bool SlowMode { get; }
            public int SteeringDegrees { get; }
            public int TillerDegrees { get; }
            public int TravelRaw { get; }
            public PalletStackerLiftState Lift { get; }

            public bool Equals(KeyboardStateSnapshot other)
            {
                return Enabled == other.Enabled &&
                       Stop == other.Stop &&
                       EmergencyStop == other.EmergencyStop &&
                       Horn == other.Horn &&
                       SlowMode == other.SlowMode &&
                       SteeringDegrees == other.SteeringDegrees &&
                       TillerDegrees == other.TillerDegrees &&
                       TravelRaw == other.TravelRaw &&
                       Lift == other.Lift;
            }

            public override bool Equals(object obj)
            {
                return obj is KeyboardStateSnapshot other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Enabled.GetHashCode();
                    hash = (hash * 397) ^ Stop.GetHashCode();
                    hash = (hash * 397) ^ EmergencyStop.GetHashCode();
                    hash = (hash * 397) ^ Horn.GetHashCode();
                    hash = (hash * 397) ^ SlowMode.GetHashCode();
                    hash = (hash * 397) ^ SteeringDegrees;
                    hash = (hash * 397) ^ TillerDegrees;
                    hash = (hash * 397) ^ TravelRaw;
                    hash = (hash * 397) ^ (int)Lift;
                    return hash;
                }
            }
        }
    }
}
