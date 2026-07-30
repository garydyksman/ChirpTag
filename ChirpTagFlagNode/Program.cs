using System.Device.Gpio;
using System.Device.I2c;
using System.Threading;
using Iot.Device.Ssd13xx;
using Iot.Device.Ssd13xx.Samples;
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

        public static void Main()
        {
            // Hardware reset
            var gpio = new GpioController();
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
            display.Font = new BasicFont();
            display.DrawString(2, 24, "Hello World", 1, true);
            display.Display();

            Thread.Sleep(Timeout.Infinite);
        }
    }
}
