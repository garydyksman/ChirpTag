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
        private readonly object _displayLock;
        private readonly IPacketBuilder _builder;
        private readonly IMessageHandler _messageHandler;
        private readonly TxQueue _txQueue;
        private readonly IGameHttpClient _httpClient;
        private readonly IWifiHttpBridge _wifiHttpBridge;
        private readonly byte[] _heartbeatBytes;
        private readonly System.Action _pauseLoRa;
        private readonly System.Action _resumeLoRa;
        private readonly object _runningLock = new object();
        private readonly object _wifiLock = new object();
        private readonly object _respawnLock = new object();
        private bool _txPaused;
        private bool _respawnInFlight;

        private byte[] _key;
        private bool _keyReady;
        private bool _keyTaken;
        private bool _isRunning;
        private byte _gameId;
        private byte _pendingGameEndWinnerId;
        private byte _pendingGameEndGameId;
        private int _captureCount;
        private int _deliverCount;

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
        public int CaptureCount => _captureCount;
        public int DeliverCount => _deliverCount;

        public FlagNodeDevice(
            byte flagNodeId,
            Sx1262 lora,
            Ssd1306 display,
            object displayLock,
            IGameHttpClient httpClient,
            IWifiHttpBridge wifiHttpBridge,
            System.Action pauseLoRaForWifi,
            System.Action resumeLoRaAfterWifi,
            byte gameId = 0)
        {
            _flagNodeId = flagNodeId;
            _lora = lora;
            _display = display;
            _displayLock = displayLock ?? throw new ArgumentNullException(nameof(displayLock));
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
            _keyReady = false;
            _keyTaken = false;

            // Wire up event handlers
            _messageHandler.CaptureReceived += OnCaptureReceived;
            _messageHandler.DeliverReceived += OnDeliverReceived;
            _messageHandler.RespawnReqReceived += OnRespawnRequestReceived;
            _messageHandler.GameEndReceived += OnGameEnd;
        }

        // ---- Job classes — capture args before background-thread dispatch ----

        private sealed class DeliverJob
        {
            private readonly FlagNodeDevice _owner;
            private readonly byte _fromDeviceId;
            private readonly byte[] _key;
            internal DeliverJob(FlagNodeDevice owner, byte fromDeviceId, byte[] key)
            {
                _owner = owner;
                _fromDeviceId = fromDeviceId;
                _key = key;
            }
            internal void Run() { _owner.DeliverWorkerThreadImpl(_fromDeviceId, _key); }
        }

        private sealed class RespawnJob
        {
            private readonly FlagNodeDevice _owner;
            private readonly byte _fromDeviceId;
            internal RespawnJob(FlagNodeDevice owner, byte fromDeviceId)
            {
                _owner = owner;
                _fromDeviceId = fromDeviceId;
            }
            internal void Run() { _owner.RespawnWorkerThreadImpl(_fromDeviceId); }
        }

        // IGameDevice lifecycle methods
        public void OnGameStart()
        {
            Console.WriteLine("[FlagNode] Game starting...");
            lock (_runningLock) { _isRunning = true; }

            // Fetch key — retry up to 3 times before giving up for the session
            for (int attempt = 1; attempt <= 3 && !_keyReady; attempt++)
            {
                FetchKeyFromServer();
                if (!_keyReady && attempt < 3)
                {
                    Console.WriteLine($"[FlagNode] Key unavailable after attempt {attempt}/3, retrying in 5s");
                    Thread.Sleep(5_000);
                }
            }

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
            if (gameId != _gameId)
            {
                Console.WriteLine($"[FlagNode] OnGameEnd: game ID mismatch ({gameId} != {_gameId}), ignoring");
                return;
            }
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
            for (int i = 0; i < 8 && _isRunning; i++)
            {
                Console.WriteLine($"[FlagNode] GameEnd relay {i + 1}/8");
                _txQueue.Enqueue(_builder.GameEnd(_flagNodeId, winnerId, gameId).ToBytes());
                Thread.Sleep(2_000);
            }
            lock (_runningLock) { _isRunning = false; }
        }

        public void FetchKeyFromServer()
        {
            Console.WriteLine("[FlagNode] Fetching key from server...");
            UpdateDisplay("FLAG NODE", "Fetching key...");

            lock (_wifiLock)
            {
                PauseLoRa();
                if (!EnableWiFi())
                {
                    ResumeLoRa();
                    Console.WriteLine("[FlagNode] WiFi failed for key fetch; key unavailable until retry");
                    UpdateDisplay("FLAG NODE", "Key unavail");
                    return;
                }

                try
                {
                    byte[] fetched = _httpClient.GetFlagKey(_flagNodeId);
                    if (fetched != null && fetched.Length > 0)
                    {
                        _key = fetched;
                        _keyReady = true;
                        Console.WriteLine("[FlagNode] Key fetched");
                        UpdateDisplay("FLAG NODE", "Key ready");
                    }
                    else
                    {
                        _key = new byte[4];
                        _keyReady = false;
                        Console.WriteLine("[FlagNode] Server returned empty key — capture disabled");
                        UpdateDisplay("FLAG NODE", "No key yet");
                    }
                    _keyTaken = false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FlagNode] FetchKey error: {ex.Message}");
                    _key = new byte[4];
                    _keyReady = false;
                    UpdateDisplay("FLAG NODE", "Key fetch failed");
                }
                finally
                {
                    TearDownWiFi();
                    ResumeLoRa();
                }
            }
        }

        // ---- LoRa RX ----

        public void OnLoRaPacketReceived(byte[] receivedPacket, int rssi, float snr)
        {
            try
            {
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

            if (!_keyReady)
            {
                Console.WriteLine("[FlagNode] Key not ready — capture ignored");
                UpdateDisplay("CAPTURE", "No key!");
                return;
            }

            if (_keyTaken)
            {
                Console.WriteLine("[FlagNode] Key already taken, ignoring capture");
                UpdateDisplay("CAPTURE", "Key taken!");
                return;
            }

            if (SendKeyGrant(fromDeviceId))
            {
                _keyTaken = true;
                _captureCount++;
                UpdateDisplay("KEY CAPTURED", $"By: 0x{fromDeviceId:X2}");
            }
            else
            {
                Console.WriteLine("[FlagNode] KeyGrant dropped — TX queue full");
                UpdateDisplay("CAPTURE", "TX full!");
            }
        }

        public void OnDeliverReceived(byte fromDeviceId, byte[] key)
        {
            Console.WriteLine($"[FlagNode] Deliver from device 0x{fromDeviceId:X2}");
            UpdateDisplay("DELIVER", $"From: 0x{fromDeviceId:X2}");

            // Dispatch to background thread so the LoRa poll callback returns immediately.
            // Capture args in a job object to avoid racing with the next Deliver packet.
            var job = new DeliverJob(this, fromDeviceId, key);
            new Thread(job.Run).Start();
        }

        internal void DeliverWorkerThreadImpl(byte fromDeviceId, byte[] key)
        {
            bool success = ReportDeliver(fromDeviceId, key);

            if (success)
            {
                Console.WriteLine("[FlagNode] Delivery successful - sending DeliverAck + GameEnd");
                _deliverCount++;
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

            lock (_respawnLock)
            {
                if (_respawnInFlight)
                {
                    Console.WriteLine($"[FlagNode] Respawn already in flight — dropping request from 0x{fromDeviceId:X2}");
                    return;
                }
                _respawnInFlight = true;
            }

            UpdateDisplay("RESPAWN", $"0x{fromDeviceId:X2}");
            var job = new RespawnJob(this, fromDeviceId);
            new Thread(job.Run).Start();
        }

        internal void RespawnWorkerThreadImpl(byte fromDeviceId)
        {
            try
            {
                byte newScore = FetchRespawnNumber(fromDeviceId);
                if (newScore == 0)
                {
                    Console.WriteLine($"[FlagNode] Respawn fetch failed for 0x{fromDeviceId:X2} — not acking");
                    UpdateDisplay("RESPAWN", "Failed");
                    return;
                }
                Console.WriteLine($"[FlagNode] Respawn score for 0x{fromDeviceId:X2}: {newScore}");
                SendRespawnAck(fromDeviceId, newScore);
                UpdateDisplay("RESPAWN", "Ack sent");
            }
            finally
            {
                lock (_respawnLock) { _respawnInFlight = false; }
            }
        }

        // ---- LoRa TX ----

        public bool SendKeyGrant(byte targetDeviceId)
        {
            var packet = _builder.KeyGrant(_flagNodeId, targetDeviceId, _key);
            bool queued = _txQueue.Enqueue(packet.ToBytes());
            if (queued)
                Console.WriteLine($"[FlagNode] Queued KeyGrant to 0x{targetDeviceId:X2}");
            else
                Console.WriteLine($"[FlagNode] KeyGrant dropped (TX full) to 0x{targetDeviceId:X2}");
            return queued;
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

            lock (_wifiLock)
            {
                PauseLoRa();
                if (!EnableWiFi())
                {
                    ResumeLoRa();
                    return 0;
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
                    return 0;
                }
                finally
                {
                    TearDownWiFi();
                    ResumeLoRa();
                }
            }
        }

        public bool ReportDeliver(byte deviceId, byte[] key)
        {
            Console.WriteLine($"[FlagNode] ReportDeliver device 0x{deviceId:X2}, key: [{key.Length}b]");

            lock (_wifiLock)
            {
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
        }

        // ---- WiFi/LoRa Coordination ----

        private void PauseLoRa()
        {
            Console.WriteLine("[FlagNode] Pausing LoRa for WiFi...");
            lock (_wifiLock) { _txPaused = true; }
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
            lock (_wifiLock) { _txPaused = false; }
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
                    lock (_wifiLock)
                    {
                        if (!_txPaused)
                        {
                            byte[] packetBytes = _txQueue.Dequeue();
                            if (packetBytes != null)
                            {
                                Console.WriteLine($"[FlagNode] Sending LoRa packet, length: {packetBytes.Length}");
                                _lora.Send(packetBytes, timeoutMs: 5000);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[FlagNode] TxLoop error: {ex.Message}");
                }

                Thread.Sleep(500);
            }

            // Game ended — keep draining for 20 more seconds so any GameEnd broadcasts
            // still being enqueued by DeliverWorkerThreadImpl / GameEndRetransmitThread are sent.
            Console.WriteLine("[FlagNode] TxLoop draining...");
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    byte[] packetBytes = _txQueue.Dequeue();
                    if (packetBytes != null)
                        _lora.Send(packetBytes, timeoutMs: 5000);
                }
                catch { }
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
                lock (_displayLock)
                {
                    _display.ClearScreen();
                    _display.DrawString(4, 6, line1, 1);
                    _display.DrawString(4, 20, line2, 1);
                    _display.Display();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FlagNode] Display update error: " + ex.Message);
            }
        }
    }
}
