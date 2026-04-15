namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IPacketParser
    {
        int ExpectedLength(byte msgType);
        IPacket Parse(byte[] raw);
        bool TryParse(byte[] raw, out IPacket packet);
    }
}
