namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IGamerDevice : IGameDevice
    {
        byte CombatNumber { get; }
        bool HasKey { get; }
        byte[] CarriedKey { get; }   // null if not carrying

        // ---- LoRa TX ----
        void SendHeartbeat();
        void Attack(byte targetDeviceId);
        void AttackAck(byte targetDeviceId);
        void SendFlagTransfer(byte targetDeviceId, byte[] key);
        void SendCapture(byte flagNodeId);
        void SendDeliver(byte flagNodeId, byte[] key);
        void SendRespawnRequest(byte flagNodeId);

        // ---- LoRa RX ----
        void OnAttackReceived(byte fromDeviceId);
        void OnAttackAckReceived(byte fromDeviceId, byte theirCombatNumber);
        void OnFlagTransferReceived(byte fromDeviceId, byte[] key);
        void OnKeyGrantReceived(byte fromFlagNodeId, byte[] key);
        void OnRespawnAckReceived(byte newCombatNumber);

        // ---- Combat ----
        bool ResolveCombat(byte myCombatNumber, byte theirCombatNumber);

        // ---- State transitions ----
        void EnterStunned();
        void EnterCapturing(byte flagNodeId);
        void EnterDelivering(byte flagNodeId);
        void EnterActive();
    }
}
