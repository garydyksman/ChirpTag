namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IPacket
    {
        byte DeviceId { get; }
        byte MsgType { get; }
        byte TargetId { get; }
        byte[] Data { get; }
        byte Crc { get; }

        bool IsBroadcast { get; }   // TargetId == 0x00
        byte[] ToBytes();
        bool IsValid();             // CRC check
    }
}
