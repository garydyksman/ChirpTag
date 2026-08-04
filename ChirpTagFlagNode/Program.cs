using System;
using System.Device.Gpio;
using System.Device.I2c;
using System.Threading;
using Iot.Device.Ssd13xx;
using nanoFramework.Hardware.Esp32;

namespace ChirpTagFlagNode
{
    public class Program
    {
        // Heltec WiFi LoRa 32 V4 (HTIT-WB32LAF v4.3) OLED pins
        private const int I2cBus = 1;
        private const int SdaPin = 17;
        private const int SclPin = 18;
        private const int OledRstPin = 21;
        private const int DisplayWidth = 128;
        private const int DisplayHeight = 64;
        private const bool EnablePixelText = true;

        public static void Main()
        {
            try
            {
                Console.WriteLine("ChirpTagFlagNode starting...");

                // Hardware reset
                Console.WriteLine("Resetting OLED...");
                var gpio = new GpioController();
                var rst = gpio.OpenPin(OledRstPin, PinMode.Output);
                rst.Write(PinValue.Low);
                Thread.Sleep(10);
                rst.Write(PinValue.High);
                Thread.Sleep(10);

                // Map I2C pins
                Console.WriteLine($"Mapping I2C: SDA={SdaPin}, SCL={SclPin}");
                Configuration.SetPinFunction(SdaPin, DeviceFunction.I2C1_DATA);
                Configuration.SetPinFunction(SclPin, DeviceFunction.I2C1_CLOCK);

                // Init display
                Console.WriteLine($"Initializing display at address 0x{Ssd1306.DefaultI2cAddress:X2}");
                var i2c = I2cDevice.Create(new I2cConnectionSettings(I2cBus, Ssd1306.DefaultI2cAddress));
                var display = new Ssd1306(i2c, Ssd13xx.DisplayResolution.OLED128x64);

                Console.WriteLine("Clearing screen...");
                display.ClearScreen();

                Console.WriteLine("Drawing startup pattern...");
                DrawStartupPattern(display);

                if (EnablePixelText)
                {
                    DrawPixelText(display);
                }

                Console.WriteLine("Updating display...");
                display.Display();

                Console.WriteLine("Display updated successfully!");

                Console.WriteLine("Entering debug loop...");
                var tick = 0;
                while (true)
                {
                    Thread.Sleep(1000);
                    tick++;

                    // Blink a small marker so runtime activity is visible and breakpoints can be hit repeatedly.
                    var markerOn = (tick & 1) == 0;
                    display.DrawFilledRectangle(4, 4, 10, 10, markerOn);

                    if (EnablePixelText)
                    {
                        // Clear a small footer area then redraw text ticker.
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

        private static void DrawPixelText(Ssd1306 display)
        {
            try
            {
                PixelTextRenderer.DrawText(display, 6, 6, "FLAG NODE", 1);
                PixelTextRenderer.DrawText(display, 6, 16, "GRID OK", 1);
                PixelTextRenderer.DrawText(display, 6, 26, "PIXEL TEXT", 1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Pixel text disabled: {ex.Message}");
            }
        }
    }
}
