using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Types;

namespace IOD.CaptureTheFlag.NanoFramework.Interfaces
{
    /// <summary>
    /// Single source of truth for all game state.
    /// RefreshCombatList must be called by the heartbeat thread every cycle
    /// before reading GetCombatTargets.
    /// </summary>
    public interface IGameStateManager
    {
        // ---- Identity ----
        byte DeviceId { get; }
        string PlayerName { get; }

        // ---- Game state ----
        GameState State { get; }
        byte Lives { get; }
        bool HasFlag { get; }
        byte[] CarriedKey { get; }
        byte CombatScore { get; }
        byte EnemyFlagId { get; }
        string Timer { get; }
        int SelectedIndex { get; }

        // ---- Setup ----
        void ApplyPlayerList(PlayerInfo[] players);

        // ---- State mutations ----
        void SetState(GameState state);
        void TakeDamage();
        void Respawn(byte newCombatScore);
        void PickupFlag(byte[] key);
        void DropFlag();
        void UpdateTimer(string timer);

        // ---- Peer tracking ----
        void UpdatePeer(byte deviceId, string playerName, int rssi, float snr);
        void RemovePeer(byte deviceId);

        /// <summary>
        /// Called by heartbeat thread every cycle.
        /// Removes timed-out peers and rebuilds the combat target list.
        /// Must be called before GetCombatTargets.
        /// </summary>
        void RefreshCombatList();

        /// <summary>
        /// Returns pre-allocated buffer of current combat targets.
        /// Use count not .Length — entries beyond count are stale.
        /// Call RefreshCombatList first.
        /// </summary>
        string[] GetCombatTargets(out int count);

        /// <summary>
        /// Copies RSSI (dBm) for each current combat target, same order as <see cref="GetCombatTargets"/>.
        /// Unknown entries use -200. Call <see cref="RefreshCombatList"/> first.
        /// </summary>
        void CopyCombatTargetRssi(int[] dest, int maxCount);

        /// <summary>
        /// Builds PeerInfo array from current combat list.
        /// Not a hot path — use for diagnostics only.
        /// </summary>
        PeerInfo[] GetNearbyPeers(out int count);

        PeerInfo GetSelectedTarget();
        string GetPlayerName(byte deviceId);
        void CycleTargets();

        /// <summary>
        /// Returns mutated cached instance — do not store the reference.
        /// </summary>
        HudData ToHudData();
    }
}
