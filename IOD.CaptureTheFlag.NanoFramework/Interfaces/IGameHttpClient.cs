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
        /// POST <c>/api/game/register</c> with <c>playerName</c> (trimmed on server; match is case-sensitive).
        /// Waiting: new name gets a new id; duplicate name → 409.
        /// In progress: same name as an existing row with assigned id → 200 same id (reconnect); unknown name or bad row → 409.
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
