namespace IOD.CaptureTheFlag.NanoFramework.Types
{
    // ---------------------------------------------------------------
    // Packet lengths
    //
    // [ DeviceID | MsgType | TargetID | Data[0..n] | CRC ]
    //   1 byte     1 byte    1 byte                  1 byte
    //
    // MinHeaderLength = DeviceID + MsgType + TargetID + CRC = 4
    // ---------------------------------------------------------------

    public static class PacketLength
    {
        public const int Header       = 3;   // DeviceID + MsgType + TargetID
        public const int Overhead     = 4;   // Header + CRC
        public const int Key          = 4;   // key payload length

        // Total expected lengths per MsgType
        public const int Heartbeat    = 5;   // Overhead + hasKey
        public const int Attack       = 4;   // Overhead only
        public const int AttackAck    = 5;   // Overhead + combatNumber
        public const int FlagTransfer = 8;   // Overhead + key(4)
        public const int Capture      = 4;   // Overhead only
        public const int KeyGrant     = 8;   // Overhead + key(4)
        public const int Deliver      = 8;   // Overhead + key(4)
        public const int RespawnReq   = 4;   // Overhead only
        public const int RespawnAck   = 5;   // Overhead + newCombatNumber
        public const int GameStart    = 4;   // Overhead only
        public const int GameEnd      = 5;   // Overhead + winnerId
        public const int CombatResult = 5;   // Overhead + winnerId
    }
}