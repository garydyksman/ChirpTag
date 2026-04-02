namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    // ---------------------------------------------------------------
    // Message handler
    //
    // Processing order (fail fast):
    //   1. Minimum length check         — drop if too short
    //   2. TargetID filter              — drop if not for us
    //   3. CRC validation               — drop if corrupt
    //   4. Expected length check        — drop if wrong size for MsgType
    //   5. Route by MsgType             — dispatch to correct handler
    //   6. Parse Data + handle
    // ---------------------------------------------------------------

    public interface IMessageHandler
    {
        /// <summary>
        /// Entry point — called by the LoRa poll thread for every received packet.
        /// Runs the full filter + route pipeline.
        /// </summary>
        void Handle(byte[] raw);

        // ---- Broadcast handlers ----
        void OnHeartbeat(IPacket packet);
        void OnGameStart(IPacket packet);
        void OnGameEnd(IPacket packet);

        // ---- Addressed handlers ----
        void OnAttack(IPacket packet);
        void OnAttackAck(IPacket packet);
        void OnFlagTransfer(IPacket packet);
        void OnCapture(IPacket packet);
        void OnKeyGrant(IPacket packet);
        void OnDeliver(IPacket packet);
        void OnRespawnReq(IPacket packet);
        void OnRespawnAck(IPacket packet);
    }
}
