# nanoFramework Firmware and Package Versions

## Working Firmware Version

**ESP32-S3 Firmware: 1.16.0.677** (non-OCTAL, non-QUAD)

### Flash Command
```bash
nanoff --target ESP32_S3 --serialport COM4 --update --masserase
```

**Note:** `--masserase` wipes SPIFFS, so `appsettings.json` on `I:\` must be re-created after flashing.

### Why This Version?
- Version **1.17.0.115 OCTAL** introduced a breaking SPI bus ID change (PR #2915 + IDF v5.5.3 migration)
- Caused `System.ArgumentException` at `NativeOpenDevice` for all SPI bus IDs
- 1.16.0.677 is the last stable version before this breaking change

## Compatible NuGet Package Versions

### Core Packages
```xml
<package id="nanoFramework.CoreLibrary" version="1.17.11" targetFramework="netnano1.0" />
<package id="nanoFramework.Hardware.Esp32" version="1.6.37" targetFramework="netnano1.0" />
<package id="nanoFramework.Runtime.Events" version="1.11.32" targetFramework="netnano1.0" />
<package id="nanoFramework.System.Collections" version="1.5.67" targetFramework="netnano1.0" />
<package id="nanoFramework.System.Threading" version="1.1.52" targetFramework="netnano1.0" />
```

### Hardware I/O
```xml
<package id="nanoFramework.System.Device.Gpio" version="1.1.57" targetFramework="netnano1.0" />
<package id="nanoFramework.System.Device.Spi" version="1.3.82" targetFramework="netnano1.0" />
<package id="nanoFramework.System.Device.I2c" version="1.1.29" targetFramework="netnano1.0" />
<package id="nanoFramework.System.Device.Wifi" version="1.5.144" targetFramework="netnano1.0" />
```

### Networking
```xml
<package id="nanoFramework.System.Net" version="1.11.54" targetFramework="netnano1.0" />
<package id="nanoFramework.System.Net.Http.Client" version="1.5.200" targetFramework="netnano1.0" />
```

### Display & Graphics
```xml
<package id="nanoFramework.Graphics.Core" version="1.2.45" targetFramework="netnano1.0" />
<package id="nanoFramework.Iot.Device.Ssd13xx" version="1.3.689" targetFramework="netnano1.0" />
```

### Utilities
```xml
<package id="nanoFramework.Json" version="2.2.203" targetFramework="netnano1.0" />
<package id="nanoFramework.System.IO.FileSystem" version="1.1.87" targetFramework="netnano1.0" />
<package id="nanoFramework.System.IO.Streams" version="1.1.96" targetFramework="netnano1.0" />
<package id="nanoFramework.System.Text" version="1.3.42" targetFramework="netnano1.0" />
```

## Hardware Targets

### Heltec Vision Master E219 (HT-VME213)
- **Board:** ESP32-S3
- **Display:** LCM EN2R13 e-Paper (SPI2)
- **Radio:** SX1262 LoRa (SPI1)
- **Firmware:** 1.16.0.677

### Heltec WiFi LoRa 32 V4 (HTIT-WB32LAF v4.3)
- **Board:** ESP32-S3
- **Display:** SSD1306/SSD1315 OLED 128×64 (I2C, SDA: GPIO17, SCL: GPIO18, RST: GPIO21)
- **Radio:** SX1262 LoRa
- **Firmware:** 1.16.0.677 (same as Vision Master)

## Updating This Document

When package versions change, update both this file and the corresponding `packages.config` files in:
- `ChirpTag/packages.config`
- `ChirpTagFlagNode/packages.config`
- `IOD.CaptureTheFlag.NanoFramework/packages.config`
