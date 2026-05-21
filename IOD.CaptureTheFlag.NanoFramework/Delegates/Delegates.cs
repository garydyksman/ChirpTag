namespace IOD.CaptureTheFlag.NanoFramework.Delegates
{
    public delegate void HeartbeatReceivedDelegate(byte deviceId, byte deviceType, int rssi, float snr);
    public delegate void GameStartDelegate();
    public delegate void GameEndDelegate(byte winnerId);
    public delegate void AttackDelegate(byte fromDeviceId);
    public delegate void AttackAckDelegate(byte fromDeviceId, byte combatNumber);
    public delegate void CombatResultDelegate(byte fromDeviceId, byte winnerId);
    public delegate void FlagTransferDelegate(byte fromDeviceId, byte[] key);
    public delegate void CaptureDelegate(byte fromDeviceId);
    public delegate void KeyGrantDelegate(byte fromFlagNodeId, byte[] key);
    public delegate void DeliverDelegate(byte fromDeviceId, byte[] key);
    public delegate void RespawnReqDelegate(byte fromDeviceId);
    public delegate void RespawnAckDelegate(byte newCombatNumber);
}
