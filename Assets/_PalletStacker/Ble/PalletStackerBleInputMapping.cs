using UnityEngine;

namespace ElectricPalletStackers.Ble
{
    [CreateAssetMenu(
        fileName = "Pallet Stacker BLE Input Mapping",
        menuName = "Electric Pallet Stacker/BLE Input Mapping")]
    public sealed class PalletStackerBleInputMapping : ScriptableObject
    {
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

        public Vector2 RawSteeringDeg => _rawSteeringDeg;
        public Vector2 MappedSteeringDeg => _mappedSteeringDeg;
        public Vector2 ClampedSteeringDeg => _clampedSteeringDeg;
        public Vector2 RawTillerDeg => _rawTillerDeg;
        public Vector2 MappedTillerDeg => _mappedTillerDeg;
        public Vector2 ClampedTillerDeg => _clampedTillerDeg;

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
    }
}
