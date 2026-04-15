namespace IOD.CaptureTheFlag.NanoFramework.Types
{
    public static class PacketType
    {
        public const byte Heartbeat    = 0x01;  // Data: [ alive ]
        public const byte Attack       = 0x02;  // Data: [ ]
        public const byte AttackAck    = 0x03;  // Data: [ combatNumber ]
        public const byte FlagTransfer = 0x04;  // Data: [ k0, k1, k2, k3 ]
        public const byte Capture      = 0x05;  // Data: [ ]
        public const byte KeyGrant     = 0x06;  // Data: [ k0, k1, k2, k3 ]
        public const byte Deliver      = 0x07;  // Data: [ k0, k1, k2, k3 ]
        public const byte RespawnReq   = 0x08;  // Data: [ ]
        public const byte RespawnAck   = 0x09;  // Data: [ newCombatNumber ]
        public const byte GameStart    = 0x0A;  // Data: [ ]
        public const byte GameEnd      = 0x0B;  // Data: [ winnerId ]
        public const byte CombatResult = 0x0C;  // Data: [ winnerId ]
    }
}