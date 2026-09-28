using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    [CreateAssetMenu(
        fileName = "Pallet Stacker BLE Input Mapping",
        menuName = "Electric Pallet Stacker/BLE Input Mapping")]
    public sealed class PalletStackerBleInputMapping : ScriptableObject
    {
        [Header("Flags")]
        [Tooltip("Bit mask representing the enabled state in the CONTROL_STATE flags byte.")]
        [SerializeField, Range(0, byte.MaxValue)] private int _enabledFlagMask = 1 << 0;
        [Tooltip("Bit mask representing the stop state in the CONTROL_STATE flags byte.")]
        [SerializeField, Range(0, byte.MaxValue)] private int _stopFlagMask = 1 << 1;
        [Tooltip("Bit mask representing the emergency-stop state in the CONTROL_STATE flags byte.")]
        [SerializeField, Range(0, byte.MaxValue)] private int _emergencyStopFlagMask = 1 << 2;
        [Tooltip("Bit mask representing the horn state in the CONTROL_STATE flags byte.")]
        [SerializeField, Range(0, byte.MaxValue)] private int _hornFlagMask = 1 << 3;
        [Tooltip("Bit mask representing the slow-mode state in the CONTROL_STATE flags byte.")]
        [SerializeField, Range(0, byte.MaxValue)] private int _slowModeFlagMask = 1 << 4;

        [Header("Steering (degrees)")]
        [Tooltip("Range received from the BLE packet.")]
        [SerializeField] private Vector2 _rawSteeringDeg = new Vector2(-90f, 90f);
        [Tooltip("Raw range is linearly remapped to this range when the two ranges differ.")]
        [SerializeField] private Vector2 _mappedSteeringDeg = new Vector2(-90f, 90f);
        [Tooltip("Final range exposed to gameplay after remapping.")]
        [SerializeField] private Vector2 _clampedSteeringDeg = new Vector2(-30f, 30f);

        [Header("Tiller (degrees)")]
        [Tooltip("Signed int8 range received from the BLE packet.")]
        [SerializeField] private Vector2 _rawTillerDeg = new Vector2(-128f, 127f);
        [Tooltip("Raw range is linearly remapped to the legacy gameplay range.")]
        [SerializeField] private Vector2 _mappedTillerDeg = new Vector2(0f, 100f);
        [Tooltip("Final range exposed to gameplay after remapping.")]
        [SerializeField] private Vector2 _clampedTillerDeg = new Vector2(0f, 100f);

        [Header("Tiller stop (mapped degrees)")]
        [Tooltip("The tiller engages the travel stop at or below this mapped angle.")]
        [SerializeField] private float _lowerTillerStopMaximumDeg = 10f;
        [Tooltip("The tiller engages the travel stop at or above this mapped angle.")]
        [SerializeField] private float _upperTillerStopMinimumDeg = 90f;

        [Header("Travel (normalized)")]
        [Tooltip("Raw endpoints mapped to the minimum and maximum travel values. Values outside this range are clamped by the final output range.")]
        [SerializeField] private Vector2 _rawTravel = new Vector2(11f, 245f);
        [Tooltip("Inclusive raw range treated as neutral travel.")]
        [SerializeField] private Vector2 _neutralTravelRaw = new Vector2(117f, 137f);
        [Tooltip("Raw travel endpoints are linearly remapped to this range, with zero at the neutral range.")]
        [SerializeField] private Vector2 _mappedTravel = new Vector2(-1f, 1f);
        [Tooltip("Final normalized range exposed to gameplay after remapping.")]
        [SerializeField] private Vector2 _clampedTravel = new Vector2(-1f, 1f);

        [Header("Lift states")]
        [Tooltip("Raw byte representing the neutral lift state.")]
        [SerializeField] private int _liftNeutralValue;
        [Tooltip("Raw byte representing the lift-up state.")]
        [SerializeField] private int _liftUpValue = 1;
        [Tooltip("Raw byte representing the lift-down state.")]
        [SerializeField] private int _liftDownValue = 2;

        public byte EnabledFlagMask => ToByte(_enabledFlagMask);
        public byte StopFlagMask => ToByte(_stopFlagMask);
        public byte EmergencyStopFlagMask => ToByte(_emergencyStopFlagMask);
        public byte HornFlagMask => ToByte(_hornFlagMask);
        public byte SlowModeFlagMask => ToByte(_slowModeFlagMask);
        public Vector2 RawSteeringDeg => _rawSteeringDeg;
        public Vector2 MappedSteeringDeg => _mappedSteeringDeg;
        public Vector2 ClampedSteeringDeg => _clampedSteeringDeg;
        public Vector2 RawTillerDeg => _rawTillerDeg;
        public Vector2 MappedTillerDeg => _mappedTillerDeg;
        public Vector2 ClampedTillerDeg => _clampedTillerDeg;
        public float LowerTillerStopMaximumDeg => _lowerTillerStopMaximumDeg;
        public float UpperTillerStopMinimumDeg => _upperTillerStopMinimumDeg;
        public Vector2 RawTravel => _rawTravel;
        public Vector2 NeutralTravelRaw => _neutralTravelRaw;
        public Vector2 MappedTravel => _mappedTravel;
        public Vector2 ClampedTravel => _clampedTravel;
        public byte LiftNeutralValue => ToByte(_liftNeutralValue);
        public byte LiftUpValue => ToByte(_liftUpValue);
        public byte LiftDownValue => ToByte(_liftDownValue);

        public bool TryDecodeFlags(
            byte flags,
            out bool enabled,
            out bool stop,
            out bool emergencyStop,
            out bool horn,
            out bool slowMode,
            out string error)
        {
            enabled = false;
            stop = false;
            emergencyStop = false;
            horn = false;
            slowMode = false;

            byte knownMask = 0;
            if (!TryAddFlagMask(EnabledFlagMask, ref knownMask, out error) ||
                !TryAddFlagMask(StopFlagMask, ref knownMask, out error) ||
                !TryAddFlagMask(EmergencyStopFlagMask, ref knownMask, out error) ||
                !TryAddFlagMask(HornFlagMask, ref knownMask, out error) ||
                !TryAddFlagMask(SlowModeFlagMask, ref knownMask, out error)) return false;

            if ((flags & ~knownMask) != 0)
            {
                error = $"CONTROL_STATE unconfigured flags must be zero; received 0x{flags:X2}.";
                return false;
            }

            enabled = (flags & EnabledFlagMask) != 0;
            stop = (flags & StopFlagMask) != 0;
            emergencyStop = (flags & EmergencyStopFlagMask) != 0;
            horn = (flags & HornFlagMask) != 0;
            slowMode = (flags & SlowModeFlagMask) != 0;
            error = string.Empty;
            return true;
        }

        public byte EncodeFlags(bool enabled, bool stop, bool emergencyStop, bool horn, bool slowMode)
        {
            byte flags = 0;
            if (enabled) flags |= EnabledFlagMask;
            if (stop) flags |= StopFlagMask;
            if (emergencyStop) flags |= EmergencyStopFlagMask;
            if (horn) flags |= HornFlagMask;
            if (slowMode) flags |= SlowModeFlagMask;
            return flags;
        }

        public bool TryMapSteeringDeg(int rawValue, out int value, out string error)
        {
            return TryMap(
                rawValue,
                _rawSteeringDeg,
                _mappedSteeringDeg,
                _clampedSteeringDeg,
                "steerDeg",
                out value,
                out error);
        }

        public bool TryMapTillerDeg(int rawValue, out int value, out string error)
        {
            return TryMap(
                rawValue,
                _rawTillerDeg,
                _mappedTillerDeg,
                _clampedTillerDeg,
                "tillerDeg",
                out value,
                out error);
        }

        public bool TryMapTravel(int rawValue, out float value, out string error)
        {
            value = 0f;
            if (rawValue < byte.MinValue || rawValue > byte.MaxValue)
            {
                error = $"travel must be between {byte.MinValue} and {byte.MaxValue}; received {rawValue}.";
                return false;
            }

            if (!TryValidateTravelMapping(out error)) return false;

            float mappedValue;
            if (rawValue < _neutralTravelRaw.x)
            {
                float normalized = (rawValue - _rawTravel.x) /
                                   (_neutralTravelRaw.x - _rawTravel.x);
                mappedValue = Mathf.LerpUnclamped(_mappedTravel.x, 0f, normalized);
            }
            else if (rawValue > _neutralTravelRaw.y)
            {
                float normalized = (rawValue - _neutralTravelRaw.y) /
                                   (_rawTravel.y - _neutralTravelRaw.y);
                mappedValue = Mathf.LerpUnclamped(0f, _mappedTravel.y, normalized);
            }
            else
            {
                mappedValue = 0f;
            }

            float clampMinimum = Mathf.Min(_clampedTravel.x, _clampedTravel.y);
            float clampMaximum = Mathf.Max(_clampedTravel.x, _clampedTravel.y);
            value = Mathf.Clamp(mappedValue, clampMinimum, clampMaximum);
            error = string.Empty;
            return true;
        }

        public byte EncodeTravel(float value)
        {
            if (!TryValidateTravelMapping(out _)) return 0;

            float rawValue;
            if (Mathf.Approximately(value, 0f))
            {
                rawValue = (_neutralTravelRaw.x + _neutralTravelRaw.y) * 0.5f;
            }
            else if (IsBetween(value, _mappedTravel.x, 0f))
            {
                float normalized = Mathf.InverseLerp(_mappedTravel.x, 0f, value);
                rawValue = Mathf.Lerp(_rawTravel.x, _neutralTravelRaw.x, normalized);
            }
            else if (IsBetween(value, 0f, _mappedTravel.y))
            {
                float normalized = Mathf.InverseLerp(0f, _mappedTravel.y, value);
                rawValue = Mathf.Lerp(_neutralTravelRaw.y, _rawTravel.y, normalized);
            }
            else
            {
                rawValue = Mathf.Abs(value - _mappedTravel.x) <= Mathf.Abs(value - _mappedTravel.y)
                    ? _rawTravel.x
                    : _rawTravel.y;
            }

            return (byte)Mathf.Clamp(Mathf.RoundToInt(rawValue), byte.MinValue, byte.MaxValue);
        }

        public bool TryMapLiftState(int rawValue, out PalletStackerLiftState value, out string error)
        {
            value = PalletStackerLiftState.Neutral;
            byte neutral = LiftNeutralValue;
            byte up = LiftUpValue;
            byte down = LiftDownValue;
            if (neutral == up || neutral == down || up == down)
            {
                error = "Lift neutral, up and down values must be unique.";
                return false;
            }

            if (rawValue == neutral) value = PalletStackerLiftState.Neutral;
            else if (rawValue == up) value = PalletStackerLiftState.Up;
            else if (rawValue == down) value = PalletStackerLiftState.Down;
            else
            {
                error = $"liftState must be one of the configured values ({neutral}, {up}, {down}); received {rawValue}.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public byte EncodeLiftState(PalletStackerLiftState value)
        {
            return value switch
            {
                PalletStackerLiftState.Up => LiftUpValue,
                PalletStackerLiftState.Down => LiftDownValue,
                _ => LiftNeutralValue
            };
        }

        public bool IsTillerStopped(float mappedTillerDeg)
        {
            float lower = Mathf.Min(_lowerTillerStopMaximumDeg, _upperTillerStopMinimumDeg);
            float upper = Mathf.Max(_lowerTillerStopMaximumDeg, _upperTillerStopMinimumDeg);
            return mappedTillerDeg <= lower || mappedTillerDeg >= upper;
        }

        public sbyte EncodeSteeringDeg(float value)
        {
            return EncodeSignedByte(value, _rawSteeringDeg, _mappedSteeringDeg);
        }

        public sbyte EncodeTillerDeg(float value)
        {
            return EncodeSignedByte(value, _rawTillerDeg, _mappedTillerDeg);
        }

        private static bool TryMap(
            float rawValue,
            Vector2 rawRange,
            Vector2 mappedRange,
            Vector2 clampedRange,
            string label,
            out int value,
            out string error)
        {
            value = 0;
            float rawMinimum = Mathf.Min(rawRange.x, rawRange.y);
            float rawMaximum = Mathf.Max(rawRange.x, rawRange.y);
            if (rawValue < rawMinimum || rawValue > rawMaximum)
            {
                error = $"{label} must be between {rawMinimum:0.###} and {rawMaximum:0.###}; received {rawValue:0.###}.";
                return false;
            }

            if (Mathf.Approximately(rawRange.x, rawRange.y))
            {
                error = $"{label} raw mapping range cannot have identical endpoints ({rawRange.x:0.###}).";
                return false;
            }

            float mappedValue = rawValue;
            if (!RangesMatch(rawRange, mappedRange))
            {
                float normalized = (rawValue - rawRange.x) / (rawRange.y - rawRange.x);
                mappedValue = Mathf.LerpUnclamped(mappedRange.x, mappedRange.y, normalized);
            }

            float clampMinimum = Mathf.Min(clampedRange.x, clampedRange.y);
            float clampMaximum = Mathf.Max(clampedRange.x, clampedRange.y);
            value = Mathf.RoundToInt(Mathf.Clamp(mappedValue, clampMinimum, clampMaximum));
            error = string.Empty;
            return true;
        }

        private static sbyte EncodeSignedByte(float value, Vector2 rawRange, Vector2 mappedRange)
        {
            float rawValue = value;
            if (!RangesMatch(rawRange, mappedRange) && !Mathf.Approximately(mappedRange.x, mappedRange.y))
            {
                float normalized = (value - mappedRange.x) / (mappedRange.y - mappedRange.x);
                rawValue = Mathf.LerpUnclamped(rawRange.x, rawRange.y, normalized);
            }

            int rounded = Mathf.RoundToInt(rawValue);
            return (sbyte)Mathf.Clamp(rounded, sbyte.MinValue, sbyte.MaxValue);
        }

        private static bool RangesMatch(Vector2 left, Vector2 right)
        {
            return Mathf.Approximately(left.x, right.x) && Mathf.Approximately(left.y, right.y);
        }

        private bool TryValidateTravelMapping(out string error)
        {
            if (_rawTravel.x >= _neutralTravelRaw.x ||
                _neutralTravelRaw.x > _neutralTravelRaw.y ||
                _neutralTravelRaw.y >= _rawTravel.y)
            {
                error = "Travel ranges must be ordered raw minimum < neutral minimum <= neutral maximum < raw maximum.";
                return false;
            }

            bool crossesZero = _mappedTravel.x < 0f && _mappedTravel.y > 0f;
            bool crossesZeroReversed = _mappedTravel.x > 0f && _mappedTravel.y < 0f;
            if (!crossesZero && !crossesZeroReversed)
            {
                error = "Mapped travel endpoints must be on opposite sides of zero.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static bool IsBetween(float value, float first, float second)
        {
            return value >= Mathf.Min(first, second) && value <= Mathf.Max(first, second);
        }

        private static byte ToByte(int value)
        {
            return (byte)Mathf.Clamp(value, byte.MinValue, byte.MaxValue);
        }

        private static bool TryAddFlagMask(byte mask, ref byte knownMask, out string error)
        {
            if (mask == 0 || (mask & (mask - 1)) != 0)
            {
                error = $"CONTROL_STATE flag masks must each contain exactly one bit; received 0x{mask:X2}.";
                return false;
            }

            if ((knownMask & mask) != 0)
            {
                error = $"CONTROL_STATE flag masks must be unique; 0x{mask:X2} is configured more than once.";
                return false;
            }

            knownMask |= mask;
            error = string.Empty;
            return true;
        }

        private void OnValidate()
        {
            _enabledFlagMask = Mathf.Clamp(_enabledFlagMask, byte.MinValue, byte.MaxValue);
            _stopFlagMask = Mathf.Clamp(_stopFlagMask, byte.MinValue, byte.MaxValue);
            _emergencyStopFlagMask = Mathf.Clamp(_emergencyStopFlagMask, byte.MinValue, byte.MaxValue);
            _hornFlagMask = Mathf.Clamp(_hornFlagMask, byte.MinValue, byte.MaxValue);
            _slowModeFlagMask = Mathf.Clamp(_slowModeFlagMask, byte.MinValue, byte.MaxValue);
            _liftNeutralValue = Mathf.Clamp(_liftNeutralValue, byte.MinValue, byte.MaxValue);
            _liftUpValue = Mathf.Clamp(_liftUpValue, byte.MinValue, byte.MaxValue);
            _liftDownValue = Mathf.Clamp(_liftDownValue, byte.MinValue, byte.MaxValue);
        }
    }
}
