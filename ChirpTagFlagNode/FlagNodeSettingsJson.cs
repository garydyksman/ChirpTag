namespace ChirpTagFlagNode
{
    /// <summary>Shape of <c>I:\appsettings.json</c> (UTF-8) for flag node. Property names are case-insensitive (camelCase OK).</summary>
    public sealed class FlagNodeSettingsJson
    {
        public byte FlagNodeId { get; set; }

        public string WifiSsid { get; set; }

        public string WifiPassword { get; set; }

        public string GameApiBaseUrl { get; set; }

        /// <summary>Use plain <c>bool</c>; nanoFramework does not support <c>bool?</c> on JSON DTOs.</summary>
        public bool GameApiSslNoVerify { get; set; }
    }
}
