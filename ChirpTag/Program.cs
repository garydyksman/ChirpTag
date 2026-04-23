using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Implementations;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;
using Iot.Device.EPaper;
using Iot.Device.EPaper.Drivers.LcmEn2r13;
using Iot.Device.EPaper.Enums;
using Iot.Device.EPaper.Fonts;
using Iot.Device.LoRa.Drivers.Sx1262;
using nanoFramework.Hardware.Esp32;
using System;
using System.Device.Gpio;
using System.Net.Security;
using System.Device.Spi;
using System.Threading;

namespace ChirpTag
{
    public class Program
    {
        // ---- Display pin mapping (HT-VME213) � SPI2 ----
        private const int PinDisplayMosi = 6;
        private const int PinDisplayClk = 4;
        private const int PinDisplayCs = 5;
        private const int PinDisplayDc = 2;
        private const int PinDisplayRst = 3;
        private const int PinDisplayBusy = 1;
        private const int PinVext = 18;

        // ---- SX1262 pin mapping (HT-VME213) � SPI1 ----
        private const int PinLoraMosi = 10;
        private const int PinLoraClk = 9;
        private const int PinLoraMiso = 11;
        private const int PinLoraCs = 8;
        private const int PinLoraRst = 12;
        private const int PinLoraBusy = 13;
        private const int PinLoraDio1 = 14;

        // ---- buttons ----
        private const int PinUserButton = 21;   // PRG button, active low
        private const int PinBootButton = 0;   // BOOT button, active low

        private static IGamerDevice _device;
        private static LcmEn2r13 _display;
        private static bool _attackRequested;
        private static bool _cycleTargetsRequested;

        private static void Log(string message)
        {
            DebugLog.Write(message);
        }

        private static void LogPhase1Status(GameStatus s)
        {
            if (s == GameStatus.None)
            {
                Log("[Program] Phase 1 status None");
            }
            else if (s == GameStatus.Waiting)
            {
                Log("[Program] Phase 1 status Waiting");
            }
            else if (s == GameStatus.Active)
            {
                Log("[Program] Phase 1 status Active");
            }
            else
            {
                Log("[Program] Phase 1 status Ended");
            }
        }

        private static void LogPhase2Status(GameStatus s)
        {
            if (s == GameStatus.None)
            {
                Log("[Program] Phase 2 status None");
            }
            else if (s == GameStatus.Waiting)
            {
                Log("[Program] Phase 2 status Waiting");
            }
            else if (s == GameStatus.Active)
            {
                Log("[Program] Phase 2 status Active");
            }
            else
            {
                Log("[Program] Phase 2 status Ended");
            }
        }

        /// <summary>Mid-game reboot: use roster from <see cref="GameInfo.Players"/> when API already lists us as Active.</summary>
        private static bool TryGetDeviceIdForPlayerName(PlayerInfo[] players, string playerName, out byte deviceId)
        {
            deviceId = 0;
            if (players == null || playerName == null || playerName.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < players.Length; i++)
            {
                PlayerInfo p = players[i];
                if (p != null && p.Name != null && p.Name == playerName)
                {
                    deviceId = p.DeviceId;
                    return true;
                }
            }

            return false;
        }

