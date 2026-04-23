namespace ChirpTag
{
    /// <summary>
    /// Default JSON shipped in firmware. The nanoFramework <c>Assembly</c> type has no
    /// <c>GetManifestResourceStream</c>, so defaults live here as one minified string.
    /// When you change <c>appsettings.json</c>, update <see cref="Json"/> to match (same logical content).
    /// </summary>
    internal static class AppSettingsBuiltIn
    {
        internal const string Json =
            "{\"playerName\":\"Jessica\",\"wifiSsid\":\"Odido-062073\",\"wifiPassword\":\"WLNYR58VQ5UGBUB5\",\"gameApiBaseUrl\":\"https://5mbvq3sq-5096.euw.devtunnels.ms\",\"gameApiSslNoVerify\":true,\"allowHudWithoutValidDeviceId\":false}";
    }
}
