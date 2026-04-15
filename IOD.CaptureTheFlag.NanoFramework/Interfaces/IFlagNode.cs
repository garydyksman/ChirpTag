namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IFlagNode : IGameDevice
    {
        byte   FlagNodeId { get; }
        byte[] Key        { get; }
        bool   KeyTaken   { get; }

        // ---- LoRa RX ----
        void OnCaptureReceived       (byte fromDeviceId);
        void OnDeliverReceived       (byte fromDeviceId, byte[] key);
        void OnRespawnRequestReceived(byte fromDeviceId);

        // ---- LoRa TX ----
        void SendKeyGrant  (byte targetDeviceId);
        void SendRespawnAck(byte targetDeviceId, byte newCombatNumber);

        // ---- HTTP ----
        byte FetchRespawnNumber(byte deviceId);
        bool ReportDeliver     (byte deviceId, byte[] key);
    }
}