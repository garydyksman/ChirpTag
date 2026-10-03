using System;
using System.Threading;
using Iot.Device.LoRa.Drivers.Sx1262;
using Iot.Device.Ssd13xx;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Implementations;
using IOD.CaptureTheFlag.NanoFramework.Types;

namespace ChirpTagFlagNode
{
    public class FlagNodeDevice : IFlagNode
    {
        private const int HeartbeatIntervalMs = 5_000;

        private readonly byte _flagNodeId;
        private readonly Sx1262 _lora;
        private readonly Ssd1306 _display;
        private readonly IPacketBuilder _builder;
        private readonly IMessageHandler _messageHandler;
        private readonly TxQueue _txQueue;
        private readonly IGameHttpClient _httpClient;
        private readonly IWifiHttpBridge _wifiHttpBridge;
        private readonly byte[] _heartbeatBytes;
        private readonly System.Action _pauseLoRa;
        private readonly System.Action _resumeLoRa;

        private byte[] _key;
        private bool _keyTaken;
        private bool _isRunning;
        private byte _pendingDeliverFromId;
        private byte[] _pendingDeliverKey;
        private byte _gameId;
        private byte _pendingGameEndWinnerId;
        private byte _pendingGameEndGameId;

        private Thread _txThread;
        private Thread _heartbeatThread;

        // IGameDevice implementation
        public byte DeviceId => _flagNodeId;
        public byte DeviceType => IOD.CaptureTheFlag.NanoFramework.Types.DeviceType.FlagNode;
        public IMessageHandler Handler => _messageHandler;

        // IFlagNode implementation
        public byte FlagNodeId => _flagNodeId;
        public byte[] Key => _key;
        public bool KeyTaken => _keyTaken;
        public bool IsRunning => _isRunning;

        public FlagNodeDevice(
            byte flagNodeId,
            Sx1262 lora,
            Ssd1306 display,
            IGameHttpClient httpClient,
            IWifiHttpBridge wifiHttpBridge,
            System.Action pauseLoRaForWifi,
            System.Action resumeLoRaAfterWifi,
            byte gameId = 0)
        {
            _flagNodeId = flagNodeId;
            _lora = lora;
            _display = display;
            _httpClient = httpClient;
            _wifiHttpBridge = wifiHttpBridge;
            _pauseLoRa = pauseLoRaForWifi;
            _resumeLoRa = resumeLoRaAfterWifi;
            _gameId = gameId;

            _builder = new PacketBuilder();
            _messageHandler = new MessageHandler(new PacketParser(), flagNodeId);
            _txQueue = new TxQueue(capacity: 8);
            _heartbeatBytes = _builder.Heartbeat(_flagNodeId, DeviceType).ToBytes();

            _key = new byte[4];
            _keyTaken = false;

            // Wire up event handlers
            _messageHandler.CaptureReceived += OnCaptureReceived;
            _messageHandler.DeliverReceived += OnDeliverReceived;
            _messageHandler.RespawnReqReceived += OnRespawnRequestReceived;
            _messageHandler.GameEndReceived += OnGameEnd;

            // LoRa receive handler will be wired up after LoRa init
            // _lora needs to call SetReceiveMode() first
        }

        // IGameDevice lifecycle methods
        public void OnGameStart()
        {
            Console.WriteLine("[FlagNode] Game starting...");
            _isRunning = true;

            // Fetch key from server
            FetchKeyFromServer();

            // Start TX loop thread
            _txThread = new Thread(TxLoop);
            _txThread.Start();

            // Start heartbeat loop thread so players classify this node as a flag node.
            _heartbeatThread = new Thread(HeartbeatLoop);
            _heartbeatThread.Start();

            Console.WriteLine("[FlagNode] Game started");
            UpdateDisplay("GAME ACTIVE", "Ready");
        }

        public void OnGameEnd(byte winnerId, byte gameId)
        {
            Console.WriteLine($"[FlagNode] Game ended. Winner: 0x{winnerId:X2} — relaying via LoRa");
            UpdateDisplay("GAME OVER", $"Winner: 0x{winnerId:X2}");
            _pendingGameEndWinnerId = winnerId;
            _pendingGameEndGameId = gameId;
            new Thread(GameEndRetransmitThread).Start();
        }

        private void GameEndRetransmitThread()
        {
            byte winnerId = _pendingGameEndWinnerId;
            byte gameId = _pendingGameEndGameId;
            for (int i = 0; i < 5 && _isRunning; i++)
            {
                Console.WriteLine($"[FlagNode] GameEnd relay {i + 1}/5");
                _txQueue.Enqueue(_builder.GameEnd(_flagNodeId, winnerId, gameId).ToBytes());
                Thread.Sleep(2_000);
            }
            _isRunning = false;
        }

