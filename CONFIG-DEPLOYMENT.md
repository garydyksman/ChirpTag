# Configuration Deployment Guide

## The Problem

**AppSettingsBuiltIn.cs is now intentionally EMPTY** to prevent hours of debugging when dev tunnels refresh!

All config must be in `I:\appsettings.json` on the device SPIFFS filesystem.

## How to Deploy Config to Device

### Option 1: One-Time Config Writer (Recommended)

1. **Edit your config** in `appsettings.sample.json` (either `ChirpTag/` or `ChirpTagFlagNode/`)
   - Update WiFi credentials
   - Update your current dev tunnel URL
   - Update player name / flag node ID

2. **Copy the code from `WriteConfig.cs`** and paste it at the **TOP of Program.cs Main()**

3. **Update the JSON** in the code with your current settings

4. **Deploy once** - watch serial output for "SUCCESS: Config written"

5. **Remove the WriteConfig code** from Program.cs

6. **Deploy again** - device now loads from `I:\appsettings.json`

### Option 2: Manual File System Access

If you have a tool to write files to ESP32 SPIFFS:

```bash
# Write appsettings.json to I:\ partition on device
# (Tool depends on your setup - esptool.py, VS Code extension, etc.)
```

## When Config Gets Erased

**Config persists across:**
- ✅ Normal reboots
- ✅ Code redeployments
- ✅ Firmware updates with `nanoff --update`

**Config is WIPED by:**
- ❌ `nanoff --masserase`
- ❌ Full flash erase

After a `--masserase`, you need to redeploy the config!

## Checking Current Config

Add this to Main() temporarily:

```csharp
if (File.Exists("I:\\appsettings.json"))
{
    string current = File.ReadAllText("I:\\appsettings.json");
    Console.WriteLine("Current config:");
    Console.WriteLine(current);
}
else
{
    Console.WriteLine("No config file found at I:\\appsettings.json");
}
```

## Quick Reference

### Game Device Config Template
```json
{
  "playerName": "Jessica",
  "wifiSsid": "Your-WiFi",
  "wifiPassword": "password",
  "gameApiBaseUrl": "https://xxx.euw.devtunnels.ms",
  "gameApiSslNoVerify": true,
  "allowHudWithoutValidDeviceId": false,
  "ignoreAttackRangeLimit": false
}
```

### Flag Node Config Template
```json
{
  "flagNodeId": 16,
  "wifiSsid": "Your-WiFi",
  "wifiPassword": "password",
  "gameApiBaseUrl": "https://xxx.euw.devtunnels.ms",
  "gameApiSslNoVerify": true
}
```

## Troubleshooting

**Device won't connect to WiFi:**
- Check `wifiSsid` and `wifiPassword` in your config
- Make sure config was actually written (check serial output)

**HTTP 404 errors:**
- Your `gameApiBaseUrl` is wrong or the dev tunnel refreshed
- Update config and redeploy

**"Config file not found":**
- You need to run the WriteConfig code once to create `I:\appsettings.json`
