using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using System.Device.Wifi;
using System.Net.Security;
using System.Net.NetworkInformation;
using System.Threading;
using Iot.Device.Ssd13xx;
using Iot.Device.Ssd13xx.Commands;
using Iot.Device.LoRa.Drivers.Sx1262;
using nanoFramework.Hardware.Esp32;
using nanoFramework.Networking;
using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Implementations;
using IOD.CaptureTheFlag.NanoFramework.Types;
using static IOD.CaptureTheFlag.NanoFramework.Implementations.WiFiHelper;

namespace ChirpTagFlagNode
{
    public class Program
    {
        // Heltec WiFi LoRa 32 V4 (HTIT-WB32LAF v4.3) pin mapping

        // OLED Display (I2C)
        private const int I2cBus = 1;
        private const int SdaPin = 17;
        private const int SclPin = 18;
        private const int OledRstPin = 21;

        // LoRa SX1262 (SPI1)
        private const int PinLoraMosi = 10;
        private const int PinLoraClk = 9;
        private const int PinLoraMiso = 11;
        private const int PinLoraCs = 8;
        private const int PinLoraRst = 12;
        private const int PinLoraBusy = 13;
        private const int PinLoraDio1 = 14;

        // PRG Button
        private const int PrgButtonPin = 0;

        // Display manager
        private static FlagNodeDisplayManager s_displayManager;
        private static bool s_modeChangeRequested = false;
        private static readonly object s_modeLock = new object();

        // Display constants
        private const int DisplayWidth = 128;
        private const int DisplayHeight = 64;
        private const bool EnablePixelText = true;

