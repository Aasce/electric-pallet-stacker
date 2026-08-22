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

        private const byte ReservedFlagMask = 0xE0;

        public static bool TryDecodeControlState(
            ReadOnlySpan<byte> packet,
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

            byte flags = packet[3];
            if ((flags & ReservedFlagMask) != 0)
            {
                error = $"CONTROL_STATE reserved flags must be zero; received 0x{flags:X2}.";
                return false;
            }

            int steerDeg = unchecked((sbyte)packet[4]);
            int tillerDeg = packet[5];
            int travelRaw = packet[6];
            int liftState = packet[7];

            if (steerDeg < -90 || steerDeg > 90)
            {
                error = $"steerDeg must be between -90 and 90; received {steerDeg}.";
                return false;
            }

            if (tillerDeg > 100)
            {
                error = $"tillerDeg must be between 0 and 100; received {tillerDeg}.";
                return false;
            }

            if (liftState > (int)PalletStackerLiftState.Down)
            {
                error = $"liftState must be 0, 1 or 2; received {liftState}.";
                return false;
            }

            sequence = DecodeUInt16LittleEndian(packet[1], packet[2]);
            state = PalletStackerControlState.FromProtocol(
                enabled: (flags & (1 << 0)) != 0,
                stop: (flags & (1 << 1)) != 0,
                emergencyStop: (flags & (1 << 2)) != 0,
                horn: (flags & (1 << 3)) != 0,
                slowMode: (flags & (1 << 4)) != 0,
                steerDeg,
                tillerDeg,
                travelRaw,
                (PalletStackerLiftState)liftState);

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

        public static float NormalizeTravelRaw(int travelRaw)
        {
            if (travelRaw <= 10) return -1f;
            if (travelRaw <= 116) return -(117f - travelRaw) / 106f;
            if (travelRaw <= 137) return 0f;
            if (travelRaw <= 244) return (travelRaw - 137f) / 108f;
            return 1f;
        }

        private static ushort DecodeUInt16LittleEndian(byte low, byte high)
        {
            return (ushort)(low | (high << 8));
        }
    }
}
