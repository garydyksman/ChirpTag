namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IPacketParser
    {
        /// <summary>
        /// Returns expected total packet length for a given MsgType.
        /// Returns -1 for unknown types.
        /// </summary>
        int ExpectedLength(byte msgType);

        /// <summary>Parse raw bytes into a packet. Throws on failure.</summary>
        IPacket Parse(byte[] raw);

        /// <summary>Safe parse — returns false and null on any failure.</summary>
        bool TryParse(byte[] raw, out IPacket packet);
    }
}
