using ElectricPalletStackers.Ble;

namespace ElectricPalletStackers.PalletStackers
{
    /// <summary>
    /// Immutable gameplay command produced from a transport state.
    /// Outputs consume this type and do not depend on BLE directly.
    /// </summary>
    public readonly struct PalletStackerDriveCommand
    {
        public PalletStackerDriveCommand(
            ushort sequence,
            float travelNormalized,
            float steeringDegrees,
            float tillerDegrees,
            PalletStackerLiftState lift,
            bool horn,
            bool slowMode,
            bool movementInhibited,
            bool emergencyStop,
            bool localInterlock)
            : this(
                sequence,
                travelNormalized,
                steeringDegrees,
                tillerDegrees,
                lift,
                horn,
                slowMode,
                movementInhibited,
                emergencyStop,
                localInterlock,
                travelNormalized,
                lift)
        {
        }

        private PalletStackerDriveCommand(
            ushort sequence,
            float travelNormalized,
            float steeringDegrees,
            float tillerDegrees,
            PalletStackerLiftState lift,
            bool horn,
            bool slowMode,
            bool movementInhibited,
            bool emergencyStop,
            bool localInterlock,
            float travelInputNormalized,
            PalletStackerLiftState liftInput)
        {
            Sequence = sequence;
            TravelNormalized = travelNormalized;
            SteeringDegrees = steeringDegrees;
            TillerDegrees = tillerDegrees;
            Lift = lift;
            TravelInputNormalized = travelInputNormalized;
            LiftInput = liftInput;
            Horn = horn;
            SlowMode = slowMode;
            MovementInhibited = movementInhibited;
            EmergencyStop = emergencyStop;
            LocalInterlock = localInterlock;
        }

        public ushort Sequence { get; }
        // Effective values consumed by the physical motor and lift outputs.
        public float TravelNormalized { get; }
        public float SteeringDegrees { get; }
        public float TillerDegrees { get; }
        public PalletStackerLiftState Lift { get; }
        // Requested values remain live for model visuals while actions are interlocked.
        public float TravelInputNormalized { get; }
        public PalletStackerLiftState LiftInput { get; }
        public bool Horn { get; }
        public bool SlowMode { get; }
        public bool MovementInhibited { get; }
        public bool EmergencyStop { get; }
        public bool LocalInterlock { get; }
        public bool RequiresImmediateStop => EmergencyStop || LocalInterlock;

        public static PalletStackerDriveCommand FromControlState(
            PalletStackerControlState state,
            ushort sequence,
            float slowModeTravelMultiplier,
            bool localInterlock,
            bool actionInterlock = false,
            float travelDeadzoneNormalized = PalletStackerVehicleSettings.DefaultTravelDeadzoneNormalized)
        {
            if (state == null) return CreateFailSafe(localInterlock || actionInterlock);

            bool movementInhibited = localInterlock || actionInterlock || !state.TravelAllowed;
            bool auxiliaryControlsEnabled = state.Enabled;
            float travel = movementInhibited
                ? 0f
                : ApplyTravelDeadzone(state.TravelNormalized, travelDeadzoneNormalized);
            if (state.SlowMode) travel *= slowModeTravelMultiplier;

            return new PalletStackerDriveCommand(
                sequence,
                travel,
                state.SteerDeg,
                state.TillerDeg,
                auxiliaryControlsEnabled && !actionInterlock
                    ? state.Lift
                    : PalletStackerLiftState.Neutral,
                auxiliaryControlsEnabled && state.Horn,
                state.SlowMode,
                movementInhibited,
                state.EmergencyStop,
                localInterlock || actionInterlock,
                state.TravelNormalized,
                state.Lift);
        }

        private static float ApplyTravelDeadzone(float value, float deadzone)
        {
            float clampedDeadzone = UnityEngine.Mathf.Clamp(deadzone, 0f, 0.5f);
            float magnitude = UnityEngine.Mathf.Abs(value);
            if (magnitude <= clampedDeadzone) return 0f;

            float remappedMagnitude = UnityEngine.Mathf.InverseLerp(clampedDeadzone, 1f, magnitude);
            return UnityEngine.Mathf.Sign(value) * remappedMagnitude;
        }

        public PalletStackerDriveCommand WithAnalogInputs(
            float travelNormalized,
            float travelInputNormalized,
            float steeringDegrees,
            float tillerDegrees)
        {
            return new PalletStackerDriveCommand(
                Sequence,
                travelNormalized,
                steeringDegrees,
                tillerDegrees,
                Lift,
                Horn,
                SlowMode,
                MovementInhibited,
                EmergencyStop,
                LocalInterlock,
                travelInputNormalized,
                LiftInput);
        }

        public static PalletStackerDriveCommand CreateFailSafe(bool localInterlock = false)
        {
            return new PalletStackerDriveCommand(
                0,
                0f,
                0f,
                0f,
                PalletStackerLiftState.Neutral,
                false,
                false,
                true,
                true,
                localInterlock);
        }
    }

    /// <summary>
    /// Composite output contract. Add another MonoBehaviour implementing this
    /// interface to extend gameplay without changing the control driver.
    /// </summary>
    public interface IPalletStackerControlOutput
    {
        void Apply(PalletStackerDriveCommand command);
        void StopImmediately();
    }
}
