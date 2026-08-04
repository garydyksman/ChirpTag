using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using System;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class GameDevice : IGameDevice
    {
        public byte DeviceId { get; private set; }

        public byte DeviceType => Types.DeviceType.Player;

        public PlayerState State {get; private set; }

        public IMessageHandler Handler {get; private set; }

        public virtual void OnGameEnd(byte winnerId)
        {
            throw new NotImplementedException();
        }

        public virtual void OnGameStart()
        {
            throw new NotImplementedException();
        }

        public GameDevice(byte deviceId, IMessageHandler messageHandler)
        {
            State = PlayerState.Idle;
            Handler = messageHandler;

            DeviceId = deviceId; // default, should be set by caller
        }
    }
}
