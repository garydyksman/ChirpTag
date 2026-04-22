namespace ChirpTag
{
    /// <summary>Shape of <c>I:\appsettings.json</c> (UTF-8). Property names are case-insensitive (camelCase OK).</summary>
    public sealed class AppSettingsJson
    {
        public string PlayerName { get; set; }

        public string WifiSsid { get; set; }

        public string WifiPassword { get; set; }

        public string GameApiBaseUrl { get; set; }

        /// <summary>Use plain <c>bool</c>; nanoFramework does not support <c>bool?</c> on JSON DTOs.</summary>
        public bool GameApiSslNoVerify { get; set; }

        /// <summary>Omitted JSON properties deserialize as <c>false</c>; include explicit values when needed.</summary>
        public bool AllowHudWithoutValidDeviceId { get; set; }
    }
}