        public static void Main()
        {
            Log("[Program] Main start");
            ChirpTagConfigLoader.TryLoad();
            Log("[Program] Create GPIO controller");
            var gpio = new GpioController();

            // ---- VEXT ----
            Log("[Program] Configure VEXT");
            gpio.OpenPin(PinVext, PinMode.Output);
            gpio.Write(PinVext, PinValue.High);
            Thread.Sleep(100);
            Log("VEXT on");

            // Free GPIO1 for E-Ink BUSY by moving COM1 to the board UART pins.
            Configuration.SetPinFunction(43, DeviceFunction.COM1_TX);
            Configuration.SetPinFunction(44, DeviceFunction.COM1_RX);

            // Heap staging: display + font first (no LoRa, no Wi-Fi yet). LoRa starts only after HTTP setup
            // so Wi-Fi and SX1262 are never both active.

            // ---- Display ----
            Log("[Program] Configure display pins");
            Configuration.SetPinFunction(PinDisplayMosi, DeviceFunction.SPI2_MOSI);
            Configuration.SetPinFunction(PinDisplayClk, DeviceFunction.SPI2_CLOCK);

            var displaySpi = SpiDevice.Create(new SpiConnectionSettings(2, PinDisplayCs)
            {
                ClockFrequency = LcmEn2r13.SpiClockFrequency,
                Mode = LcmEn2r13.SpiMode,
                ChipSelectLineActiveState = false,
                Configuration = SpiBusConfiguration.HalfDuplex,
                DataFlow = DataFlow.MsbFirst
            });

            Log("[Program] Create display driver instance");
            _display = new LcmEn2r13(
                displaySpi,
                resetPin: PinDisplayRst,
                busyPin: PinDisplayBusy,
                dataCommandPin: PinDisplayDc,
                gpioController: gpio,
                shouldDispose: false);

            Log("[Program] Display PowerOn");
            _display.PowerOn();
            Log("[Program] Display Clear");
            _display.Clear(false);

            var font = new Font8x12();
            var gfx = new Graphics(_display)
            {
                DisplayRotation = Rotation.Degrees90Clockwise,
                FlipGlyphsHorizontally = true
            };

            // ---- Init Display Driver ----
            Log("[Program] Create game display driver");
            var driver = new DisplayDriver(_display, gfx, font);

            string gameApiBaseUrl = ChirpTagSettings.GameApiBaseUrl == null ? string.Empty : ChirpTagSettings.GameApiBaseUrl.Trim();
            if (gameApiBaseUrl.Length == 0)
            {
                driver.ShowMessage("Missing config", "gameApiBaseUrl");
                Log("[Program] halt: set gameApiBaseUrl in AppSettingsBuiltIn / appsettings.json or " + ChirpTagConfigLoader.DefaultConfigPath);
                while (true)
                {
                    Thread.Sleep(60_000);
                }
            }

            // ---- Wi-Fi (HTTP API; torn down before LoRa) ----
            WifiBootOutcome wifiBoot = WifiBootOutcome.Skipped;
            if (WiFiBootstrap.IsConfigured)
            {
                driver.ShowMessage("WiFi", "connecting...");
                wifiBoot = WiFiBootstrap.RunForHttpSetup();
            }

            if (wifiBoot == WifiBootOutcome.Failed)
            {
                driver.ShowMessage("WiFi failed", "cannot continue");
                Log("[Program] halt: WiFi configured but not connected");
                while (true)
                {
                    Thread.Sleep(60_000);
                }
            }

            if (wifiBoot == WifiBootOutcome.Connected)
            {
                driver.ShowMessage("WiFi", "OK");
            }

            // ---- HTTP game client ----
            Log("[Program] Create game HTTP client");
            SslVerification ssl = ChirpTagSettings.GameApiSslNoVerify
                ? SslVerification.NoVerification
                : SslVerification.CertificateRequired;
            IGameHttpClient http = new GameApiHttpClient(gameApiBaseUrl, ssl);
            string apiPlayerName = GameApiHttpClient.NormalizePlayerName(ChirpTagSettings.PlayerName);

            // ---- User button ----
            Log("[Program] Configure user button");
            var buttonPin = gpio.OpenPin(PinUserButton, PinMode.InputPullUp);
            buttonPin.DebounceTimeout = TimeSpan.FromMilliseconds(50);
            buttonPin.ValueChanged += OnButtonChanged;

            // ---- Boot button ----
            Log("[Program] Configure boot button");
            var bootButtonPin = gpio.OpenPin(PinBootButton, PinMode.InputPullUp);
            bootButtonPin.DebounceTimeout = TimeSpan.FromMilliseconds(50);
            bootButtonPin.ValueChanged += BootButtonPin_ValueChanged;

            // -------------------------------------------------------
            // Phase 1 � wait for a game to be created on the server
            // -------------------------------------------------------
            Log("[Program] Phase 1 show connecting / looking for game");
            driver.ShowMessage("Connecting to the server", "looking for a game");
            while (true)
            {
                Log("[Program] Phase 1 GetCurrentGame");
                GameInfo game = http.GetCurrentGame();
                LogPhase1Status(game.Status);
                if (game.Status != GameStatus.None)
                {
                    break;
                }

                Thread.Sleep(1_000);
            }

            // -------------------------------------------------------
            // Phase 2 � register (or rejoin from roster) + wait for Active
            // -------------------------------------------------------
            GameInfo phase2Snapshot = http.GetCurrentGame();
            PlayerSetup setup;
            if (phase2Snapshot.Status == GameStatus.Active
                && TryGetDeviceIdForPlayerName(phase2Snapshot.Players, apiPlayerName, out byte rosterId))
            {
                setup = new PlayerSetup { DeviceId = rosterId };
                Log("[Program] Phase 2 reconnect from roster deviceId=" + rosterId.ToString());
                driver.ShowMessage("Rejoining", "game...");
            }
            else
            {
                Log("[Program] Phase 2 Register");
                setup = http.Register(apiPlayerName);
                Log("[Program] Phase 2 registered deviceId=" + setup.DeviceId.ToString());
                if (setup.DeviceId == 0)
                {
                    driver.ShowMessage("Register failed", "name or game?");
                }
                else
                {
                    driver.ShowMessage("Waiting for", "players...");
                }
            }

            GameInfo active = null;
            while (true)
            {
                Log("[Program] Phase 2 GetCurrentGame");
                GameInfo game = http.GetCurrentGame();
                LogPhase2Status(game.Status);
                if (game.Status == GameStatus.Active)
                {
                    active = game;
                    break;
                }

                if (game.Status == GameStatus.None)
                {
                    Log("[Program] Phase 2 game cancelled, show connecting / looking for game");
                    driver.ShowMessage("Connecting to the server", "looking for a game");
                }

                Thread.Sleep(1_000);
            }

            if (setup.DeviceId == 0
                && TryGetDeviceIdForPlayerName(active.Players, apiPlayerName, out byte recoveredId))
            {
                setup = new PlayerSetup { DeviceId = recoveredId };
                Log("[Program] Phase 2 deviceId from Active roster=" + recoveredId.ToString());
            }

            if (setup.DeviceId == 0)
            {
                if (!ChirpTagSettings.AllowHudWithoutValidDeviceId)
                {
                    driver.ShowMessage("Cannot join", "game running");
                    Log("[Program] halt: no deviceId (register failed or name not in game — use same name or wait for next game)");
                    while (true)
                    {
                        Thread.Sleep(60_000);
                    }
                }

                Log("[Program] AllowHudWithoutValidDeviceId (config): continuing with deviceId=0 (test only)");
                driver.ShowMessage("TEST", "no device id");
            }

            // -------------------------------------------------------
            // Phase 3 � apply config and start
            // -------------------------------------------------------
            WiFiBootstrap.TearDownRadio();

            Log("[Program] Configure LoRa pins");
            Configuration.SetPinFunction(PinLoraMosi, DeviceFunction.SPI1_MOSI);
            Configuration.SetPinFunction(PinLoraClk, DeviceFunction.SPI1_CLOCK);
            Configuration.SetPinFunction(PinLoraMiso, DeviceFunction.SPI1_MISO);

            var loraSpi = SpiDevice.Create(new SpiConnectionSettings(1, PinLoraCs)
            {
                ClockFrequency = 1000000,
                Mode = SpiMode.Mode0,
                DataBitLength = 8
            });

            Log("[Program] Create LoRa driver instance");
            var lora = new Sx1262(
                loraSpi,
                resetPin: PinLoraRst,
                busyPin: PinLoraBusy,
                dio1Pin: PinLoraDio1,
                gpioController: gpio,
                shouldDispose: false);

            Log("[Program] LoRa Reset");
            lora.Reset();
            Log("[Program] LoRa Initialise");
            lora.Initialize();

            Log("[Program] Phase 3 create GameStateManager");
            var state = new GameStateManager(deviceId: setup.DeviceId, playerName: apiPlayerName);
            Log("[Program] Phase 3 ApplyPlayerList");
            state.ApplyPlayerList(active.Players);
            if (CombatListDiagnostics.Enabled && active.Players != null)
            {
                CombatListDiagnostics.Write(
                    "[Program] Phase3 HTTP Active roster length=" + active.Players.Length.ToString());
            }

            Log("[Program] Phase 3 SetState Active");
            state.SetState(GameState.Active);

            var builder = new PacketBuilder();
            var handler = new MessageHandler(new PacketParser(), state.DeviceId);
            Log("[Program] Phase 3 create GamerDevice");
            var wifiBridge = new WifiHttpBridge();
            _device = new GamerDevice(
                state,
                driver,
                lora,
                builder,
                handler,
                http,
                wifiBridge,
                () => lora.StopPolling(),
                () => lora.StartPolling());

            // Initial HUD
            Log("[Program] Initial OnGameStart");
            _device.OnGameStart();
            Log("[Program] Enter main input loop");

            while (true)
            {
                if (_attackRequested)
                {
                    _attackRequested = false;
                    Log("Processing combat button");
                    _device.OnCombatButtonPressed();
                }

                if (_cycleTargetsRequested)
                {
                    _cycleTargetsRequested = false;
                    Log("Processing cycle-targets button");
                    _device.OnCycleTargetsButtonPressed();
                }

                Thread.Sleep(25);
            }
        }

        private static void BootButtonPin_ValueChanged(object sender, PinValueChangedEventArgs args)
        {
            if (args.ChangeType == PinEventTypes.Rising)
            {
                Log("Boot button pressed, queueing target cycle");
                _cycleTargetsRequested = true;
            }
        }

        // ISR � keep it minimal, no SPI, no blocking
        private static void OnButtonChanged(object sender, PinValueChangedEventArgs args)
        {
            if (args.ChangeType == PinEventTypes.Rising)
            {
                Log("User button pressed, queueing attack");
                _attackRequested = true;
            }
        }
    }
}

