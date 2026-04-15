using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Extensions;
using System;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    internal class Packet : IPacket
    {
        private const bool VerboseLogging = false;
        private const int HeaderSize = 3; // DeviceId + MsgType + TargetId
        private const int CrcSize = 1;
        private static readonly byte[] EmptyData = new byte[0];

        public byte DeviceId { get; private set; }

        public byte PacketType { get; private set; }

        public byte TargetId { get; private set; }

        public byte[] Data { get; private set; }

        public byte Crc { get; private set; } = 0x00;

        public bool IsBroadcast => TargetId == 0x00;

        public bool IsValid()
        {
            if (VerboseLogging) Log("IsValid begin");
            bool valid = ByteArrayExtensions.ComputePacketCrc(DeviceId, PacketType, TargetId, Data) == Crc;
            if (VerboseLogging) Log("IsValid end");
            return valid;
        }

        public byte[] ToBytes()
        {
            if (VerboseLogging) Log("ToBytes begin");
            byte[] result = new byte[HeaderSize + Data.Length + CrcSize];
            result[0] = DeviceId;
            result[1] = PacketType;
            result[2] = TargetId;
            Array.Copy(Data, 0, result, HeaderSize, Data.Length);
            result[result.Length - 1] = Crc;
            if (VerboseLogging)
            {
                Log("ToBytes end");
            }
            return result;
        }

        /// <summary>
        /// Constructor for creating a new packet to send. CRC will be calculated automatically.
        /// </summary>
        /// <param name="deviceId">The ID of the device sending the packet.</param>
        /// <param name="packetType">Packet Type <see cref="PacketType.cs"/></param>
        /// <param name="targetId">The ID of the target device.</param>
        /// <param name="data">The data payload of the packet.</param>
        /// <param name="crc">The CRC of the packet.</param>
        public Packet(byte deviceId, byte packetType, byte targetId, byte[] data = null, byte crc = 0)
        {
            if (VerboseLogging) Log("ctor begin");
            DeviceId = deviceId;
            PacketType = packetType;
            TargetId = targetId;
            Data = data ?? EmptyData;
            Crc = crc;

            if (Crc == 0)
            {
                Crc = ByteArrayExtensions.ComputePacketCrc(DeviceId, PacketType, TargetId, Data);
                if (VerboseLogging) Log("ctor computed crc");
            }
            if (VerboseLogging) Log("ctor end");
        }

        private static void Log(string message)
        {
            if (VerboseLogging)
                DebugLog.Write("[Packet] " + message);
        }
    }
}
