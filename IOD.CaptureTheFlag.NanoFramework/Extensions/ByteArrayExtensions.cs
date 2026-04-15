using IOD.CaptureTheFlag.NanoFramework.Implementations;
using System;
using System.Diagnostics;

namespace IOD.CaptureTheFlag.NanoFramework.Extensions
{
    public static class ByteArrayExtensions
    {
        private const bool VerboseLogging = false;
        private const byte Poly = 0x07; // CRC-8/SMBUS

        public static byte ComputeCrc(this byte[] data)
        {
            if (data == null) return 0x00;
            return ComputeCrc(data, 0, data.Length);
        }

        public static byte ComputeCrc(this byte[] data, int offset, int count)
        {
            byte crc = 0x00;
            if (data == null) return crc;

            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                crc = UpdateCrc(crc, data[i]);
            }
            return crc;
        }

        public static byte ComputePacketCrc(byte deviceId, byte packetType, byte targetId, byte[] data)
        {
            byte crc = 0x00;
            crc = UpdateCrc(crc, deviceId);
            crc = UpdateCrc(crc, packetType);
            crc = UpdateCrc(crc, targetId);

            if (data != null)
            {
                for (int i = 0; i < data.Length; i++)
                    crc = UpdateCrc(crc, data[i]);
            }

            return crc;
        }

        public static bool Validate(this byte[] packet)
        {
            if (packet == null || packet.Length < 2) return false;

            byte crc = packet.ComputeCrc(0, packet.Length - 1);

            if (VerboseLogging)
                DebugLog.Write("Validating packet");

            return crc == packet[packet.Length - 1];
        }

        private static byte UpdateCrc(byte crc, byte value)
        {
            crc ^= value;
            for (int b = 0; b < 8; b++)
                crc = (byte)((crc & 0x80) != 0 ? crc << 1 ^ Poly : crc << 1);

            return crc;
        }

        public static string ToHexString(this byte[] bytes)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                sb.Append(bytes[i].ToString("X2"));
                if (i < bytes.Length - 1) sb.Append(" ");
            }
            return sb.ToString();
        }
    }
}