        public void FetchKeyFromServer()
        {
            Console.WriteLine("[FlagNode] Fetching key from server...");
            UpdateDisplay("FLAG NODE", "Fetching key...");

            PauseLoRa();
            if (!EnableWiFi())
            {
                ResumeLoRa();
                Console.WriteLine("[FlagNode] WiFi failed for key fetch; using fallback key");
                _key = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                UpdateDisplay("FLAG NODE", "Key fallback");
                return;
            }

            try
            {
                byte[] fetched = _httpClient.GetFlagKey(_flagNodeId);
                _key = (fetched != null && fetched.Length > 0) ? fetched : new byte[4];
                _keyTaken = false;
                Console.WriteLine("[FlagNode] Key fetched");
                UpdateDisplay("FLAG NODE", "Key ready");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] FetchKey error: {ex.Message}");
                _key = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                UpdateDisplay("FLAG NODE", "Key fetch failed");
            }
            finally
            {
                TearDownWiFi();
                ResumeLoRa();
            }
        }

        // ---- LoRa RX ----

        public void OnLoRaPacketReceived(byte[] receivedPacket, int rssi, float snr)
        {
            try
            {
                // Pass raw packet to message handler for parsing and routing
                _messageHandler.Handle(receivedPacket, rssi, snr);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] LoRa RX error: {ex.Message}");
            }
        }

        // ---- Packet Handlers ----

        public void OnCaptureReceived(byte fromDeviceId)
        {
            Console.WriteLine($"[FlagNode] Capture request from device 0x{fromDeviceId:X2}");

            if (_keyTaken)
            {
                Console.WriteLine("[FlagNode] Key already taken, ignoring capture");
                UpdateDisplay("CAPTURE", "Key taken!");
                return;
            }

            // Grant key to player
            Console.WriteLine($"[FlagNode] Granting key to device 0x{fromDeviceId:X2}");
            SendKeyGrant(fromDeviceId);

            _keyTaken = true;
            UpdateDisplay("KEY CAPTURED", $"By: 0x{fromDeviceId:X2}");
        }

        public void OnDeliverReceived(byte fromDeviceId, byte[] key)
        {
            Console.WriteLine($"[FlagNode] Deliver from device 0x{fromDeviceId:X2}");
            UpdateDisplay("DELIVER", $"From: 0x{fromDeviceId:X2}");

            // Dispatch to background thread so the LoRa poll callback returns immediately.
            // PauseLoRa/ResumeLoRa must not be called from the poll thread — see DeliverWorkerThread.
            _pendingDeliverFromId = fromDeviceId;
            _pendingDeliverKey = key;
            new Thread(DeliverWorkerThread).Start();
        }

        private void DeliverWorkerThread()
        {
            byte fromDeviceId = _pendingDeliverFromId;
            byte[] key = _pendingDeliverKey;

            bool success = ReportDeliver(_flagNodeId, key);

            if (success)
            {
                Console.WriteLine("[FlagNode] Delivery successful - sending DeliverAck + GameEnd");
                UpdateDisplay("DELIVER", "Success!");
            }
            else
            {
                Console.WriteLine("[FlagNode] Delivery failed - sending DeliverAck(rejected)");
                UpdateDisplay("DELIVER", "Failed");
            }

            var ack = _builder.DeliverAck(_flagNodeId, fromDeviceId, success);
            _txQueue.Enqueue(ack.ToBytes());

            if (success)
            {
                _txQueue.Enqueue(_builder.GameEnd(_flagNodeId, fromDeviceId, _gameId).ToBytes());
                // Retransmit GameEnd every 3 s for 30 s so devices that missed the first broadcast receive it
                for (int i = 0; i < 10 && _isRunning; i++)
                {
                    Thread.Sleep(3_000);
                    Console.WriteLine($"[FlagNode] GameEnd retransmit {i + 1}/10");
                    _txQueue.Enqueue(_builder.GameEnd(_flagNodeId, fromDeviceId, _gameId).ToBytes());
                }
            }
        }

        public void OnRespawnRequestReceived(byte fromDeviceId)
        {
            Console.WriteLine($"[FlagNode] Respawn request from device 0x{fromDeviceId:X2}");
            UpdateDisplay("RESPAWN", $"0x{fromDeviceId:X2}");

            // Fetch respawn number from server
            byte newScore = FetchRespawnNumber(fromDeviceId);

            Console.WriteLine($"[FlagNode] Respawn score for 0x{fromDeviceId:X2}: {newScore}");
            SendRespawnAck(fromDeviceId, newScore);

            UpdateDisplay("RESPAWN", "Ack sent");
        }

        // ---- LoRa TX ----

        public void SendKeyGrant(byte targetDeviceId)
        {
            var packet = _builder.KeyGrant(_flagNodeId, targetDeviceId, _key);
            _txQueue.Enqueue(packet.ToBytes());
            Console.WriteLine($"[FlagNode] Queued KeyGrant to 0x{targetDeviceId:X2}");
        }

        public void SendRespawnAck(byte targetDeviceId, byte newCombatNumber)
        {
            var packet = _builder.RespawnAck(_flagNodeId, targetDeviceId, newCombatNumber);
            _txQueue.Enqueue(packet.ToBytes());
            Console.WriteLine($"[FlagNode] Queued RespawnAck to 0x{targetDeviceId:X2}, score: {newCombatNumber}");
        }

        // ---- HTTP ----

        public byte FetchRespawnNumber(byte deviceId)
        {
            Console.WriteLine($"[FlagNode] FetchRespawnNumber for device 0x{deviceId:X2}");

            // Pause LoRa, enable WiFi
            PauseLoRa();
            if (!EnableWiFi())
            {
                ResumeLoRa();
                return 5; // Default fallback score
            }

            try
            {
                byte score = _httpClient.GetRespawnNumber(deviceId);
                Console.WriteLine($"[FlagNode] Respawn score fetched: {score}");
                return score;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] FetchRespawnNumber error: {ex.Message}");
                return 5;
            }
            finally
            {
                TearDownWiFi();
                ResumeLoRa();
            }
        }

        public bool ReportDeliver(byte deviceId, byte[] key)
        {
            Console.WriteLine($"[FlagNode] ReportDeliver device 0x{deviceId:X2}, key: {BitConverter.ToString(key)}");

            // Pause LoRa, enable WiFi
            PauseLoRa();
            if (!EnableWiFi())
            {
                ResumeLoRa();
                return false;
            }

            try
            {
                bool accepted = _httpClient.ReportDeliver(deviceId, key);
                Console.WriteLine($"[FlagNode] Deliver reported, accepted={accepted}");
                return accepted;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] ReportDeliver error: {ex.Message}");
                return false;
            }
            finally
            {
                TearDownWiFi();
                ResumeLoRa();
            }
        }

        // ---- WiFi/LoRa Coordination ----

        private void PauseLoRa()
        {
            Console.WriteLine("[FlagNode] Pausing LoRa for WiFi...");
            if (_pauseLoRa != null)
            {
                try { _pauseLoRa(); }
                catch (Exception ex) { Console.WriteLine($"[FlagNode] PauseLoRa error: {ex.Message}"); }
            }
        }

        private void ResumeLoRa()
        {
            Console.WriteLine("[FlagNode] Resuming LoRa...");
            if (_resumeLoRa != null)
            {
                try { _resumeLoRa(); }
                catch (Exception ex) { Console.WriteLine($"[FlagNode] ResumeLoRa error: {ex.Message}"); }
            }
        }

        private bool EnableWiFi()
        {
            Console.WriteLine("[FlagNode] Enabling WiFi...");
            try
            {
                var outcome = _wifiHttpBridge.EnableForHttp();
                return outcome == WifiHttpBootOutcome.Connected;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] WiFi enable error: {ex.Message}");
                return false;
            }
        }

        private void TearDownWiFi()
        {
            Console.WriteLine("[FlagNode] Tearing down WiFi...");
            try
            {
                _wifiHttpBridge.TearDownRadio();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] WiFi teardown error: {ex.Message}");
            }
        }

        // ---- TX Loop Thread ----

        private void TxLoop()
        {
            Console.WriteLine("[FlagNode] TxLoop started");

            while (_isRunning)
            {
                try
                {
                    byte[] packetBytes = _txQueue.Dequeue();
                    if (packetBytes != null)
                    {
                        Console.WriteLine($"[FlagNode] Sending LoRa packet, length: {packetBytes.Length}");
                        _lora.Send(packetBytes, timeoutMs: 5000);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FlagNode] TxLoop error: {ex.Message}");
                }

                Thread.Sleep(500);
            }

            Console.WriteLine("[FlagNode] TxLoop stopped");
        }

        private void HeartbeatLoop()
        {
            Console.WriteLine("[FlagNode] HeartbeatLoop started");

            while (_isRunning)
            {
                try
                {
                    if (!_txQueue.Enqueue(_heartbeatBytes))
                    {
                        Console.WriteLine("[FlagNode] Heartbeat dropped (TX queue full)");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FlagNode] HeartbeatLoop error: {ex.Message}");
                }

                Thread.Sleep(HeartbeatIntervalMs);
            }

            Console.WriteLine("[FlagNode] HeartbeatLoop stopped");
        }

        // ---- Display ----

        private void UpdateDisplay(string line1, string line2)
        {
            try
            {
                if (_display == null) return;
                _display.ClearScreen();
                _display.DrawString(4, 6, line1, 1);
                _display.DrawString(4, 20, line2, 1);
                _display.Display();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FlagNode] Display update error: " + ex.Message);
            }
        }
    }
}