        public static void Main()
        {
            // Wait for serial to stabilize
            Thread.Sleep(2000);

            Console.WriteLine("");
            Console.WriteLine("");
            Console.WriteLine("========================================");
            Console.WriteLine("FLAG NODE - ULTRA MINIMAL TEST");
            Console.WriteLine("========================================");
            Console.WriteLine("");

            int step = 0;

            try
            {
                step = 1;
                Console.WriteLine("[STEP 1/7] Setting up PRG button...");
                Thread.Sleep(100);

                // Set up GPIO controller and PRG button
                var gpio = new GpioController();
                var prgButton = gpio.OpenPin(PrgButtonPin, PinMode.InputPullUp);
                prgButton.ValueChanged += OnPrgButtonPressed;

                Console.WriteLine("[STEP 2/7] Hardware reset OLED...");
                Thread.Sleep(100);

                // Hardware reset the OLED (CRITICAL for V4!)
                var rst = gpio.OpenPin(OledRstPin, PinMode.Output);
                rst.Write(PinValue.Low);
                Thread.Sleep(10);
                rst.Write(PinValue.High);
                Thread.Sleep(10);

                step = 3;
                Console.WriteLine("[STEP 3/7] Mapping I2C pins...");
                Thread.Sleep(100);

                // Map I2C pins
                Configuration.SetPinFunction(SdaPin, DeviceFunction.I2C1_DATA);
                Configuration.SetPinFunction(SclPin, DeviceFunction.I2C1_CLOCK);

                step = 4;
                Console.WriteLine("[STEP 4/7] I2C pins mapped");
                Thread.Sleep(100);

                // Create I2C device
                var i2c = I2cDevice.Create(new I2cConnectionSettings(I2cBus, Ssd1306.DefaultI2cAddress));

                step = 5;
                Console.WriteLine("[STEP 5/7] I2C device created");
                Thread.Sleep(100);

                // Create display
                var display = new Ssd1306(i2c, Ssd13xx.DisplayResolution.OLED128x64);

                // Use default contrast (127) - seems to work best for this display
                display.SendCommand(new SetContrastControlForBank0(127));

                step = 6;
                Console.WriteLine("[STEP 6/7] Display created - contrast at default (127)");
                Thread.Sleep(100);

                // Clear and show test pattern
                display.ClearScreen();
                display.DrawFilledRectangle(0, 0, 128, 64, true);
                display.DrawFilledRectangle(10, 10, 108, 44, false);
                display.Display();

                step = 7;
                Console.WriteLine("[STEP 7/7] Display initialized");
                Console.WriteLine("");

                // ================================================================
                // WiFi Connection
                // ================================================================
                step = 8;
                Console.WriteLine("[STEP 8/12] Connecting to WiFi...");

                display.ClearScreen();
                if (EnablePixelText)
                {
                    PixelTextRenderer.DrawText(display, 6, 6, "FLAG NODE", 1);
                    PixelTextRenderer.DrawText(display, 6, 20, "WIFI...", 1);
                }
                display.Display();

                // Connect to WiFi using WifiNetworkHelper
                CancellationTokenSource cts = new CancellationTokenSource(60000); // 60 second timeout
                bool wifiSuccess = WifiNetworkHelper.ConnectDhcp(
                    LocalConfig.WIFI_SSID,
                    LocalConfig.WIFI_PASSWORD,
                    requiresDateTime: false,
                    token: cts.Token);

                if (!wifiSuccess)
                {
                    Console.WriteLine($"WiFi connection FAILED! Status: {WifiNetworkHelper.Status}");
                    if (WifiNetworkHelper.HelperException != null)
                    {
                        Console.WriteLine($"Exception: {WifiNetworkHelper.HelperException}");
                    }
                    display.ClearScreen();
                    if (EnablePixelText)
                    {
                        PixelTextRenderer.DrawText(display, 6, 6, "WIFI", 1);
                        PixelTextRenderer.DrawText(display, 6, 20, "FAILED", 1);
                    }
                    display.Display();
                    Thread.Sleep(Timeout.Infinite);
                }

                var ni = NetworkInterface.GetAllNetworkInterfaces()[0];
                Console.WriteLine($"WiFi connected! IP: {ni.IPv4Address}");

                // Get MAC address from hardware
                string macAddress = WiFiHelper.GetMacAddress();
                Console.WriteLine($"MAC: {macAddress}");

                step = 8;
                display.ClearScreen();
                if (EnablePixelText)
                {
                    PixelTextRenderer.DrawText(display, 6, 6, "WIFI OK", 1);
                    PixelTextRenderer.DrawText(display, 6, 20, ni.IPv4Address, 1);
                }
                display.Display();
                Thread.Sleep(2000);

                // ================================================================
                // Register with Game Server
                // ================================================================
                step = 9;
                Console.WriteLine("[STEP 9/10] Creating HTTP client...");

                var httpClient = new GameApiHttpClient(
                    LocalConfig.API_URL,
                    LocalConfig.SSL_NO_VERIFY ? SslVerification.NoVerification : SslVerification.CertificateRequired);

                display.ClearScreen();
                if (EnablePixelText)
                {
                    PixelTextRenderer.DrawText(display, 6, 6, "REGISTERING", 1);
                    PixelTextRenderer.DrawText(display, 6, 20, "FLAG NODE", 1);
                }
                display.Display();

                Console.WriteLine("[STEP 9/10] Registering flag node with server...");
                PlayerSetup setup = httpClient.RegisterFlagNode(macAddress);

                if (setup == null || setup.DeviceId == 0)
                {
                    Console.WriteLine("Failed to register with server!");
                    display.ClearScreen();
                    if (EnablePixelText)
                    {
                        PixelTextRenderer.DrawText(display, 6, 6, "REGISTER", 1);
                        PixelTextRenderer.DrawText(display, 6, 20, "FAILED", 1);
                    }
                    display.Display();
                    Thread.Sleep(Timeout.Infinite);
                }

                byte deviceId = setup.DeviceId;
                Console.WriteLine($"Registered! Device ID: {deviceId}");

                step = 10;
                display.ClearScreen();
                if (EnablePixelText)
                {
                    PixelTextRenderer.DrawText(display, 6, 6, "FLAG NODE", 1);
                    PixelTextRenderer.DrawText(display, 6, 20, "ID: " + deviceId, 1);
                    PixelTextRenderer.DrawText(display, 6, 34, "REGISTERED", 1);
                }
                display.Display();
                Thread.Sleep(2000);

                // ================================================================
                // Wait for Game to Start
                // ================================================================
                step = 11;
                Console.WriteLine("[STEP 11/12] Polling for game start...");

                GameInfo gameInfo = null;
                int pollCount = 0;
                bool gameActive = false;

                while (!gameActive)
                {
                    pollCount++;
                    Console.WriteLine($"[POLL {pollCount}] Checking game status...");

                    gameInfo = httpClient.GetCurrentGame();
                    if (gameInfo == null)
                    {
                        Console.WriteLine("Failed to get game info!");
                        display.ClearScreen();
                        if (EnablePixelText)
                        {
                            PixelTextRenderer.DrawText(display, 6, 6, "API ERROR", 1);
                            PixelTextRenderer.DrawText(display, 6, 20, "RETRYING", 1);
                        }
                        display.Display();
                        Thread.Sleep(5000);
                        continue;
                    }

                    string statusText = gameInfo.Status.ToString().ToUpper();
                    Console.WriteLine($"Game Status: {statusText}");

                    display.ClearScreen();
                    if (EnablePixelText)
                    {
                        PixelTextRenderer.DrawText(display, 6, 6, "WAITING", 1);
                        PixelTextRenderer.DrawText(display, 6, 20, "GAME: " + statusText, 1);
                        PixelTextRenderer.DrawText(display, 6, 34, "ID: " + deviceId, 1);
                        PixelTextRenderer.DrawText(display, 6, 48, "POLL: " + pollCount, 1);
                    }
                    display.Display();

                    if (gameInfo.Status == GameStatus.Active)
                    {
                        gameActive = true;
                        Console.WriteLine("Game is ACTIVE!");
                        break;
                    }

                    Thread.Sleep(3000); // Poll every 3 seconds
                }

                step = 12;
                display.ClearScreen();
                if (EnablePixelText)
                {
                    PixelTextRenderer.DrawText(display, 6, 6, "FLAG NODE", 1);
                    PixelTextRenderer.DrawText(display, 6, 20, "GAME ACTIVE", 1);
                    PixelTextRenderer.DrawText(display, 6, 34, "ID: " + deviceId, 1);
                }
                display.Display();
                Thread.Sleep(2000);

                // Find our team from the player roster
                string ourTeam = "UNKNOWN";
                if (gameInfo != null && gameInfo.Players != null)
                {
                    foreach (var player in gameInfo.Players)
                    {
                        if (player.DeviceId == deviceId && !string.IsNullOrEmpty(player.Team))
                        {
                            ourTeam = player.Team;
                            break;
                        }
                    }
                }
                Console.WriteLine($"Team: {ourTeam}");

                // Initialize display manager
                s_displayManager = new FlagNodeDisplayManager(display, DisplayMode.Rotating);

                // ================================================================
                // Main Loop
                // ================================================================
                Console.WriteLine("[STEP 12/12] Entering main loop...");

                // Game stats
                var stats = new FlagNodeStats
                {
                    DeviceId = deviceId,
                    TeamName = ourTeam,
                    MacAddress = macAddress,
                    LastSeenDevice = 0,
                    LastActivity = "IDLE",
                    IdleSeconds = 0,
                    CaptureCount = 0,
                    DeliverCount = 0,
                    UptimeSeconds = 0
                };

                while (true)
                {
                    Thread.Sleep(1000);
                    stats.UptimeSeconds++;
                    stats.IdleSeconds++;

                    // Check for mode change request
                    bool modeChanged = false;
                    lock (s_modeLock)
                    {
                        if (s_modeChangeRequested)
                        {
                            s_displayManager.CycleMode();
                            s_modeChangeRequested = false;
                            modeChanged = true;
                            Console.WriteLine($"[MODE] Changed to {s_displayManager.CurrentMode}");
                        }
                    }

                    Console.WriteLine($"[HEARTBEAT] {stats.UptimeSeconds}s - ID:{deviceId} Team:{ourTeam} Mode:{s_displayManager.CurrentMode} Cap:{stats.CaptureCount} Del:{stats.DeliverCount}");

                    // TODO: Check for LoRa packets and update stats

                    // Show mode change feedback
                    if (modeChanged)
                    {
                        s_displayManager.ShowModeChange();
                        Thread.Sleep(1000);
                    }

                    // Update display every 2 seconds
                    if (stats.UptimeSeconds % 2 == 0)
                    {
                        s_displayManager.Render(stats);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("");
                Console.WriteLine("========================================");
                Console.WriteLine($"EXCEPTION AT STEP {step}:");
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine($"Type: {ex.GetType().Name}");
                if (ex.StackTrace != null)
                {
                    Console.WriteLine($"Stack: {ex.StackTrace}");
                }
                Console.WriteLine("========================================");
                Console.WriteLine("");

                // Still heartbeat to show it's alive
                int counter = 0;
                while (true)
                {
                    Thread.Sleep(1000);
                    counter++;
                    Console.WriteLine($"[ERROR-BEAT] {counter}s - Failed at step {step}");
                }
            }

            Console.WriteLine("Main() reached end - this should NEVER print");
            Thread.Sleep(Timeout.Infinite);
        }

        private static void DrawStartupPattern(Ssd1306 display)
        {
            // Border
            display.DrawHorizontalLine(0, 0, DisplayWidth, true);
            display.DrawHorizontalLine(0, DisplayHeight - 1, DisplayWidth, true);
            display.DrawVerticalLine(0, 0, DisplayHeight, true);
            display.DrawVerticalLine(DisplayWidth - 1, 0, DisplayHeight, true);

            // Crosshair + center marker
            display.DrawHorizontalLine(0, DisplayHeight / 2, DisplayWidth, true);
            display.DrawVerticalLine(DisplayWidth / 2, 0, DisplayHeight, true);
            display.DrawFilledRectangle((DisplayWidth / 2) - 4, (DisplayHeight / 2) - 4, 8, 8, true);
        }

        private static void ShowRxDebug(Ssd1306 display, int rxCount, byte[] payload, int rssi)
        {
            byte from = 0x00;
            byte type = 0x00;

            if (payload != null && payload.Length >= 2)
            {
                from = payload[0];
                type = payload[1];
            }

            display.ClearScreen();
            if (EnablePixelText)
            {
                string typeLabel = GetPacketTypeLabel(type);
                string eventText = GetPacketEventText(type);

                PixelTextRenderer.DrawText(display, 6, 6, eventText, 1);
                PixelTextRenderer.DrawText(display, 6, 18, "COUNT " + rxCount, 1);
                PixelTextRenderer.DrawText(display, 6, 30, "FROM 0X" + from.ToString("X2"), 1);
                PixelTextRenderer.DrawText(display, 6, 42, "TYPE " + typeLabel, 1);
                PixelTextRenderer.DrawText(display, 6, 54, "RSSI " + rssi, 1);
            }
            display.Display();
        }

        private static string GetPacketTypeLabel(byte type)
        {
            switch (type)
            {
                case PacketType.Heartbeat:
                    return "HB 01";
                case PacketType.Attack:
                    return "ATK 02";
                case PacketType.AttackAck:
                    return "AACK 03";
                case PacketType.FlagTransfer:
                    return "XFER 04";
                case PacketType.Capture:
                    return "CAP 05";
                case PacketType.KeyGrant:
                    return "KGRANT 06";
                case PacketType.Deliver:
                    return "DELIV 07";
                case PacketType.RespawnReq:
                    return "RREQ 08";
                case PacketType.RespawnAck:
                    return "RACK 09";
                case PacketType.GameStart:
                    return "GSTART 0A";
                case PacketType.GameEnd:
                    return "GEND 0B";
                case PacketType.CombatResult:
                    return "CRES 0C";
                default:
                    return "UNK " + type.ToString("X2");
            }
        }

        private static string GetPacketEventText(byte type)
        {
            switch (type)
            {
                case PacketType.Heartbeat:
                    return "HEARTBEAT SEEN";
                case PacketType.Attack:
                    return "ATTACK RX";
                case PacketType.AttackAck:
                    return "ATTACK ACK";
                case PacketType.FlagTransfer:
                    return "FLAG XFER";
                case PacketType.Capture:
                    return "CAPTURE RX";
                case PacketType.KeyGrant:
                    return "KEY GRANT RX";
                case PacketType.Deliver:
                    return "DELIVER RX";
                case PacketType.RespawnReq:
                    return "RESPAWN REQ";
                case PacketType.RespawnAck:
                    return "RESPAWN ACK";
                case PacketType.GameStart:
                    return "GAME STARTED";
                case PacketType.GameEnd:
                    return "GAME ENDED";
                case PacketType.CombatResult:
                    return "COMBAT RESULT";
                default:
                    return "UNKNOWN PACKET";
            }
        }

        private static void OnPrgButtonPressed(object sender, PinValueChangedEventArgs e)
        {
            // Button pressed (LOW due to pull-up)
            if (e.ChangeType == PinEventTypes.Falling)
            {
                lock (s_modeLock)
                {
                    s_modeChangeRequested = true;
                }
                Console.WriteLine("[BUTTON] PRG pressed - mode change requested");
            }
        }
    }
}
