namespace ChirpTagFlagNode
{
    /// <summary>
    /// Local configuration template.
    ///
    /// SETUP INSTRUCTIONS:
    /// 1. Copy this file to LocalConfig.cs (same directory)
    /// 2. Edit LocalConfig.cs with your WiFi and API settings
    /// 3. LocalConfig.cs is in .gitignore and won't be committed
    /// </summary>
    public static class LocalConfig
    {
        // ============================================================
        // 🔧 HACKATHON CONFIG - CHANGE THESE IN LocalConfig.cs!
        // ============================================================

        public const string WIFI_SSID = "YourWiFiNetwork";
        public const string WIFI_PASSWORD = "YourPassword";
        public const string API_URL = "https://your-tunnel.devtunnels.ms";

        // Optional settings
        public const bool SSL_NO_VERIFY = false;

        // ============================================================
    }
}
