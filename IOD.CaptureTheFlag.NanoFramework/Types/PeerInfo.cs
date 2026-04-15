namespace IOD.CaptureTheFlag.NanoFramework.Types
{
    public class PeerInfo
    {
        public byte DeviceId { get; set; }
        public string PlayerName { get; set; }
        public int Rssi { get; set; }
        public float Snr { get; set; }
        public long LastSeenAt { get; set; }
    }
}
