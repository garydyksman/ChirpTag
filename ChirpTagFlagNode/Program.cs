using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using System.Device.Wifi;
using System.Net.Security;
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
        private const int VextPin = 36;  // Active HIGH on V4

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

        private const int GameEndCheckIntervalS = 30;
        private const int GameRestartCooldownS = 25;

        private static FlagNodeDisplayManager s_displayManager;
        private static bool s_modeChangeRequested = false;
        private static readonly object s_modeLock = new object();
        private static readonly object s_displayLock = new object();

        // Routes LoRa packets to whichever FlagNodeDevice is currently active.
        // Updated at the start of each game cycle.
        private static FlagNodeDevice s_currentFlagNode;

        public static void Main()
        {
            Thread.Sleep(2000);
            Console.WriteLine("[FLAG NODE] Booting...");

            // ================================================================
            // ONE-TIME HARDWARE SETUP
            // ================================================================

            var gpio = new GpioController();
            var prgButton = gpio.OpenPin(PrgButtonPin, PinMode.InputPullUp);
            prgButton.ValueChanged += OnPrgButtonPressed;

            gpio.OpenPin(VextPin, PinMode.Output);
            gpio.Write(VextPin, PinValue.High);
            Thread.Sleep(100);

            // Hardware reset OLED
            var rst = gpio.OpenPin(OledRstPin, PinMode.Output);
            rst.Write(PinValue.Low);
            Thread.Sleep(10);
            rst.Write(PinValue.High);
            Thread.Sleep(10);

            // I2C + OLED
            Configuration.SetPinFunction(SdaPin, DeviceFunction.I2C1_DATA);
            Configuration.SetPinFunction(SclPin, DeviceFunction.I2C1_CLOCK);
            var i2c = I2cDevice.Create(new I2cConnectionSettings(I2cBus, Ssd1306.DefaultI2cAddress));
            var display = new Ssd1306(i2c, Ssd13xx.DisplayResolution.OLED128x64);
            display.Font = new BasicFont();
            display.SendCommand(new SetContrastControlForBank0(255));
            display.ClearScreen();
            display.Display();

            // LoRa SPI (hardware wiring — done once)
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

            // Wire LoRa event once — delegates to the active flag node each game
            lora.PacketReceived += (object s, LoRaMessage msg) =>
                s_currentFlagNode?.OnLoRaPacketReceived(msg.Payload, msg.Rssi, msg.Snr);

            // WiFi bridge and HTTP client are reused across all game cycles
            var wifiBridge = new FlagNodeWifiHttpBridge();
            SslVerification ssl = LocalConfig.SSL_NO_VERIFY
                ? SslVerification.NoVerification
                : SslVerification.CertificateRequired;
            var httpClient = new GameApiHttpClient(LocalConfig.API_URL, ssl);
            string macAddress = null;

            // ================================================================
            // GAME LIFECYCLE LOOP — runs forever; restarts after each game
            // ================================================================
            while (true)
            {
                try
                {
                    // ---- Connect WiFi ----
                    ShowOled(display, "FLAG NODE", "Connecting...");
                    lora.StopPolling();
                    WifiHttpBootOutcome wifiOutcome = wifiBridge.EnableForHttp();
                    if (wifiOutcome != WifiHttpBootOutcome.Connected)
                    {
                        ShowOled(display, "WIFI FAILED", "Retrying 5s...");
                        Console.WriteLine("[FLAG NODE] WiFi failed, retrying...");
                        Thread.Sleep(5_000);
                        continue;
                    }

                    if (macAddress == null)
                        macAddress = WiFiHelper.GetMacAddress();
                    Console.WriteLine("[FLAG NODE] WiFi OK  MAC: " + macAddress);

                    // ---- Register ----
                    ShowOled(display, "REGISTERING", "FLAG NODE");
                    PlayerSetup setup = httpClient.RegisterFlagNode(macAddress);
                    if (setup == null || setup.DeviceId == 0)
                    {
                        ShowOled(display, "REGISTER", "FAILED");
                        Console.WriteLine("[FLAG NODE] Registration failed, retrying...");
                        wifiBridge.TearDownRadio();
                        Thread.Sleep(5_000);
                        continue;
                    }

                    byte deviceId = setup.DeviceId;
                    Console.WriteLine("[FLAG NODE] Registered. ID: " + deviceId);
                    ShowOled(display, "FLAG NODE", "ID: " + deviceId);
                    Thread.Sleep(2_000);

                    // ---- Poll for game active ----
                    GameInfo gameInfo = null;
                    int pollCount = 0;
                    while (true)
                    {
                        pollCount++;
                        gameInfo = httpClient.GetCurrentGame();
                        Console.WriteLine("[POLL " + pollCount + "] Status: " + (gameInfo != null ? gameInfo.Status.ToString() : "null"));

                        if (gameInfo != null && gameInfo.Status == GameStatus.Active)
                            break;

                        ShowOled(display, "WAITING", "POLL: " + pollCount);
                        Thread.Sleep(3_000);
                    }

                    // Find our team from roster
                    string ourTeam = "UNKNOWN";
                    if (gameInfo.Players != null)
                    {
                        for (int pi = 0; pi < gameInfo.Players.Length; pi++)
                        {
                            PlayerInfo p = gameInfo.Players[pi];
                            if (p != null && p.DeviceId == deviceId && p.Team != null && p.Team.Length > 0)
                            {
                                ourTeam = p.Team;
                                break;
                            }
                        }
                    }
                    Console.WriteLine("[FLAG NODE] Team: " + ourTeam);

                    // ---- Tear down WiFi, init LoRa radio ----
                    wifiBridge.TearDownRadio();
                    ShowOled(display, "FLAG NODE", "STARTING...");
                    Thread.Sleep(500);

                    lora.Reset();
                    lora.Initialize();
                    Console.WriteLine("[FLAG NODE] LoRa ready");

                    // ---- Create FlagNodeDevice for this game ----
                    var flagNode = new FlagNodeDevice(
                        deviceId,
                        lora,
                        display,
                        s_displayLock,
                        httpClient,
                        wifiBridge,
                        () => lora.StopPolling(),
                        () => lora.StartPolling(),
                        gameInfo.GameId);

                    s_currentFlagNode = flagNode;

                    // OnGameStart fetches the key (WiFi on → fetch → WiFi off → LoRa start)
                    flagNode.OnGameStart();

                    s_displayManager = new FlagNodeDisplayManager(display, s_displayLock, DisplayMode.Rotating);
                    var stats = new FlagNodeStats
                    {
                        DeviceId = deviceId,
                        TeamName = ourTeam,
                        MacAddress = macAddress ?? string.Empty,
                        LastSeenDevice = 0,
                        LastActivity = "IDLE",
                        IdleSeconds = 0,
                        CaptureCount = 0,
                        DeliverCount = 0,
                        UptimeSeconds = 0
                    };

                    // ---- GAME LOOP ----
                    int gameEndCheckCounter = 0;
                    while (flagNode.IsRunning)
                    {
                        Thread.Sleep(1_000);
                        stats.UptimeSeconds++;
                        stats.IdleSeconds++;
                        gameEndCheckCounter++;

                        bool modeChanged = false;
                        lock (s_modeLock)
                        {
                            if (s_modeChangeRequested)
                            {
                                s_displayManager.CycleMode();
                                s_modeChangeRequested = false;
                                modeChanged = true;
                                Console.WriteLine("[MODE] Changed to " + s_displayManager.CurrentMode);
                            }
                        }

                        Console.WriteLine("[BEAT] " + stats.UptimeSeconds + "s ID:" + deviceId + " Team:" + ourTeam);

                        // HTTP fallback: check game status every 30 s
                        if (gameEndCheckCounter >= GameEndCheckIntervalS && flagNode.IsRunning)
                        {
                            gameEndCheckCounter = 0;
                            Console.WriteLine("[GAME-END] Checking server...");
                            lora.StopPolling();
                            WifiHttpBootOutcome endWifi = wifiBridge.EnableForHttp();
                            if (endWifi == WifiHttpBootOutcome.Connected)
                            {
                                try
                                {
                                    GameInfo endInfo = httpClient.GetCurrentGame();
                                    if (endInfo != null && endInfo.Status == GameStatus.Ended)
                                    {
                                        Console.WriteLine("[GAME-END] Server: game ended. Winner: " + endInfo.WinnerId);
                                        wifiBridge.TearDownRadio();
                                        lora.StartPolling();
                                        // Route through TxQueue/TxLoop — avoids direct Send() racing the poll thread on the SPI bus
                                        flagNode.OnGameEnd(endInfo.WinnerId, endInfo.GameId);
                                    }
                                    else
                                    {
                                        wifiBridge.TearDownRadio();
                                        lora.StartPolling();
                                    }
                                }
                                catch (Exception pollEx)
                                {
                                    Console.WriteLine("[GAME-END] Poll error: " + pollEx.Message);
                                    wifiBridge.TearDownRadio();
                                    lora.StartPolling();
                                }
                            }
                            else
                            {
                                lora.StartPolling();
                            }
                        }

                        if (modeChanged)
                        {
                            s_displayManager.ShowModeChange();
                            Thread.Sleep(1_000);
                        }

                        if (stats.UptimeSeconds % 2 == 0)
                        {
                            stats.CaptureCount = flagNode.CaptureCount;
                            stats.DeliverCount = flagNode.DeliverCount;
                            s_displayManager.Render(stats);
                        }
                    }

                    // ---- Game over — wait for any in-flight retransmit to drain, then cooldown ----
                    Console.WriteLine("[LIFECYCLE] Game over. Cooldown " + GameRestartCooldownS + "s before next game.");
                    ShowOled(display, "GAME OVER", "Next game " + GameRestartCooldownS + "s");
                    Thread.Sleep(GameRestartCooldownS * 1_000);

                    // Loop back to registration
                    Console.WriteLine("[LIFECYCLE] Restarting game lifecycle...");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[FLAG NODE] Lifecycle error: " + ex.Message);
                    ShowOled(display, "ERROR", "Retry 10s");
                    Thread.Sleep(10_000);
                    // Loop continues — attempt re-registration
                }
            }
        }

        private static void ShowOled(Ssd1306 display, string line1, string line2)
        {
            try
            {
                lock (s_displayLock)
                {
                    display.ClearScreen();
                    display.DrawString(6, 6, line1, 1);
                    display.DrawString(6, 20, line2, 1);
                    display.Display();
                }
            }
            catch { }
        }

        private static void OnPrgButtonPressed(object sender, PinValueChangedEventArgs e)
        {
            if (e.ChangeType == PinEventTypes.Falling)
            {
                lock (s_modeLock)
                {
                    s_modeChangeRequested = true;
                }
            }
        }
    }
}
