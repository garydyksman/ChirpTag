using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Device.Spi;
using System.Threading;
using Iot.Device.Ssd13xx;
using Iot.Device.LoRa.Drivers.Sx1262;
using nanoFramework.Hardware.Esp32;

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

        // Display constants
        private const int DisplayWidth = 128;
        private const int DisplayHeight = 64;
        private const bool EnablePixelText = true;

        public static void Main()
        {
            try
            {
                Console.WriteLine("ChirpTagFlagNode starting...");

                var gpio = new GpioController();

                // ================================================================
                // OLED Display Initialization
                // ================================================================
                Console.WriteLine("Initializing OLED display...");

                // Hardware reset
                var rst = gpio.OpenPin(OledRstPin, PinMode.Output);
                rst.Write(PinValue.Low);
                Thread.Sleep(10);
                rst.Write(PinValue.High);
                Thread.Sleep(10);

                // Map I2C pins
                Configuration.SetPinFunction(SdaPin, DeviceFunction.I2C1_DATA);
                Configuration.SetPinFunction(SclPin, DeviceFunction.I2C1_CLOCK);

                // Init display
                var i2c = I2cDevice.Create(new I2cConnectionSettings(I2cBus, Ssd1306.DefaultI2cAddress));
                var display = new Ssd1306(i2c, Ssd13xx.DisplayResolution.OLED128x64);

                display.ClearScreen();
                DrawStartupPattern(display);

                if (EnablePixelText)
                {
                    PixelTextRenderer.DrawText(display, 6, 6, "FLAG NODE", 1);
                    PixelTextRenderer.DrawText(display, 6, 20, "Initializing", 1);
                    PixelTextRenderer.DrawText(display, 6, 34, "LoRa...", 1);
                }

                display.Display();
                Console.WriteLine("OLED display initialized");

                // ================================================================
                // LoRa Radio Initialization
                // ================================================================
                Console.WriteLine("Initializing LoRa...");

                // Map SPI pins
                Configuration.SetPinFunction(PinLoraMosi, DeviceFunction.SPI1_MOSI);
                Configuration.SetPinFunction(PinLoraClk, DeviceFunction.SPI1_CLOCK);
                Configuration.SetPinFunction(PinLoraMiso, DeviceFunction.SPI1_MISO);

                var loraSpi = SpiDevice.Create(new SpiConnectionSettings(1, PinLoraCs)
                {
                    ClockFrequency = 1_000_000,
                    Mode = SpiMode.Mode0,
                    DataBitLength = 8
                });

                Console.WriteLine("Creating LoRa driver instance...");
                var lora = new Sx1262(
                    loraSpi,
                    resetPin: PinLoraRst,
                    busyPin: PinLoraBusy,
                    dio1Pin: PinLoraDio1,
                    gpioController: gpio,
                    shouldDispose: false);

                Console.WriteLine("Resetting LoRa...");
                lora.Reset();

                Console.WriteLine("Initializing LoRa...");
                lora.Initialize();

                Console.WriteLine("LoRa initialized successfully");

                // Update display
                display.ClearScreen();
                if (EnablePixelText)
                {
                    PixelTextRenderer.DrawText(display, 6, 6, "FLAG NODE", 1);
                    PixelTextRenderer.DrawText(display, 6, 20, "LoRa: OK", 1);
                    PixelTextRenderer.DrawText(display, 6, 34, "Ready", 1);
                }
                display.Display();

                // ================================================================
                // TODO: WiFi + HTTP Client Initialization
                // ================================================================
                // Console.WriteLine("Initializing WiFi...");
                // var wifiBootstrap = new WiFiBootstrap(...);
                // var httpClient = new GameApiHttpClient(...);

                // ================================================================
                // TODO: Create FlagNodeDevice
                // ================================================================
                // Console.WriteLine("Creating FlagNodeDevice...");
                // var flagNode = new FlagNodeDevice(
                //     flagNodeId: 0x10,  // TODO: Get from config
                //     lora: lora,
                //     display: display,
                //     httpClient: httpClient,
                //     wifiHttpBridge: wifiBootstrap);
                //
                // flagNode.FetchKeyFromServer();
                // flagNode.Start();

                // ================================================================
                // Debug Loop
                // ================================================================
                Console.WriteLine("Entering debug loop...");
                var tick = 0;
                while (true)
                {
                    Thread.Sleep(1000);
                    tick++;

                    // Blink a small marker
                    var markerOn = (tick & 1) == 0;
                    display.DrawFilledRectangle(4, 4, 10, 10, markerOn);

                    if (EnablePixelText)
                    {
                        // Clear footer area and redraw tick counter
                        display.DrawFilledRectangle(2, 54, 124, 8, false);
                        PixelTextRenderer.DrawText(display, 4, 55, "TICK " + tick, 1);
                    }

                    display.Display();
                    Console.WriteLine($"Tick: {tick}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: {ex.Message}");
                Console.WriteLine($"Stack: {ex.StackTrace}");
            }

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
    }
}
