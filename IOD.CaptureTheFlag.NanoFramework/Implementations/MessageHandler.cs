using IOD.CaptureTheFlag.NanoFramework.Delegates;
using IOD.CaptureTheFlag.NanoFramework.Extensions;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;
using System;
using System.Diagnostics;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class MessageHandler : IMessageHandler
    {
        private const bool VerboseLogging = false;
        private readonly IPacketParser _parser;
        private readonly byte _deviceId;

        public event HeartbeatReceivedDelegate HeartbeatReceived;
        public event GameStartDelegate GameStartReceived;
        public event GameEndDelegate GameEndReceived;
        public event AttackDelegate AttackReceived;
        public event AttackAckDelegate AttackAckReceived;
        public event CombatResultDelegate CombatResultReceived;
        public event FlagTransferDelegate FlagTransferReceived;
        public event CaptureDelegate CaptureReceived;
        public event KeyGrantDelegate KeyGrantReceived;
        public event DeliverDelegate DeliverReceived;
        public event RespawnReqDelegate RespawnReqReceived;
        public event RespawnAckDelegate RespawnAckReceived;

        public MessageHandler(IPacketParser parser, byte deviceId)
        {
            _parser = parser;
            _deviceId = deviceId;
            if (VerboseLogging) Log("ctor");
        }

        // ---------------------------------------------------------------
        // Entry point — called by LoRa poll thread
        // ---------------------------------------------------------------

        public void Handle(byte[] raw, int rssi, float snr)
        {
            if (VerboseLogging) Log("Handle begin");

            if (raw == null || raw.Length < 4)
            {
                if (VerboseLogging) Log("Handle drop minimum length");
                if (CombatListDiagnostics.Enabled)
                {
                    CombatListDiagnostics.Write("RX drop: too short len=" + (raw == null ? "null" : raw.Length.ToString()));
                }

                return;
            }

            byte fromDeviceId = raw[0];
            byte msgType = raw[1];
            byte targetId = raw[2];

            int expectedLength = _parser.ExpectedLength(msgType);
            if (expectedLength == -1 || raw.Length != expectedLength)
            {
                if (VerboseLogging) Log("Handle drop expected length");
                if (CombatListDiagnostics.Enabled)
                {
                    CombatListDiagnostics.Write(
                        "RX drop: len mismatch from=0x" + fromDeviceId.ToString("X2") + " type=0x" + msgType.ToString("X2") + " len=" + raw.Length.ToString() + " expected=" + expectedLength.ToString());
                }

                return;
            }

            if (!raw.Validate())
            {
                if (VerboseLogging) Log("Handle drop CRC");
                if (CombatListDiagnostics.Enabled)
                {
                    CombatListDiagnostics.Write(
                        "RX drop: CRC from=0x" + fromDeviceId.ToString("X2") + " type=0x" + msgType.ToString("X2") + " len=" + raw.Length.ToString());
                }

                return;
            }

            // TargetId filter - accept broadcast or addressed to us.
            if (targetId != 0x00 && targetId != _deviceId)
            {
                if (VerboseLogging) Log("Handle drop target mismatch");
                if (CombatListDiagnostics.Enabled)
                {
                    CombatListDiagnostics.Write(
                        "RX drop: target not us from=0x" + fromDeviceId.ToString("X2") + " type=0x" + msgType.ToString("X2") + " target=0x" + targetId.ToString("X2") + " me=0x" + _deviceId.ToString("X2"));
                }

                return;
            }

            if (VerboseLogging)
                DebugLog.Write("RX packet accepted");
            if (VerboseLogging) Log("Handle route");

            if (CombatListDiagnostics.Enabled)
            {
                CombatListDiagnostics.Write(
                    "RX ok from=0x" + fromDeviceId.ToString("X2") + " type=0x" + msgType.ToString("X2") + " rssi=" + rssi.ToString());
            }

            switch (msgType)
            {
                case PacketType.Heartbeat:
                    if (VerboseLogging) Log("Handle dispatch HeartbeatReceived");
                    HeartbeatReceived?.Invoke(fromDeviceId, rssi, snr);
                    break;

                case PacketType.GameStart:
                    if (VerboseLogging) Log("Handle dispatch GameStartReceived");
                    GameStartReceived?.Invoke();
                    break;

                case PacketType.GameEnd:
                    if (VerboseLogging) Log("Handle dispatch GameEndReceived");
                    GameEndReceived?.Invoke(raw[3]);
                    break;

                case PacketType.Attack:
                    if (VerboseLogging) Log("Handle dispatch AttackReceived");
                    AttackReceived?.Invoke(fromDeviceId);
                    break;

                case PacketType.AttackAck:
                    if (VerboseLogging) Log("Handle dispatch AttackAckReceived");
                    AttackAckReceived?.Invoke(fromDeviceId, raw[3]);
                    break;

                case PacketType.CombatResult:
                    if (VerboseLogging) Log("Handle dispatch CombatResultReceived");
                    if (CombatResultReceived != null)
                        CombatResultReceived(fromDeviceId, raw[3]); // winnerId
                    break;

                case PacketType.FlagTransfer:
                    if (VerboseLogging) Log("Handle dispatch FlagTransferReceived");
                    FlagTransferReceived?.Invoke(fromDeviceId, CopyPayload(raw, 4));
                    break;

                case PacketType.Capture:
                    if (VerboseLogging) Log("Handle dispatch CaptureReceived");
                    CaptureReceived?.Invoke(fromDeviceId);
                    break;

                case PacketType.KeyGrant:
                    if (VerboseLogging) Log("Handle dispatch KeyGrantReceived");
                    KeyGrantReceived?.Invoke(fromDeviceId, CopyPayload(raw, 4));
                    break;

                case PacketType.Deliver:
                    if (VerboseLogging) Log("Handle dispatch DeliverReceived");
                    DeliverReceived?.Invoke(fromDeviceId, CopyPayload(raw, 4));
                    break;

                case PacketType.RespawnReq:
                    if (VerboseLogging) Log("Handle dispatch RespawnReqReceived");
                    RespawnReqReceived?.Invoke(fromDeviceId);
                    break;

                case PacketType.RespawnAck:
                    if (VerboseLogging) Log("Handle dispatch RespawnAckReceived");
                    RespawnAckReceived?.Invoke(raw[3]);
                    break;

                default:
                    if (VerboseLogging) Log("Handle unknown packet type");
                    if (CombatListDiagnostics.Enabled)
                    {
                        CombatListDiagnostics.Write("RX unhandled type=0x" + msgType.ToString("X2") + " from=0x" + fromDeviceId.ToString("X2"));
                    }

                    break;
            }
            if (VerboseLogging) Log("Handle end");
        }

        private static byte[] CopyPayload(byte[] raw, int length)
        {
            byte[] payload = new byte[length];
            Array.Copy(raw, 3, payload, 0, length);
            return payload;
        }

        private void Log(string message)
        {
            if (VerboseLogging)
                DebugLog.Write("[MessageHandler] " + message);
        }
    }
}
