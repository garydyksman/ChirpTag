using IOD.CaptureTheFlag.NanoFramework.Enum;

namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IGameDevice
    {
        byte DeviceId { get; }
        byte DeviceType { get; }
        GameState State { get; }
        IMessageHandler Handler { get; }   // wired to the LoRa PacketReceived event

        void OnGameStart();
        void OnGameEnd(byte winnerId);
    }
}
