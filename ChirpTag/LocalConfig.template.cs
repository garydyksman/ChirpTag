namespace ChirpTag
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
        public const string PLAYER_NAME = "Player1";

        // Optional settings
        public const bool SSL_NO_VERIFY = false;
        public const bool ALLOW_HUD_WITHOUT_DEVICE_ID = false;
        public const bool IGNORE_ATTACK_RANGE_LIMIT = false;
        public const bool SHOW_RSSI_IN_COMBAT_LIST = false;

        // ============================================================
    }
}
