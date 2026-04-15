using IOD.CaptureTheFlag.NanoFramework.Delegates;

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
        void Handle(byte[] raw, int rssi, float snr);

        event HeartbeatReceivedDelegate HeartbeatReceived;
        event GameStartDelegate GameStartReceived;
        event GameEndDelegate GameEndReceived;
        event AttackDelegate AttackReceived;
        event AttackAckDelegate AttackAckReceived;
        event CombatResultDelegate CombatResultReceived;
        event FlagTransferDelegate FlagTransferReceived;
        event CaptureDelegate CaptureReceived;
        event KeyGrantDelegate KeyGrantReceived;
        event DeliverDelegate DeliverReceived;
        event RespawnReqDelegate RespawnReqReceived;
        event RespawnAckDelegate RespawnAckReceived;
    }
}
