using IOD.CaptureTheFlag.NanoFramework.Types;

namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IGameHttpClient
    {
        /// <summary>
        /// Always safe to call. Returns current game state.
        /// Players only populated when Status == Active.
        /// </summary>
        GameInfo GetCurrentGame();

        /// <summary>
        /// Register by player name. Only call when Status == Waiting.
        /// Returns assigned DeviceId.
        /// </summary>
        PlayerSetup Register(string playerName);

        /// <summary>Reports combat result.</summary>
        void ReportCombat(byte winnerId, byte loserId, bool loserHadKey);

        /// <summary>Reports flag delivery. Returns true if accepted.</summary>
        bool ReportDeliver(byte deviceId, byte[] key);

        /// <summary>Gets a new combat score after respawn.</summary>
        byte GetRespawnNumber(byte deviceId);
    }
}
