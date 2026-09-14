using IOD.CaptureTheFlag.NanoFramework.Interfaces;

namespace ChirpTagFlagNode
{
    /// <summary>
    /// WiFi/HTTP bridge for flag node.
    /// Simpler than game device - WiFi stays connected most of the time.
    /// </summary>
    internal sealed class FlagNodeWifiHttpBridge : IWifiHttpBridge
    {
        public WifiHttpBootOutcome EnableForHttp()
        {
            // For flag node, WiFi is already connected at boot
            // Just verify it's still connected
            if (WiFiHelper.IsConfigured)
            {
                return WifiHttpBootOutcome.Connected;
            }

            // Try to connect if not already
            WifiStatus status = WiFiHelper.Connect();

            return status switch
            {
                WifiStatus.Connected => WifiHttpBootOutcome.Connected,
                WifiStatus.NotConfigured => WifiHttpBootOutcome.Skipped,
                _ => WifiHttpBootOutcome.Failed
            };
        }

        public void TearDownRadio()
        {
            // For flag node, we might want to keep WiFi up
            // Or disconnect if we need LoRa exclusively
            // For now, do nothing - WiFi stays up
            // WiFiHelper.Disconnect();
        }
    }
}
