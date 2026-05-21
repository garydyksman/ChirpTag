namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IPacketBuilder
    {
        IPacket Heartbeat(byte deviceId, byte deviceType);
        IPacket Attack(byte deviceId, byte targetId);
        IPacket AttackAck(byte deviceId, byte targetId, byte combatScore);
        IPacket CombatResult(byte deviceId, byte targetId, byte winnerId);
        IPacket FlagTransfer(byte deviceId, byte targetId, byte[] key);
        IPacket Capture(byte deviceId, byte flagNodeId);
        IPacket KeyGrant(byte flagNodeId, byte targetId, byte[] key);
        IPacket Deliver(byte deviceId, byte flagNodeId, byte[] key);
        IPacket RespawnReq(byte deviceId, byte flagNodeId);
        IPacket RespawnAck(byte flagNodeId, byte targetId, byte newCombatNumber);
        IPacket GameStart(byte gmNodeId);
        IPacket GameEnd(byte gmNodeId, byte winnerId);
    }

}
