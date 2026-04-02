namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface ICrcHelper
    {
        byte Compute(byte[] data, int length);
        bool Validate(byte[] packet);
    }
}
