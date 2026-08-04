namespace ChirpTagFlagNode
{
    /// <summary>
    /// Default JSON shipped in firmware. The nanoFramework <c>Assembly</c> type has no
    /// <c>GetManifestResourceStream</c>, so defaults live here as one minified string.
    /// When you change <c>appsettings.json</c>, update <see cref="Json"/> to match (same logical content).
    /// </summary>
    internal static class FlagNodeSettingsBuiltIn
    {
        internal const string Json =
            "{\"flagNodeId\":16,\"wifiSsid\":\"Odido-062073\",\"wifiPassword\":\"WLNYR58VQ5UGBUB5\",\"gameApiBaseUrl\":\"https://l2f3t9xl-7055.euw.devtunnels.ms\",\"gameApiSslNoVerify\":true}";
    }
}
