namespace ChirpTagFlagNode
{
    /// <summary>Runtime values sourced from <see cref="LocalConfig"/>.</summary>
    public static class FlagNodeSettings
    {
        public static byte FlagNodeId = LocalConfig.FLAG_NODE_ID;

        public static string WifiSsid = LocalConfig.WIFI_SSID;

        public static string WifiPassword = LocalConfig.WIFI_PASSWORD;

        /// <summary>HTTPS root of the game API (no trailing slash required).</summary>
        public static string GameApiBaseUrl = LocalConfig.API_URL;

        /// <summary>When <c>true</c>, use <see cref="System.Net.Security.SslVerification.NoVerification"/> (development only).</summary>
        public static bool GameApiSslNoVerify = LocalConfig.SSL_NO_VERIFY;
    }
}
