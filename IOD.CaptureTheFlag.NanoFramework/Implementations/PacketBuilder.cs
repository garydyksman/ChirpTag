using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class PacketBuilder : IPacketBuilder
    {
        private static readonly byte[] EmptyData = new byte[0];

        public IPacket Heartbeat(byte deviceId)
            => new Packet(deviceId, PacketType.Heartbeat, 0x00, EmptyData);

        public IPacket Attack(byte deviceId, byte targetId)
            => new Packet(deviceId, PacketType.Attack, targetId, EmptyData);

        public IPacket AttackAck(byte deviceId, byte targetId, byte combatScore)
            => new Packet(deviceId, PacketType.AttackAck, targetId, new byte[] { combatScore });

        public IPacket CombatResult(byte deviceId, byte targetId, byte winnerId)
            => new Packet(deviceId, PacketType.CombatResult, targetId, new byte[] { winnerId });

        public IPacket FlagTransfer(byte deviceId, byte targetId, byte[] key)
            => new Packet(deviceId, PacketType.FlagTransfer, targetId, key);

        public IPacket Capture(byte deviceId, byte flagNodeId)
            => new Packet(deviceId, PacketType.Capture, flagNodeId, EmptyData);

        public IPacket KeyGrant(byte flagNodeId, byte targetId, byte[] key)
            => new Packet(flagNodeId, PacketType.KeyGrant, targetId, key);

        public IPacket Deliver(byte deviceId, byte flagNodeId, byte[] key)
            => new Packet(deviceId, PacketType.Deliver, flagNodeId, key);

        public IPacket RespawnReq(byte deviceId, byte flagNodeId)
            => new Packet(deviceId, PacketType.RespawnReq, flagNodeId, EmptyData);

        public IPacket RespawnAck(byte flagNodeId, byte targetId, byte newCombatScore)
            => new Packet(flagNodeId, PacketType.RespawnAck, targetId, new byte[] { newCombatScore });

        public IPacket GameStart(byte gmNodeId)
            => new Packet(gmNodeId, PacketType.GameStart, 0x00, EmptyData);

        public IPacket GameEnd(byte gmNodeId, byte winnerId)
            => new Packet(gmNodeId, PacketType.GameEnd, 0x00, new byte[] { winnerId });
    }
}
