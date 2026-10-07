namespace ChirpTag
{
    /// <summary>Runtime values sourced from <see cref="LocalConfig"/>.</summary>
    public static class ChirpTagSettings
    {
        public static string PlayerName = LocalConfig.PLAYER_NAME;

        public static string WifiSsid = LocalConfig.WIFI_SSID;

        public static string WifiPassword = LocalConfig.WIFI_PASSWORD;

        /// <summary>HTTPS root of the game API (no trailing slash required).</summary>
        public static string GameApiBaseUrl = LocalConfig.API_URL;

        /// <summary>When <c>true</c>, use <see cref="System.Net.Security.SslVerification.NoVerification"/> (development only).</summary>
        public static bool GameApiSslNoVerify = LocalConfig.SSL_NO_VERIFY;

        /// <summary>Same meaning as the former Program constant (testing only).</summary>
        public static bool AllowHudWithoutValidDeviceId = LocalConfig.ALLOW_HUD_WITHOUT_DEVICE_ID;

        /// <summary>When <c>true</c>, skip RSSI-based exclusion so any heard peer can be selected for attack.</summary>
        public static bool IgnoreAttackRangeLimit = LocalConfig.IGNORE_ATTACK_RANGE_LIMIT;

        /// <summary>When <c>true</c>, show RSSI values in the combat list (off by default).</summary>
        public static bool ShowRssiInCombatList = LocalConfig.SHOW_RSSI_IN_COMBAT_LIST;
    }
}
