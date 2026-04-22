namespace ChirpTag
{
    /// <summary>Runtime values from <c>I:\appsettings.json</c> (see <see cref="ChirpTagConfigLoader"/>).</summary>
    public static class ChirpTagSettings
    {
        public static string PlayerName = "Player";

        public static string WifiSsid = string.Empty;

        public static string WifiPassword = string.Empty;

        /// <summary>HTTPS root of the game API (no trailing slash required).</summary>
        public static string GameApiBaseUrl = string.Empty;

        /// <summary>When <c>true</c>, use <see cref="System.Net.Security.SslVerification.NoVerification"/> (development only).</summary>
        public static bool GameApiSslNoVerify = true;

        /// <summary>Same meaning as the former Program constant (testing only).</summary>
        public static bool AllowHudWithoutValidDeviceId;
    }
}
