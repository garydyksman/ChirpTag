using Iot.Device.EPaper;
using Iot.Device.EPaper.Drivers.Jd796xx.LcmEn2r13;
using Iot.Device.EPaper.Enums;
using Iot.Device.EPaper.Fonts;
using Iot.Device.LoRa;
using Iot.Device.LoRa.Drivers.Sx1262;
using nanoFramework.Hardware.Esp32;
using System;
using System.Device.Gpio;
using System.Device.Spi;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Threading;

namespace ChirpTag
{
    public class Program
    {
        // ---- Display pin mapping (HT-VME213) — SPI2 ----
        private const int PinDisplayMosi = 6;
        private const int PinDisplayClk = 4;
        private const int PinDisplayMiso = 7;
        private const int PinDisplayCs = 5;
        private const int PinDisplayDc = 2;
        private const int PinDisplayRst = 3;
        private const int PinDisplayBusy = 1;
        private const int PinVext = 18;

        // ---- SX1262 pin mapping (HT-VME213) — SPI1 ----
        private const int PinLoraMosi = 10;
        private const int PinLoraClk = 9;
        private const int PinLoraMiso = 11;
        private const int PinLoraCs = 8;
        private const int PinLoraRst = 12;
        private const int PinLoraBusy = 13;
        private const int PinLoraDio1 = 14;

        // ---- User button ----
        private const int PinUserButton = 21;   // PRG button, active low

        // ---- Shared state ----
        private static LcmEn2r13 _display;
        private static Graphics _gfx;
        private static Font8x12 _font;
        private static Sx1262 _lora;

        private static string _lastRx = "No RX yet";
        private static int _txCount = 0;
        private static string _statusMsg = "Ready";

        // Set by the button ISR, consumed by the main thread
        private static bool _sendRequested = false;

        public static void Main()
        {
            var gpio = new GpioController();

            // ---- VEXT ----
            gpio.OpenPin(PinVext, PinMode.Output);
            gpio.Write(PinVext, PinValue.High);
            Thread.Sleep(100);
            Debug.WriteLine("VEXT on");

            // ---- Display ----
            Configuration.SetPinFunction(PinDisplayMosi, DeviceFunction.SPI2_MOSI);
            Configuration.SetPinFunction(PinDisplayClk, DeviceFunction.SPI2_CLOCK);
            Configuration.SetPinFunction(PinDisplayMiso, DeviceFunction.SPI2_MISO);

            var displaySpi = SpiDevice.Create(new SpiConnectionSettings(2, PinDisplayCs)
            {
                ClockFrequency = LcmEn2r13.SpiClockFrequency,
                Mode = LcmEn2r13.SpiMode,
                ChipSelectLineActiveState = false,
                Configuration = SpiBusConfiguration.HalfDuplex,
                DataFlow = DataFlow.MsbFirst
            });

            _display = new LcmEn2r13(
                displaySpi,
                resetPin: PinDisplayRst,
                busyPin: PinDisplayBusy,
                dataCommandPin: PinDisplayDc,
                gpioController: gpio,
                shouldDispose: false);

            _display.PowerOn();
            _display.Clear(triggerPageRefresh: true);

            _font = new Font8x12();
            _gfx = new Graphics(_display)
            {
                DisplayRotation = Rotation.Degrees90Clockwise,
                FlipGlyphsHorizontally = true
            };

            // ---- LoRa ----
            Configuration.SetPinFunction(PinLoraMosi, DeviceFunction.SPI1_MOSI);
            Configuration.SetPinFunction(PinLoraClk, DeviceFunction.SPI1_CLOCK);
            Configuration.SetPinFunction(PinLoraMiso, DeviceFunction.SPI1_MISO);

            var loraSpi = SpiDevice.Create(new SpiConnectionSettings(1, PinLoraCs)
            {
                ClockFrequency = 1000000,
                Mode = SpiMode.Mode0,
                DataBitLength = 8
            });

            _lora = new Sx1262(
                loraSpi,
                resetPin: PinLoraRst,
                busyPin: PinLoraBusy,
                dio1Pin: PinLoraDio1,
                gpioController: gpio,
                shouldDispose: false);

            _lora.Reset();
            _lora.Initialise();

            _lora.PacketReceived += OnPacketReceived;
            _lora.StartPolling();

            // ---- User button ----
            var buttonPin = gpio.OpenPin(PinUserButton, PinMode.InputPullUp);
            buttonPin.DebounceTimeout = TimeSpan.FromMilliseconds(50);
            buttonPin.ValueChanged += OnButtonChanged;

            // ---- Initial draw ----
            Redraw();

            // ---- Main loop — handles TX on the main thread ----
            while (true)
            {
                if (_sendRequested)
                {
                    _sendRequested = false;
                    DoSend();
                }
                Thread.Sleep(20);
            }
        }

        // ISR — keep it minimal, no SPI, no blocking
        private static void OnButtonChanged(object sender, PinValueChangedEventArgs args)
        {
            if (args.ChangeType == PinEventTypes.Rising)
                _sendRequested = true;
        }

        // Called from main thread only
        private static void DoSend()
        {
            try
            {
                _txCount++;
                byte[] payload = Encoding.UTF8.GetBytes("CHIRP " + _txCount);
                _statusMsg = "TX #" + _txCount + "...";
                Redraw();

                _lora.Send(payload, 3000);

                _statusMsg = "TX OK #" + _txCount;
                Debug.WriteLine("TX OK #" + _txCount);
            }
            catch (Exception ex)
            {
                _statusMsg = "TX FAIL";
                Debug.WriteLine("TX failed: " + ex.Message);
            }

            Redraw();
        }

        private static void OnPacketReceived(object sender, LoRaMessage msg)
        {
            string text = Encoding.UTF8.GetString(msg.Payload, 0, msg.Payload.Length);
            _lastRx = text + " (" + msg.Rssi + "dBm)";
            Debug.WriteLine("RX: '" + text + "' RSSI=" + msg.Rssi + "dBm SNR=" + msg.Snr + "dB");
            Redraw();
        }

        private static void Redraw()
        {
            byte status = _lora.GetStatus();
            _display.BeginFrameDraw();
            _gfx.DrawText("ChirpTag", _font, 4, 8, Color.Black);
            _gfx.DrawText("Mode: " + Sx1262.DecodeChipMode(status), _font, 4, 26, Color.Black);
            _gfx.DrawText(_statusMsg, _font, 4, 44, Color.Black);
            _gfx.DrawText(_lastRx, _font, 4, 62, Color.Black);
            _display.EndFrameDraw();
            _display.PerformFullRefresh();
        }
    }
}
