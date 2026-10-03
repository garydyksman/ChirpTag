using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using System.Device.Wifi;
using System.Net.Security;
using System.Net.NetworkInformation;
using System.Threading;
using Iot.Device.LoRa;
using Iot.Device.LoRa.Drivers.Sx1262;
using Iot.Device.Ssd13xx;
using Iot.Device.Ssd13xx.Commands;
using nanoFramework.Hardware.Esp32;
using nanoFramework.Networking;
using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Implementations;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
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
        private const int VextPin = 36;  // Active HIGH on V4 — must be set before OLED init

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

                // Enable Vext power rail — OLED is unpowered until this is HIGH on V4
                gpio.OpenPin(VextPin, PinMode.Output);
                gpio.Write(VextPin, PinValue.High);
                Thread.Sleep(100);

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

                var i2c = I2cDevice.Create(new I2cConnectionSettings(I2cBus, Ssd1306.DefaultI2cAddress));

                step = 5;
                Console.WriteLine("[STEP 5/7] I2C device created");
                Thread.Sleep(100);

                var display = new Ssd1306(i2c, Ssd13xx.DisplayResolution.OLED128x64);
                display.Font = new BasicFont();
                display.SendCommand(new SetContrastControlForBank0(255));

                step = 6;
                Console.WriteLine("[STEP 6/7] Display created");
                Thread.Sleep(100);

                display.ClearScreen();
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
                display.DrawString(6, 6, "FLAG NODE", 1);
                display.DrawString(6, 20, "WIFI...", 1);
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
                    display.DrawString(6, 6, "WIFI", 1);
                    display.DrawString(6, 20, "FAILED", 1);
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
                display.DrawString(6, 6, "WIFI OK", 1);
                display.DrawString(6, 20, ni.IPv4Address, 1);
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
                display.DrawString(6, 6, "REGISTERING", 1);
                display.DrawString(6, 20, "FLAG NODE", 1);
                display.Display();

                Console.WriteLine("[STEP 9/10] Registering flag node with server...");
                PlayerSetup setup = httpClient.RegisterFlagNode(macAddress);

                if (setup == null || setup.DeviceId == 0)
                {
                    Console.WriteLine("Failed to register with server!");
                    display.ClearScreen();
                    display.DrawString(6, 6, "REGISTER", 1);
                    display.DrawString(6, 20, "FAILED", 1);
                    display.Display();
                    Thread.Sleep(Timeout.Infinite);
                }

                byte deviceId = setup.DeviceId;
                Console.WriteLine("Registered! Device ID: " + deviceId);

                step = 10;
                display.ClearScreen();
                display.DrawString(6, 6, "FLAG NODE", 1);
                display.DrawString(6, 20, "ID: " + deviceId, 1);
                display.DrawString(6, 34, "REGISTERED", 1);
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
                        display.DrawString(6, 6, "API ERROR", 1);
                        display.DrawString(6, 20, "RETRYING", 1);
                        display.Display();
                        Thread.Sleep(5000);
                        continue;
                    }

                    Console.WriteLine("Game Status: " + gameInfo.Status);

                    display.ClearScreen();
                    display.DrawString(6, 6, "WAITING", 1);
                    display.DrawString(6, 20, "ID: " + deviceId, 1);
                    display.DrawString(6, 34, "POLL: " + pollCount, 1);
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
                display.DrawString(6, 6, "FLAG NODE", 1);
                display.DrawString(6, 20, "GAME ACTIVE", 1);
                display.DrawString(6, 34, "ID: " + deviceId, 1);
                display.Display();
                Thread.Sleep(2000);

                // Find our team from the player roster
                string ourTeam = "UNKNOWN";
                if (gameInfo != null && gameInfo.Players != null)
                {
                    for (int pi = 0; pi < gameInfo.Players.Length; pi++)
                    {
                        var player = gameInfo.Players[pi];
                        if (player != null && player.DeviceId == deviceId && player.Team != null && player.Team.Length > 0)
                        {
                            ourTeam = player.Team;
                            break;
                        }
                    }
                }
                Console.WriteLine($"Team: {ourTeam}");

                // ================================================================
                // Phase 3: Tear down WiFi, init LoRa, start game
                // ================================================================
                step = 12;
                Console.WriteLine("[STEP 12/12] Tearing down WiFi for LoRa...");
                display.ClearScreen();
                display.DrawString(6, 6, "FLAG NODE", 1);
                display.DrawString(6, 20, "STARTING...", 1);
                display.Display();

                // Tear down WiFi so LoRa can start
                WifiAdapter[] wifiAdapters = WifiAdapter.FindAllAdapters();
                if (wifiAdapters != null)
                {
                    for (int wi = 0; wi < wifiAdapters.Length; wi++)
                    {
                        if (wifiAdapters[wi] != null)
                        {
                            try { wifiAdapters[wi].Disconnect(); } catch { }
                        }
                    }
                }
                Thread.Sleep(500);

                // Init LoRa SPI
                Console.WriteLine("Mapping LoRa SPI pins...");
                Configuration.SetPinFunction(PinLoraMosi, DeviceFunction.SPI1_MOSI);
                Configuration.SetPinFunction(PinLoraClk, DeviceFunction.SPI1_CLOCK);
                Configuration.SetPinFunction(PinLoraMiso, DeviceFunction.SPI1_MISO);

                var loraSpi = SpiDevice.Create(new SpiConnectionSettings(1, PinLoraCs)
                {
                    ClockFrequency = 1_000_000,
                    Mode = SpiMode.Mode0,
                    DataBitLength = 8
                });

                var lora = new Sx1262(
                    loraSpi,
                    resetPin: PinLoraRst,
                    busyPin: PinLoraBusy,
                    dio1Pin: PinLoraDio1,
                    gpioController: gpio,
                    shouldDispose: false);

                lora.Reset();
                lora.Initialize();
                Console.WriteLine("LoRa initialized");

                // Create game device with WiFi bridge and LoRa pause/resume callbacks
                var wifiBridge = new FlagNodeWifiHttpBridge();
                var flagNode = new FlagNodeDevice(
                    deviceId,
                    lora,
                    display,
                    httpClient,
                    wifiBridge,
                    () => lora.StopPolling(),
                    () => lora.StartPolling(),
                    gameInfo != null ? gameInfo.GameId : (byte)0);

                // Wire LoRa packet received event BEFORE StartPolling
                lora.PacketReceived += (object s, LoRaMessage msg) =>
                    flagNode.OnLoRaPacketReceived(msg.Payload, msg.Rssi, msg.Snr);

                // OnGameStart fetches key (which will reconnect WiFi, get key, tear down WiFi, start LoRa polling)
                flagNode.OnGameStart();

                // Initialize display manager
                s_displayManager = new FlagNodeDisplayManager(display, DisplayMode.Rotating);

                // ================================================================
                // Main Loop
                // ================================================================
                Console.WriteLine("Entering main loop...");

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

                const int GameEndCheckIntervalS = 30;
                int gameEndCheckCounter = 0;

                while (true)
                {
                    Thread.Sleep(1000);
                    stats.UptimeSeconds++;
                    stats.IdleSeconds++;
                    gameEndCheckCounter++;

                    // Check for mode change request from PRG button
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

                    Console.WriteLine($"[BEAT] {stats.UptimeSeconds}s ID:{deviceId} Team:{ourTeam}");

                    // Poll server for game end every 30 seconds
                    if (gameEndCheckCounter >= GameEndCheckIntervalS)
                    {
                        gameEndCheckCounter = 0;
                        Console.WriteLine("[GAME-END] Checking game status...");
                        lora.StopPolling();
                        WifiHttpBootOutcome wifiOutcome = wifiBridge.EnableForHttp();
                        if (wifiOutcome == WifiHttpBootOutcome.Connected)
                        {
                            try
                            {
                                GameInfo endInfo = httpClient.GetCurrentGame();
                                if (endInfo != null && endInfo.Status == GameStatus.Ended)
                                {
                                    Console.WriteLine($"[GAME-END] Game ended! Winner: {endInfo.WinnerId}");
                                    wifiBridge.TearDownRadio();
                                    // Broadcast GameEnd via LoRa so game devices learn the game is over
                                    lora.StartPolling();
                                    var builder = new IOD.CaptureTheFlag.NanoFramework.Implementations.PacketBuilder();
                                    byte winnerId = endInfo.WinnerId;
                                    byte[] gameEndBytes = builder.GameEnd(deviceId, winnerId, endInfo.GameId).ToBytes();
                                    for (int i = 0; i < 5; i++)
                                    {
                                        Console.WriteLine($"[GAME-END] Broadcasting GameEnd via LoRa ({i + 1}/5)");
                                        try { lora.Send(gameEndBytes, timeoutMs: 3000); } catch { }
                                        Thread.Sleep(2_000);
                                    }
                                    flagNode.OnGameEnd(winnerId, endInfo.GameId);
                                    while (true) { Thread.Sleep(60_000); }
                                }
                            }
                            catch (Exception pollEx)
                            {
                                Console.WriteLine("[GAME-END] Poll error: " + pollEx.Message);
                            }

                            wifiBridge.TearDownRadio();
                        }

                        lora.StartPolling();
                    }

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
