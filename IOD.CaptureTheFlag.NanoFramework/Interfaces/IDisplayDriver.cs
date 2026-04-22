using IOD.CaptureTheFlag.NanoFramework.Types;

namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    public interface IDisplayDriver
    {
        // ---- Lifecycle ----
        void PowerOn();
        void PowerDown();
        void Clear();

        // ---- Boot ----
        void ShowMessage(string line1, string line2 = null);

        // ---- Full refresh screens ----
        void RenderHud(HudData data);
        void RenderHud(HudData data, string[] targets, int count, int selectedIndex);
        void RenderHud(HudData data, string[] targets, int count, int selectedIndex, int[] targetRssi);
        void ShowCombat(string targetName, byte myScore);
        void ShowCombatResult(bool won, string targetName);
        void ShowDead(byte lives);

        // ---- Partial HUD updates ----
        void UpdateLives(byte value);
        void UpdateHasFlag(bool value);
        void UpdateCombatScore(byte value);       // was UpdateCombatNumber
        void UpdateHeartbeat();
        void UpdateCombatList(string[] targets, int count, int selectedIndex);
        void UpdateCombatList(string[] targets, int count, int selectedIndex, int[] targetRssi);

        // ---- Combat bar ----
        void UpdateCombatBar(int second, int totalSeconds);

        // ---- Debug ----
        void UpdateLastTx(string hex);
        void UpdateLastRx(string hex, int rssi);
    }
}
