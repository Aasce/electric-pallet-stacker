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
        {
            Sequence = sequence;
            TravelNormalized = travelNormalized;
            SteeringDegrees = steeringDegrees;
            TillerDegrees = tillerDegrees;
            Lift = lift;
            Horn = horn;
            SlowMode = slowMode;
            MovementInhibited = movementInhibited;
            EmergencyStop = emergencyStop;
            LocalInterlock = localInterlock;
        }

        public ushort Sequence { get; }
        public float TravelNormalized { get; }
        public float SteeringDegrees { get; }
        public float TillerDegrees { get; }
        public PalletStackerLiftState Lift { get; }
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
            bool localInterlock)
        {
            if (state == null) return CreateFailSafe(localInterlock);

            bool movementInhibited = localInterlock ||
                                     !state.Enabled ||
                                     state.TillerStop ||
                                     state.EmergencyStop;
            float travel = movementInhibited ? 0f : state.TravelNormalized;
            if (state.SlowMode) travel *= slowModeTravelMultiplier;

            return new PalletStackerDriveCommand(
                sequence,
                travel,
                state.SteerDeg,
                state.TillerDeg,
                movementInhibited ? PalletStackerLiftState.Neutral : state.Lift,
                state.Horn,
                state.SlowMode,
                movementInhibited,
                state.EmergencyStop,
                localInterlock);
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
