using System;
using System.Threading;
using Iot.Device.Ssd13xx;
using Iot.Device.LoRa.Drivers.Sx1262;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Implementations;
using IOD.CaptureTheFlag.NanoFramework.Types;

namespace ChirpTagFlagNode
{
    public class FlagNodeDevice : IFlagNode
    {
        private readonly byte _flagNodeId;
        private readonly Sx1262 _lora;
        private readonly Ssd1306 _display;
        private readonly IPacketBuilder _builder;
        private readonly IMessageHandler _messageHandler;
        private readonly TxQueue _txQueue;
        private readonly IGameHttpClient _httpClient;
        private readonly IWifiHttpBridge _wifiHttpBridge;

        private byte[] _key;
        private bool _keyTaken;
        private bool _isRunning;

        private Thread _txThread;

        // IGameDevice implementation
        public byte DeviceId => _flagNodeId;
        public byte DeviceType => IOD.CaptureTheFlag.NanoFramework.Types.DeviceType.FlagNode;
        public IMessageHandler Handler => _messageHandler;

        // IFlagNode implementation
        public byte FlagNodeId => _flagNodeId;
        public byte[] Key => _key;
        public bool KeyTaken => _keyTaken;

        public FlagNodeDevice(
            byte flagNodeId,
            Sx1262 lora,
            Ssd1306 display,
            IGameHttpClient httpClient,
            IWifiHttpBridge wifiHttpBridge)
        {
            _flagNodeId = flagNodeId;
            _lora = lora;
            _display = display;
            _httpClient = httpClient;
            _wifiHttpBridge = wifiHttpBridge;

            _builder = new PacketBuilder();
            _messageHandler = new MessageHandler(new PacketParser(), flagNodeId);
            _txQueue = new TxQueue(capacity: 8);

            _key = new byte[4];
            _keyTaken = false;

            // Wire up event handlers
            _messageHandler.CaptureReceived += OnCaptureReceived;
            _messageHandler.DeliverReceived += OnDeliverReceived;
            _messageHandler.RespawnReqReceived += OnRespawnRequestReceived;

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

            Console.WriteLine("[FlagNode] Game started");
            UpdateDisplay("GAME ACTIVE", "Ready");
        }

        public void OnGameEnd(byte winnerId)
        {
            Console.WriteLine($"[FlagNode] Game ended. Winner: 0x{winnerId:X2}");
            _isRunning = false;
            UpdateDisplay("GAME OVER", $"Winner: 0x{winnerId:X2}");
        }

        public void FetchKeyFromServer()
        {
            Console.WriteLine("[FlagNode] Fetching key from server...");
            UpdateDisplay("FLAG NODE", "Fetching key...");

            try
            {
                // TODO: Implement server API call to fetch key
                // For now, generate a placeholder key
                _key = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
                _keyTaken = false;

                Console.WriteLine($"[FlagNode] Key fetched: {BitConverter.ToString(_key)}");
                UpdateDisplay("FLAG NODE", "Key ready");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] FetchKey error: {ex.Message}");
                UpdateDisplay("FLAG NODE", "Key fetch failed");
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
            Console.WriteLine($"[FlagNode] Deliver from device 0x{fromDeviceId:X2}, key: {BitConverter.ToString(key)}");
            UpdateDisplay("DELIVER", $"From: 0x{fromDeviceId:X2}");

            // Report to server via HTTP
            bool success = ReportDeliver(fromDeviceId, key);

            if (success)
            {
                Console.WriteLine("[FlagNode] Delivery successful");
                UpdateDisplay("DELIVER", "Success!");
            }
            else
            {
                Console.WriteLine("[FlagNode] Delivery failed");
                UpdateDisplay("DELIVER", "Failed");
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
                // TODO: Call actual API endpoint
                // byte score = _httpClient.GetRespawnNumber(deviceId);

                // Placeholder: return random score 1-10
                byte score = (byte)((deviceId % 10) + 1);
                Console.WriteLine($"[FlagNode] Respawn score fetched: {score}");

                return score;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] FetchRespawnNumber error: {ex.Message}");
                return 5; // Default fallback
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
                // TODO: Call actual API endpoint
                // bool success = _httpClient.ReportDeliver(deviceId, key);

                // Placeholder: return success
                Console.WriteLine("[FlagNode] Deliver reported to server");
                return true;
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
            // TODO: Stop listening on LoRa (if needed)
        }

        private void ResumeLoRa()
        {
            Console.WriteLine("[FlagNode] Resuming LoRa...");
            // TODO: Resume listening on LoRa (if needed)
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

        // ---- Display ----

        private void UpdateDisplay(string line1, string line2)
        {
            try
            {
                _display.ClearScreen();
                PixelTextRenderer.DrawText(_display, 4, 10, line1, 1);
                PixelTextRenderer.DrawText(_display, 4, 30, line2, 1);
                _display.Display();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FlagNode] Display update error: {ex.Message}");
            }
        }
    }
}
