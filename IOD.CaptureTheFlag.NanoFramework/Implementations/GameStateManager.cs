using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;
using System;
using System.Diagnostics;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class GameStateManager : IGameStateManager
    {
        // ---------------------------------------------------------------
        // Constants
        // ---------------------------------------------------------------

        private const int DeviceLostTimeoutCycles = 15;
        private const int MaxDeviceId = 256;
        private const int MaxPlayers = 16;
        private const int UnknownRssiDbm = -200;
        /// <summary>Peers at or below this RSSI (once measured) are omitted from the combat target list.</summary>
        private const int WeakLinkHideFromCombatListDbm = -90;

        // ---------------------------------------------------------------
        // Player list — from HTTP server, permanent for game duration
        // _playerNames[deviceId] = name, null if not registered
        // ---------------------------------------------------------------

        private readonly string[] _playerNames = new string[MaxDeviceId];

        // ---------------------------------------------------------------
        // Presence — _lastSeen[deviceId] = refresh cycle of last heartbeat RX
        // 0 = never heard
        // ---------------------------------------------------------------

        private readonly int[] _lastSeen = new int[MaxDeviceId];
        private readonly int[] _peerLastRssi = new int[MaxDeviceId];
        private int _presenceCycle = 1;

        // ---------------------------------------------------------------
        // Thread safety — LoRa poll thread writes, heartbeat thread reads
        // ---------------------------------------------------------------

        private readonly object _peerLock = new object();

        // ---------------------------------------------------------------
        // Pre-allocated combat targets — parallel arrays, same index
        // _combatTargets[i]   = player name
        // _combatTargetIds[i] = deviceId — avoids string scan
        // ---------------------------------------------------------------

        private readonly string[] _combatTargets = new string[MaxPlayers];
        private readonly byte[] _combatTargetIds = new byte[MaxPlayers];
        private readonly HudData _hudData = new HudData();

        private int _combatTargetCount = 0;
        private int _selectedIndex = 0;

        // ---------------------------------------------------------------
        // Identity + game state
        // ---------------------------------------------------------------

        public byte DeviceId { get; }
        public string PlayerName { get; }
        public GameState State { get; private set; }
        public byte Lives { get; private set; }
        public bool HasFlag { get; private set; }
        public byte[] CarriedKey { get; private set; }
        public byte CombatScore { get; private set; }
        public byte EnemyFlagId { get; private set; }
        public string Timer { get; private set; }
        public int SelectedIndex => _selectedIndex;

        public GameStateManager(byte deviceId, string playerName)
        {
            DeviceId = deviceId;
            PlayerName = playerName;
            Lives = 1;
            State = GameState.Idle;
            Timer = "00:00";
            for (int i = 0; i < MaxDeviceId; i++)
                _peerLastRssi[i] = UnknownRssiDbm;
            Log($"ctor deviceId=0x{DeviceId:X2} player={PlayerName} lives={Lives} state={State}");
        }

        // ---------------------------------------------------------------
        // ApplyPlayerList — called once when game goes Active
        // ---------------------------------------------------------------

        public void ApplyPlayerList(PlayerInfo[] players)
        {
            Log($"ApplyPlayerList begin count={(players == null ? 0 : players.Length)}");
            int httpPeerRows = 0;
            foreach (PlayerInfo player in players)
            {
                Log($"ApplyPlayerList player id=0x{player.DeviceId:X2} name={player.Name} team={player.Team} score={player.CombatScore} enemyFlag=0x{player.EnemyFlagId:X2}");
                if (player.DeviceId == DeviceId)
                {
                    CombatScore = player.CombatScore;
                    EnemyFlagId = player.EnemyFlagId;
                    Log($"ApplyPlayerList self score={CombatScore} enemyFlag=0x{EnemyFlagId:X2}");
                }
                else
                {
                    // Empty names from JSON deserialize as ""; treat like unknown so HUD uses 0xNN fallback.
                    _playerNames[player.DeviceId] = string.IsNullOrEmpty(player.Name) ? null : player.Name;
                    httpPeerRows++;
                    Log($"ApplyPlayerList stored peer id=0x{player.DeviceId:X2} name={(_playerNames[player.DeviceId] ?? "(null)")}");
                }
            }
            Log("ApplyPlayerList end");
            if (CombatListDiagnostics.Enabled)
            {
                CombatListDiagnostics.Write(
                    "HTTP roster applied: self=0x" + DeviceId.ToString("X2") + " httpPeerRows=" + httpPeerRows.ToString() + " (names for LoRa ids; list rows still need heartbeat presence)");
            }
        }

        // ---------------------------------------------------------------
        // State mutations
        // ---------------------------------------------------------------

        public void SetState(GameState state)
        {
            Log($"SetState {State} -> {state}");
            State = state;
        }

        public void UpdateTimer(string timer)
        {
            Log($"UpdateTimer {Timer} -> {timer}");
            Timer = timer;
        }

        public void TakeDamage()
        {
            Log($"TakeDamage begin lives={Lives} state={State}");
            if (Lives > 0) Lives--;
            State = Lives == 0 ? GameState.Dead : GameState.Stunned;
            Log($"TakeDamage end lives={Lives} state={State}");
        }

        public void Respawn(byte newCombatScore)
        {
            Log($"Respawn begin oldScore={CombatScore} newScore={newCombatScore} state={State}");
            CombatScore = newCombatScore;
            State = GameState.Active;
            Log($"Respawn end score={CombatScore} state={State}");
        }

        public void PickupFlag(byte[] key)
        {
            Log($"PickupFlag key={(key == null ? "null" : key.Length.ToString())} bytes");
            CarriedKey = key;
            HasFlag = true;
            Log($"PickupFlag end hasFlag={HasFlag}");
        }

        public void DropFlag()
        {
            Log("DropFlag begin");
            CarriedKey = null;
            HasFlag = false;
            Log($"DropFlag end hasFlag={HasFlag}");
        }

        // ---------------------------------------------------------------
        // UpdatePeer — called on every heartbeat RX (LoRa poll thread)
        // ---------------------------------------------------------------

        public void UpdatePeer(byte deviceId, string playerName, int rssi, float snr)
        {
            Log($"UpdatePeer begin device=0x{deviceId:X2} playerName={playerName} rssi={rssi} snr={snr}");
            if (CombatListDiagnostics.Enabled)
            {
                CombatListDiagnostics.Write(
                    "LoRa presence UpdatePeer id=0x" + deviceId.ToString("X2") + " rssi=" + rssi.ToString() + " rosterName=" + (_playerNames[deviceId] ?? "(null)"));
            }

            // Accept name from heartbeat only if server didn't give us one
            if (_playerNames[deviceId] == null && !string.IsNullOrEmpty(playerName))
            {
                _playerNames[deviceId] = playerName;
                Log($"UpdatePeer accepted heartbeat name={playerName}");
            }

            lock (_peerLock)
            {
                _lastSeen[deviceId] = _presenceCycle;
                _peerLastRssi[deviceId] = rssi;
                Log($"UpdatePeer lastSeen=0x{deviceId:X2} cycle={_lastSeen[deviceId]} rssi={rssi}");
            }
            Log("UpdatePeer end");
        }

        public void RemovePeer(byte deviceId)
        {
            Log($"RemovePeer begin device=0x{deviceId:X2}");
            lock (_peerLock)
            {
                _lastSeen[deviceId] = 0;
                _peerLastRssi[deviceId] = UnknownRssiDbm;
            }
            Log("RemovePeer end");
        }

        // ---------------------------------------------------------------
        // RefreshCombatList — called by heartbeat thread every cycle
        // Scans _lastSeen, removes timed-out players, rebuilds list
        // ---------------------------------------------------------------

        public void RefreshCombatList()
        {
            Log($"RefreshCombatList begin selected={_selectedIndex} currentCount={_combatTargetCount}");
            _presenceCycle++;
            if (_presenceCycle == int.MaxValue)
                _presenceCycle = 1;

            if (State == GameState.Dead)
            {
                lock (_peerLock)
                {
                    for (int j = 0; j < _combatTargetCount; j++)
                    {
                        _combatTargets[j] = null;
                        _combatTargetIds[j] = 0;
                    }

                    _combatTargetCount = 0;
                    _selectedIndex = 0;
                }

                Log("RefreshCombatList end state=Dead count=0");
                if (CombatListDiagnostics.Enabled)
                {
                    CombatListDiagnostics.Write("Refresh state=Dead targetCount=0 (combat list cleared)");
                }

                return;
            }

            int i = 0;

            lock (_peerLock)
            {
                for (int id = 0; id < MaxDeviceId; id++)
                {
                    if (id == DeviceId) continue; // skip self
                    if (_lastSeen[id] == 0) continue; // never heard
                    if (i >= MaxPlayers) break;

                    if ((_presenceCycle - _lastSeen[id]) > DeviceLostTimeoutCycles)
                    {
                        // Timed out — reset presence, keep name
                        Log($"RefreshCombatList peer timeout id=0x{id:X2} lastSeen={_lastSeen[id]} cycle={_presenceCycle}");
                        _lastSeen[id] = 0;
                        _peerLastRssi[id] = UnknownRssiDbm;
                        continue;
                    }

                    int rssi = _peerLastRssi[id];
                    if (rssi != UnknownRssiDbm && rssi <= WeakLinkHideFromCombatListDbm)
                    {
                        Log($"RefreshCombatList skip weak signal id=0x{id:X2} rssi={rssi}");
                        continue;
                    }

                    // Store both name and deviceId at same index
                    _combatTargets[i] = DisplayNameForPeer((byte)id);
                    _combatTargetIds[i] = (byte)id;
                    Log($"RefreshCombatList target index={i} id=0x{id:X2} name={_combatTargets[i]}");
                    i++;
                }

                // Clear stale entries beyond current count
                for (int j = i; j < _combatTargetCount; j++)
                {
                    Log($"RefreshCombatList clear stale index={j}");
                    _combatTargets[j] = null;
                    _combatTargetIds[j] = 0;
                }

                _combatTargetCount = i;
            }

            // Clamp selected index
            if (_selectedIndex >= _combatTargetCount)
            {
                Log($"RefreshCombatList clamp selected {_selectedIndex} -> 0");
                _selectedIndex = 0;
            }
            Log($"RefreshCombatList end count={_combatTargetCount} selected={_selectedIndex}");
            TraceCombatRefreshSummary();
        }

        private void TraceCombatRefreshSummary()
        {
            if (!CombatListDiagnostics.Enabled)
            {
                return;
            }

            CombatListDiagnostics.Write(
                "Refresh state=" + State.ToString() + " targetCount=" + _combatTargetCount.ToString() + " selected=" + _selectedIndex.ToString() + " presenceCycle=" + _presenceCycle.ToString());

            lock (_peerLock)
            {
                for (int k = 0; k < _combatTargetCount && k < MaxPlayers; k++)
                {
                    byte id = _combatTargetIds[k];
                    int rssi = _peerLastRssi[id];
                    string label = _combatTargets[k] ?? "";
                    CombatListDiagnostics.Write(
                        "  row[" + k.ToString() + "] id=0x" + id.ToString("X2") + " rssi=" + rssi.ToString() + " label=" + label);
                }

                if (_combatTargetCount == 0)
                {
                    int heard = 0;
                    int weak = 0;
                    for (int id = 0; id < MaxDeviceId; id++)
                    {
                        if (id == DeviceId)
                        {
                            continue;
                        }

                        if (_lastSeen[id] == 0)
                        {
                            continue;
                        }

                        heard++;
                        int r = _peerLastRssi[id];
                        if (r != UnknownRssiDbm && r <= WeakLinkHideFromCombatListDbm)
                        {
                            weak++;
                        }
                    }

                    CombatListDiagnostics.Write(
                        "  no rows: LoRa ids with presence=" + heard.ToString() + " of those weakRssiSkip<=" + WeakLinkHideFromCombatListDbm.ToString() + " =" + weak.ToString());
                }
            }
        }

        // ---------------------------------------------------------------
        // GetCombatTargets — returns cached buffer, call RefreshCombatList first
        // ---------------------------------------------------------------

        public string[] GetCombatTargets(out int count)
        {
            count = _combatTargetCount;
            Log($"GetCombatTargets count={count}");
            return _combatTargets;
        }

        public void CopyCombatTargetRssi(int[] dest, int maxCount)
        {
            if (dest == null || maxCount <= 0)
                return;

            lock (_peerLock)
            {
                int n = _combatTargetCount < maxCount ? _combatTargetCount : maxCount;
                for (int i = 0; i < n; i++)
                {
                    byte id = _combatTargetIds[i];
                    dest[i] = _peerLastRssi[id];
                }

                for (int i = n; i < maxCount && i < dest.Length; i++)
                    dest[i] = UnknownRssiDbm;
            }
        }

        // ---------------------------------------------------------------
        // GetNearbyPeers — builds from parallel arrays, no string scan
        // ---------------------------------------------------------------

        public PeerInfo[] GetNearbyPeers(out int count)
        {
            count = _combatTargetCount;
            Log($"GetNearbyPeers begin count={count}");
            var result = new PeerInfo[count];

            for (int i = 0; i < count; i++)
            {
                byte deviceId = _combatTargetIds[i];
                Log($"GetNearbyPeers peer index={i} id=0x{deviceId:X2} name={_combatTargets[i]}");
                result[i] = new PeerInfo
                {
                    DeviceId = deviceId,
                    PlayerName = _combatTargets[i],
                    LastSeenAt = _lastSeen[deviceId],
                    Rssi = _peerLastRssi[deviceId]
                };
            }

            Log("GetNearbyPeers end");
            return result;
        }

        // ---------------------------------------------------------------
        // GetSelectedTarget — direct index into parallel arrays, no scan
        // ---------------------------------------------------------------

        public PeerInfo GetSelectedTarget()
        {
            Log($"GetSelectedTarget begin count={_combatTargetCount} selected={_selectedIndex}");
            if (_combatTargetCount == 0) return null;
            if (_selectedIndex >= _combatTargetCount) _selectedIndex = 0;

            byte deviceId = _combatTargetIds[_selectedIndex];
            Log($"GetSelectedTarget end id=0x{deviceId:X2} name={_combatTargets[_selectedIndex]}");

            return new PeerInfo
            {
                DeviceId = deviceId,
                PlayerName = _combatTargets[_selectedIndex],
                LastSeenAt = _lastSeen[deviceId],
                Rssi = _peerLastRssi[deviceId]
            };
        }

        public string GetPlayerName(byte deviceId)
        {
            return DisplayNameForPeer(deviceId);
        }

        /// <summary>Roster name if present and non-empty; otherwise a stable hex label for the device.</summary>
        private string DisplayNameForPeer(byte deviceId)
        {
            string name = _playerNames[deviceId];
            return string.IsNullOrEmpty(name) ? $"0x{deviceId:X2}" : name;
        }

        // ---------------------------------------------------------------
        // CycleTargets — wraps selection index
        // ---------------------------------------------------------------

        public void CycleTargets()
        {
            Log($"CycleTargets begin count={_combatTargetCount} selected={_selectedIndex}");
            if (_combatTargetCount == 0) { _selectedIndex = 0; Log("CycleTargets no targets"); return; }
            _selectedIndex = (_selectedIndex + 1) % _combatTargetCount;
            Log($"CycleTargets end selected={_selectedIndex}");
        }

        // ---------------------------------------------------------------
        // ToHudData — mutates cached instance, no allocation
        // ---------------------------------------------------------------

        public HudData ToHudData()
        {
            Log($"ToHudData lives={Lives} hasFlag={HasFlag} score={CombatScore} timer={Timer}");
            _hudData.Lives = Lives;
            _hudData.HasFlag = HasFlag;
            _hudData.CombatScore = CombatScore;
            _hudData.Timer = Timer;
            return _hudData;
        }

        [Conditional("VERBOSE_GAMESTATE")]
        private void Log(string message)
        {
            DebugLog.Write("[GameState] " + message);
        }
    }
}
