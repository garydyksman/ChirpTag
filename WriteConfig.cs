/*
 * USAGE: Temporarily add this code to your Program.cs Main() to write appsettings.json to device SPIFFS.
 *
 * 1. Update the JSON string below with your current WiFi and dev tunnel URL
 * 2. Add this code at the START of Main() (before config loading)
 * 3. Deploy once
 * 4. Check serial output for "Config written to I:\appsettings.json"
 * 5. Remove this code and deploy again
 *
 * After that, I:\appsettings.json will persist across reboots (but not --masserase flashes).
 */

using System;
using System.IO;

// FOR GAME DEVICE (ChirpTag):
string gameDeviceConfig = @"{
  ""playerName"": ""Jessica"",
  ""wifiSsid"": ""YOUR_SSID"",
  ""wifiPassword"": ""YOUR_PASSWORD"",
  ""gameApiBaseUrl"": ""https://YOUR-TUNNEL-HERE.euw.devtunnels.ms"",
  ""gameApiSslNoVerify"": true,
  ""allowHudWithoutValidDeviceId"": false,
  ""ignoreAttackRangeLimit"": false
}";

// FOR FLAG NODE (ChirpTagFlagNode):
string flagNodeConfig = @"{
  ""flagNodeId"": 16,
  ""wifiSsid"": ""YOUR_SSID"",
  ""wifiPassword"": ""YOUR_PASSWORD"",
  ""gameApiBaseUrl"": ""https://YOUR-TUNNEL-HERE.euw.devtunnels.ms"",
  ""gameApiSslNoVerify"": true
}";

// CHOOSE ONE:
string configToWrite = flagNodeConfig;  // or gameDeviceConfig

try
{
    File.WriteAllText("I:\\appsettings.json", configToWrite);
    Console.WriteLine("SUCCESS: Config written to I:\\appsettings.json");
    Console.WriteLine("Remove this code and redeploy!");
}
catch (Exception ex)
{
    Console.WriteLine("ERROR writing config: " + ex.Message);
}

Thread.Sleep(Timeout.Infinite);  // Stop here so you can see the message
