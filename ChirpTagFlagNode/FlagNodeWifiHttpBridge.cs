using System;
using System.Device.Wifi;
using System.Threading;
using nanoFramework.Networking;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;

namespace ChirpTagFlagNode
{
    /// <summary>
    /// WiFi/HTTP bridge for flag node. Reconnects WiFi on demand (LoRa and WiFi cannot be active simultaneously).
    /// </summary>
    internal sealed class FlagNodeWifiHttpBridge : IWifiHttpBridge
    {
        public WifiHttpBootOutcome EnableForHttp()
        {
            try
            {
                Console.WriteLine("[FlagNodeWifi] Connecting WiFi...");
                var cts = new CancellationTokenSource(30_000);
                bool ok = WifiNetworkHelper.ConnectDhcp(
                    LocalConfig.WIFI_SSID,
                    LocalConfig.WIFI_PASSWORD,
                    requiresDateTime: false,
                    token: cts.Token);

                if (ok)
                {
                    Console.WriteLine("[FlagNodeWifi] WiFi connected");
                    return WifiHttpBootOutcome.Connected;
                }

                Console.WriteLine("[FlagNodeWifi] WiFi connect failed");
                return WifiHttpBootOutcome.Failed;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FlagNodeWifi] EnableForHttp error: " + ex.Message);
                return WifiHttpBootOutcome.Failed;
            }
        }

        public void TearDownRadio()
        {
            try
            {
                Console.WriteLine("[FlagNodeWifi] Tearing down WiFi...");
                WifiAdapter[] adapters = WifiAdapter.FindAllAdapters();
                if (adapters == null)
                {
                    return;
                }

                for (int i = 0; i < adapters.Length; i++)
                {
                    WifiAdapter a = adapters[i];
                    if (a == null)
                    {
                        continue;
                    }

                    try
                    {
                        a.Disconnect();
                    }
                    catch
                    {
                    }
                }

                Console.WriteLine("[FlagNodeWifi] WiFi torn down");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[FlagNodeWifi] TearDownRadio error: " + ex.Message);
            }
        }
    }
}
