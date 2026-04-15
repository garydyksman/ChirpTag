
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;
using System;
using IOD.CaptureTheFlag.NanoFramework.Extensions;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class PacketParser : IPacketParser
    {
        private const bool VerboseLogging = false;
        private const int MinLength = 4; // header(3) + crc(1)
        private const int HeaderSize = 3; // DeviceId + MsgType + TargetId
        private const int CrcSize = 1;
        private static readonly byte[] EmptyData = new byte[0];

        // ---------------------------------------------------------------
        // ExpectedLength — total wire bytes for a given MsgType
        // ---------------------------------------------------------------

        public int ExpectedLength(byte msgType)
        {
            if (VerboseLogging) Log("ExpectedLength");
            switch (msgType)
            {
                case PacketType.Heartbeat: return 4;  // header + crc
                case PacketType.Attack: return 4;
                case PacketType.Capture: return 4;
                case PacketType.RespawnReq: return 4;
                case PacketType.GameStart: return 4;
                case PacketType.AttackAck: return 5;  // + combatNumber
                case PacketType.RespawnAck: return 5;  // + newCombatNumber
                case PacketType.GameEnd: return 5;  // + winnerId
                case PacketType.FlagTransfer: return 8;  // + k0-k3
                case PacketType.KeyGrant: return 8;
                case PacketType.Deliver: return 8;
                case PacketType.CombatResult: return 5; // header(3) + winnerId(1) + crc(1)
                default: return -1; // unknown type
            }
        }

        // ---------------------------------------------------------------
        // TryParse — safe, no exceptions, use this in the hot path
        // Pipeline:
        //   1. Minimum length check
        //   2. CRC validation
        //   3. Expected length check
        //   4. Deserialise
        // Note: TargetId filter belongs in MessageHandler (needs device ID)
        // ---------------------------------------------------------------

        public bool TryParse(byte[] raw, out IPacket packet)
        {
            packet = null;
            if (VerboseLogging) Log("TryParse begin");

            // 1. Minimum length
            if (raw == null || raw.Length < MinLength)
            {
                if (VerboseLogging) Log("TryParse fail minimum length");
                return false;
            }

            // 2. CRC validation
            if (!raw.Validate())
            {
                if (VerboseLogging) Log("TryParse fail CRC");
                return false;
            }

            // 3. Expected length for this MsgType
            byte msgType = raw[1];
            int expected = ExpectedLength(msgType);
            if (expected == -1 || raw.Length != expected)
            {
                if (VerboseLogging) Log("TryParse fail expectedLength");
                return false;
            }

            // 4. Deserialise
            packet = Deserialise(raw);
            if (VerboseLogging) Log("TryParse success");
            return true;
        }

        // ---------------------------------------------------------------
        // Parse — throws on failure, use for trusted/internal paths
        // ---------------------------------------------------------------

        public IPacket Parse(byte[] raw)
        {
            if (VerboseLogging) Log("Parse begin");
            if (!TryParse(raw, out IPacket packet))
            {
                if (VerboseLogging) Log("Parse throwing Invalid packet");
                throw new Exception("Invalid packet");
            }
            if (VerboseLogging) Log("Parse success");
            return packet;
        }

        // ---------------------------------------------------------------
        // Deserialise — wire format: [DeviceId, MsgType, TargetId, ...Data, Crc]
        // ---------------------------------------------------------------

        private static IPacket Deserialise(byte[] raw)
        {
            if (VerboseLogging) Log("Deserialise begin");
            int dataLength = raw.Length - HeaderSize - CrcSize;
            byte[] data = dataLength == 0 ? EmptyData : new byte[dataLength];

            if (dataLength > 0)
                Array.Copy(raw, HeaderSize, data, 0, dataLength);

            IPacket packet = new Packet(
                deviceId: raw[0],
                packetType: raw[1],
                targetId: raw[2],
                data: data,
                crc: raw[raw.Length - 1]
            );
            if (VerboseLogging) Log("Deserialise end");
            return packet;
        }

        private static void Log(string message)
        {
            if (VerboseLogging)
                DebugLog.Write("[PacketParser] " + message);
        }
    }
}
