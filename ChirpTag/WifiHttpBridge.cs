using IOD.CaptureTheFlag.NanoFramework.Interfaces;

namespace ChirpTag
{
    /// <summary>Maps host Wi-Fi bootstrap to <see cref="IWifiHttpBridge"/> for library code (e.g. HTTP respawn).</summary>
    internal sealed class WifiHttpBridge : IWifiHttpBridge
    {
        public WifiHttpBootOutcome EnableForHttp()
        {
            switch (WiFiBootstrap.RunForHttpSetup())
            {
                case WifiBootOutcome.Connected:
                    return WifiHttpBootOutcome.Connected;
                case WifiBootOutcome.Skipped:
                    return WifiHttpBootOutcome.Skipped;
                default:
                    return WifiHttpBootOutcome.Failed;
            }
        }

        public void TearDownRadio()
        {
            WiFiBootstrap.TearDownRadio();
        }
    }
}
