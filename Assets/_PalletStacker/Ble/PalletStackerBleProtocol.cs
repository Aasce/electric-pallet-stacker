using System;

namespace ElectricPalletStackers.Ble
{
    public static class PalletStackerBleProtocol
    {
        public const byte Version = 1;
        public const int ControlStatePacketLength = 8;
        public const int SequencePacketLength = 3;

        public const string DeviceName = "FORKLIFT_CTRL_ESP32";
        public const string ServiceUuidText = "7b7e0001-7c6f-4f8b-9a5b-2f7c2a4d1000";
        public const string ControlStateUuidText = "7b7e0002-7c6f-4f8b-9a5b-2f7c2a4d1000";
        public const string CollisionEventUuidText = "7b7e0003-7c6f-4f8b-9a5b-2f7c2a4d1000";
        public const string AckUuidText = "7b7e0004-7c6f-4f8b-9a5b-2f7c2a4d1000";

        public static readonly Guid ServiceUuid = Guid.Parse(ServiceUuidText);
        public static readonly Guid ControlStateUuid = Guid.Parse(ControlStateUuidText);
        public static readonly Guid CollisionEventUuid = Guid.Parse(CollisionEventUuidText);
        public static readonly Guid AckUuid = Guid.Parse(AckUuidText);

        public static bool TryDecodeControlState(
            ReadOnlySpan<byte> packet,
            PalletStackerBleInputMapping inputMapping,
            out ushort sequence,
            out PalletStackerControlState state,
            out string error)
        {
            sequence = 0;
            state = null;

            if (packet.Length != ControlStatePacketLength)
            {
                error = $"CONTROL_STATE must be exactly {ControlStatePacketLength} bytes; received {packet.Length}.";
                return false;
            }

            if (packet[0] != Version)
            {
                error = $"Unsupported CONTROL_STATE version {packet[0]}; expected {Version}.";
                return false;
            }

            if (inputMapping == null)
            {
                error = "CONTROL_STATE input mapping is not configured.";
                return false;
            }

            int rawSteerDeg = unchecked((sbyte)packet[4]);
            int rawTillerDeg = unchecked((sbyte)packet[5]);
            int travelRaw = packet[6];
            int liftState = packet[7];

            if (!inputMapping.TryDecodeFlags(
                    packet[3],
                    out bool enabled,
                    out bool stop,
                    out bool emergencyStop,
                    out bool horn,
                    out bool slowMode,
                    out error)) return false;
            if (!inputMapping.TryMapSteeringDeg(rawSteerDeg, out int steerDeg, out error)) return false;
            if (!inputMapping.TryMapTillerDeg(rawTillerDeg, out int tillerDeg, out error)) return false;
            if (!inputMapping.TryMapTravel(travelRaw, out float travelNormalized, out error)) return false;
            if (!inputMapping.TryMapLiftState(liftState, out PalletStackerLiftState lift, out error)) return false;

            sequence = DecodeUInt16LittleEndian(packet[1], packet[2]);
            state = PalletStackerControlState.FromProtocol(
                enabled,
                stop,
                emergencyStop,
                horn,
                slowMode,
                steerDeg,
                tillerDeg,
                travelRaw,
                travelNormalized,
                inputMapping.IsTillerStopped(tillerDeg),
                lift);

            error = string.Empty;
            return true;
        }

        public static bool TryDecodeSequencePacket(ReadOnlySpan<byte> packet, out ushort sequence, out string error)
        {
            sequence = 0;

            if (packet.Length != SequencePacketLength)
            {
                error = $"Sequence packet must be exactly {SequencePacketLength} bytes; received {packet.Length}.";
                return false;
            }

            if (packet[0] != Version)
            {
                error = $"Unsupported sequence packet version {packet[0]}; expected {Version}.";
                return false;
            }

            sequence = DecodeUInt16LittleEndian(packet[1], packet[2]);
            error = string.Empty;
            return true;
        }

        public static byte[] EncodeSequencePacket(ushort sequence)
        {
            return new[]
            {
                Version,
                (byte)(sequence & 0xFF),
                (byte)(sequence >> 8)
            };
        }

        private static ushort DecodeUInt16LittleEndian(byte low, byte high)
        {
            return (ushort)(low | (high << 8));
        }
    }
}
