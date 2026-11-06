using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Spaxtek.Ble.Core;
using Spaxtek.Ble.Unity;

namespace ElectricPalletStackers.Ble
{
    public static class PalletStackerBleProtocol
    {
        public const byte Version = 1;
        public const int ControlStatePacketLength = 8;
        public const int SequencePacketLength = 3;
        public const int RecenterPacketLength = 2;

        public const byte RecenterCommand = 0x52; // ASCII 'R'
        public const byte RecenterAccepted = 0x41; // ASCII 'A'
        public const byte RecenterRejected = 0x4E; // ASCII 'N'

        public const string DeviceName = "FORKLIFT_CTRL_ESP32";
        public const string ServiceUuidText = "7b7e0001-7c6f-4f8b-9a5b-2f7c2a4d1000";
        public const string ControlStateUuidText = "7b7e0002-7c6f-4f8b-9a5b-2f7c2a4d1000";
        public const string CollisionEventUuidText = "7b7e0003-7c6f-4f8b-9a5b-2f7c2a4d1000";
        public const string AckUuidText = "7b7e0004-7c6f-4f8b-9a5b-2f7c2a4d1000";
        public const string RecenterRequestUuidText = "7b7e0005-7c6f-4f8b-9a5b-2f7c2a4d1000";
        public const string RecenterResponseUuidText = "7b7e0006-7c6f-4f8b-9a5b-2f7c2a4d1000";

        public static readonly Guid ServiceUuid = Guid.Parse(ServiceUuidText);
        public static readonly Guid ControlStateUuid = Guid.Parse(ControlStateUuidText);
        public static readonly Guid CollisionEventUuid = Guid.Parse(CollisionEventUuidText);
        public static readonly Guid AckUuid = Guid.Parse(AckUuidText);
        public static readonly Guid RecenterRequestUuid = Guid.Parse(RecenterRequestUuidText);
        public static readonly Guid RecenterResponseUuid = Guid.Parse(RecenterResponseUuidText);

        public static byte[] EncodeRecenterRequest(byte requestId)
        {
            return new[] { RecenterCommand, requestId };
        }

        public static bool TryDecodeRecenterResponse(
            ReadOnlySpan<byte> packet,
            byte expectedRequestId,
            out bool accepted,
            out string error)
        {
            accepted = false;
            if (packet.Length != RecenterPacketLength)
            {
                error = $"RECENTER_RSP must be exactly {RecenterPacketLength} bytes; received {packet.Length}.";
                return false;
            }

            if (packet[1] != expectedRequestId)
            {
                error = $"RECENTER_RSP request_id {packet[1]} does not match pending request_id {expectedRequestId}.";
                return false;
            }

            if (packet[0] == RecenterAccepted)
            {
                accepted = true;
                error = string.Empty;
                return true;
            }

            if (packet[0] == RecenterRejected)
            {
                error = string.Empty;
                return true;
            }

            error = $"RECENTER_RSP has unsupported status 0x{packet[0]:X2}.";
            return false;
        }

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

    /// <summary>
    /// Serializes GATT writes per BleManager. Android and Windows BLE stacks are both
    /// sensitive to overlapping write-with-response operations.
    /// </summary>
    internal static class PalletStackerBleWriteGate
    {
        private static readonly ConditionalWeakTable<BleManager, SemaphoreSlim> Gates = new();

        public static async UniTask<BleWriteResult> WriteAsync(
            BleManager bleManager,
            BleCharacteristicId characteristicId,
            ReadOnlyMemory<byte> data,
            BleWriteMode writeMode,
            CancellationToken cancellationToken)
        {
            if (bleManager == null) throw new ArgumentNullException(nameof(bleManager));

            SemaphoreSlim gate = Gates.GetValue(bleManager, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken).AsUniTask();
            try
            {
                return await bleManager.WriteAsync(
                    characteristicId,
                    data,
                    writeMode,
                    cancellationToken).AsUniTask();
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
