namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    /// <summary>
    /// Base interface for all game devices (players and flag nodes).
    /// </summary>
    public interface IGameDevice
    {
        byte DeviceId { get; }
        byte DeviceType { get; }
        IMessageHandler Handler { get; }   // wired to the LoRa PacketReceived event

        void OnGameStart();
        void OnGameEnd(byte winnerId);
    }
}
