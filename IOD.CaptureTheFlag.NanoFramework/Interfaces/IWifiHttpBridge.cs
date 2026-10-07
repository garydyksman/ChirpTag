namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    /// <summary>Outcome of bringing Wi-Fi up for a short HTTP call (e.g. respawn score).</summary>
    public enum WifiHttpBootOutcome
    {
        Skipped,
        Connected,
        Failed,
    }

    /// <summary>Lets game code join AP and tear down without referencing a specific host bootstrap type.</summary>
    public interface IWifiHttpBridge
    {
        WifiHttpBootOutcome EnableForHttp();

        void TearDownRadio();
    }
}
