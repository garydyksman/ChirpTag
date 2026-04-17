using System;
using System.Diagnostics;
using System.Device.Wifi;
using System.Threading;

namespace ChirpTag
{
    /// <summary>Result of <see cref="WiFiBootstrap.RunForHttpSetup"/>.</summary>
    public enum WifiBootOutcome
    {
        /// <summary>WiFi disabled or SSID not set.</summary>
        Skipped,

        /// <summary>Joined AP (sample flow or helper reports success).</summary>
        Connected,

        /// <summary>Configured but scan/associate/DHCP failed; see debug output.</summary>
        Failed,
    }

    /// <summary>
    /// Station Wi-Fi for HTTP phases. Prefer <see cref="RunForHttpSetup"/> after display is up so the framebuffer
    /// and font allocate first. Call <see cref="TearDownRadio"/> before starting LoRa so Wi-Fi and radio are not both hot.
    /// </summary>
    public static class WiFiBootstrap
    {
        /// <summary>WPA2-PSK SSID (2.4 GHz AP for ESP32). Leave empty to skip Wi-Fi entirely.</summary>
        public const string Ssid = "Odido-062073";

        /// <summary>Pre-shared key; empty for open networks (rare).</summary>
        public const string Password = "WLNYR58VQ5UGBUB5";

        /// <summary>Delay after power rails / pin mux so Wi-Fi can start cleanly.</summary>
        public const int RadioSettleMs = 500;

        /// <summary>
        /// Wait after <see cref="WifiAdapter.Disconnect"/> + event hook before first scan so the stack is not mid-connect.
        /// Increase if you see scan/connect instability.
        /// </summary>
        public const int PreScanSettleMs = 2_000;

        /// <summary>Wait for <see cref="WifiAdapter.AvailableNetworksChanged"/> after <see cref="WifiAdapter.ScanAsync"/>.</summary>
        public const int ScanCompleteWaitMs = 15_000;

        /// <summary>Pause after <see cref="WifiAdapter.Connect"/> success to allow DHCP.</summary>
        public const int PostConnectDhcpPauseMs = 2_000;

        public static bool IsConfigured => Ssid != null && Ssid.Length > 0;

        /// <summary>Join AP for HTTP (call after display/font to reduce peak heap during framebuffer setup).</summary>
        public static WifiBootOutcome RunForHttpSetup()
        {
            if (!IsConfigured)
            {
                Debug.WriteLine("[WiFi] skip");
                return WifiBootOutcome.Skipped;
            }

            Debug.WriteLine("[WiFi] start");
            Thread.Sleep(RadioSettleMs);

            return TryConnectViaScan();
        }

        /// <summary>
        /// Disconnect and dispose any Wi-Fi adapters so the radio stack is not active alongside LoRa.
        /// Safe to call multiple times.
        /// </summary>
        public static void TearDownRadio()
        {
            try
            {
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

                    try
                    {
                        a.Dispose();
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WiFi] teardown " + ex.Message);
            }

            Thread.Sleep(300);
        }

        private static WifiBootOutcome TryConnectViaScan()
        {
            WifiAdapter[] adapters;
            try
            {
                adapters = WifiAdapter.FindAllAdapters();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WiFi] adapters " + ex.Message);
                return WifiBootOutcome.Failed;
            }

            if (adapters == null || adapters.Length == 0)
            {
                Debug.WriteLine("[WiFi] no adapter");
                return WifiBootOutcome.Failed;
            }

            for (int i = 1; i < adapters.Length; i++)
            {
                adapters[i].Dispose();
            }

            WifiAdapter wifi = adapters[0];
            ManualResetEvent scanDone = new ManualResetEvent(false);

            void OnAvailableNetworksChanged(WifiAdapter sender, object e)
            {
                scanDone.Set();
            }

            try
            {
                wifi.Disconnect();
                wifi.AvailableNetworksChanged += OnAvailableNetworksChanged;

                Thread.Sleep(PreScanSettleMs);

                wifi.ScanAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[WiFi] scan " + ex.Message);
                wifi.AvailableNetworksChanged -= OnAvailableNetworksChanged;
                wifi.Dispose();
                return WifiBootOutcome.Failed;
            }

            if (!scanDone.WaitOne(ScanCompleteWaitMs, false))
            {
                Debug.WriteLine("[WiFi] scan timeout");
                wifi.AvailableNetworksChanged -= OnAvailableNetworksChanged;
                wifi.Dispose();
                return WifiBootOutcome.Failed;
            }

            wifi.AvailableNetworksChanged -= OnAvailableNetworksChanged;

            WifiNetworkReport report = wifi.NetworkReport;
            if (report == null || report.AvailableNetworks == null)
            {
                Debug.WriteLine("[WiFi] no report");
                wifi.Dispose();
                return WifiBootOutcome.Failed;
            }

            foreach (WifiAvailableNetwork net in report.AvailableNetworks)
            {
                if (net.Ssid != Ssid)
                {
                    continue;
                }

                wifi.Disconnect();
                WifiConnectionResult result = wifi.Connect(net, WifiReconnectionKind.Automatic, Password ?? string.Empty);
                if (result.ConnectionStatus == WifiConnectionStatus.Success)
                {
                    Thread.Sleep(PostConnectDhcpPauseMs);
                    wifi.Dispose();
                    Debug.WriteLine("[WiFi] ok");
                    return WifiBootOutcome.Connected;
                }

                Debug.WriteLine("[WiFi] connect " + result.ConnectionStatus);
                wifi.Dispose();
                return WifiBootOutcome.Failed;
            }

            Debug.WriteLine("[WiFi] ssid missing");
            wifi.Dispose();
            return WifiBootOutcome.Failed;
        }
    }
}
