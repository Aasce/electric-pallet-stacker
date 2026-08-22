using System;
using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    [Flags]
    public enum PalletStackerControlFields
    {
        None = 0,
        Enabled = 1 << 0,
        Stop = 1 << 1,
        EmergencyStop = 1 << 2,
        Horn = 1 << 3,
        SlowMode = 1 << 4,
        SteerDeg = 1 << 5,
        TillerDeg = 1 << 6,
        TravelRaw = 1 << 7,
        LiftState = 1 << 8
    }

    [Serializable]
    public sealed class PalletStackerControlState
    {
        [SerializeField] private bool enabled;
        [SerializeField] private bool stop = true;
        [SerializeField] private bool emergencyStop;
        [SerializeField] private bool horn;
        [SerializeField] private bool slowMode;
        [SerializeField] private int steerDeg;
        [SerializeField] private int tillerDeg = 55;
        [SerializeField] private int travelRaw = 127;
        [SerializeField] private int liftState;

        public bool Enabled => enabled;
        public bool Stop => stop;
        public bool EmergencyStop => emergencyStop;
        public bool Horn => horn;
        public bool SlowMode => slowMode;
        public int SteerDeg => steerDeg;
        public int TillerDeg => tillerDeg;
        public int TravelRaw => travelRaw;
        public int LiftState => liftState;

        internal PalletStackerControlState Clone()
        {
            return new PalletStackerControlState
            {
                enabled = enabled,
                stop = stop,
                emergencyStop = emergencyStop,
                horn = horn,
                slowMode = slowMode,
                steerDeg = steerDeg,
                tillerDeg = tillerDeg,
                travelRaw = travelRaw,
                liftState = liftState
            };
        }

        internal PalletStackerControlFields GetChangedFields(PalletStackerControlState other)
        {
            PalletStackerControlFields changedFields = PalletStackerControlFields.None;
            if (enabled != other.enabled) changedFields |= PalletStackerControlFields.Enabled;
            if (stop != other.stop) changedFields |= PalletStackerControlFields.Stop;
            if (emergencyStop != other.emergencyStop) changedFields |= PalletStackerControlFields.EmergencyStop;
            if (horn != other.horn) changedFields |= PalletStackerControlFields.Horn;
            if (slowMode != other.slowMode) changedFields |= PalletStackerControlFields.SlowMode;
            if (steerDeg != other.steerDeg) changedFields |= PalletStackerControlFields.SteerDeg;
            if (tillerDeg != other.tillerDeg) changedFields |= PalletStackerControlFields.TillerDeg;
            if (travelRaw != other.travelRaw) changedFields |= PalletStackerControlFields.TravelRaw;
            if (liftState != other.liftState) changedFields |= PalletStackerControlFields.LiftState;
            return changedFields;
        }

        public override string ToString()
        {
            return JsonUtility.ToJson(this);
        }
    }
}
