using IOD.CaptureTheFlag.NanoFramework.Enum;
using IOD.CaptureTheFlag.NanoFramework.Extensions;
using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;
using Iot.Device.LoRa;
using System;
using System.Diagnostics;
using System.Threading;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class GamerDevice : GameDevice, IGamerDevice
    {
        private enum UiMode
        {
            Message,
            Hud,
            Combat,
            CombatResult,
            Dead
        }

        // ---------------------------------------------------------------
        // Constants
        // ---------------------------------------------------------------

        private const int HeartbeatIntervalMs = 5_000;
        private const int TxDrainIntervalMs = 500;
        private const int UiRefreshIntervalMs = 1_000;
        private const int CombatTimeoutSeconds = 8;
        private const int CombatResultDelayMs = 2_000;
        private const int HeartbeatIndicatorIntervalMs = 5_000;
        private const int HeartbeatIndicatorQuietWindowMs = 3_000;
        private const long TicksPerMillisecond = 10_000L;
        private const int MaxRenderedTargets = 16;   // keep aligned with GameStateManager.MaxPlayers
        private const bool VerboseLogging = false;
        private const bool VerboseByteLogging = false;
        private const bool VerboseTickLogging = false;
        private const bool VerboseRadioLogging = false;
        // ---------------------------------------------------------------
        // Dependencies
        // ---------------------------------------------------------------

        private readonly IDisplayDriver _display;
        private readonly IGameStateManager _state;
        private readonly ILoRaDevice _lora;
        private readonly IPacketBuilder _builder;
        private readonly TxQueue _txQueue;

        // ---------------------------------------------------------------
        // Threads
        // ---------------------------------------------------------------

        private Thread _heartbeatThread;
        private Thread _txThread;
        private Thread _uiThread;
        private bool _running;
        private bool _started;

        // ---------------------------------------------------------------
        // UI / combat flow
        // ---------------------------------------------------------------

        private bool _combatListDirty = false;
        private bool _combatPending = false;
        private bool _combatResolved = true;
        private string _combatTarget = null;
        private byte _combatTargetDeviceId = 0;
        private UiMode _uiMode = UiMode.Message;

        // ---------------------------------------------------------------
        // Cached rendered combat list snapshot
        // Prevents unnecessary e-ink redraws but still detects timeouts
        // ---------------------------------------------------------------

        private readonly string[] _lastRenderedTargets = new string[MaxRenderedTargets];
        private int _lastRenderedCount = -1;
        private int _lastRenderedSelectedIndex = -1;

        // ---------------------------------------------------------------
        // Pre-allocated heartbeat bytes
        // ---------------------------------------------------------------

        private readonly byte[] _heartbeatBytes;
        private readonly object _displayActivityLock = new object();
        private long _lastDisplayActivityTick;
        private long _lastHeartbeatIndicatorTick;

        // ---------------------------------------------------------------
        // State helpers
        // ---------------------------------------------------------------

        private bool IsActive => _state.State == GameState.Active;
        private bool IsDead => _state.State == GameState.Dead;

        // Show/update HUD while alive in-game, even if temporarily stunned.
        private bool CanRenderHud =>
            _uiMode == UiMode.Hud &&
            _state.State != GameState.Dead &&
            _state.State != GameState.Idle;

        private bool IsCombatRadioWindow =>
            _combatPending ||
            _uiMode == UiMode.Combat ||
            _uiMode == UiMode.CombatResult ||
            _uiMode == UiMode.Dead;

        // Presence heartbeat should be transmitted while the player is still "in play".
        // Dead and idle devices should not keep advertising themselves.
        private bool CanAdvertisePresence =>
            _state.State == GameState.Active ||
            _state.State == GameState.Stunned;

        // ---------------------------------------------------------------
        // IGamerDevice properties
        // ---------------------------------------------------------------

        public byte CombatScore => _state.CombatScore;
        public bool HasKey => _state.HasFlag;
        public byte[] CarriedKey => _state.CarriedKey;

        // ---------------------------------------------------------------
        // Construction
        // ---------------------------------------------------------------

        public GamerDevice(
            IGameStateManager state,
            IDisplayDriver display,
            ILoRaDevice lora,
            IPacketBuilder builder,
            IMessageHandler messageHandler)
            : base(state.DeviceId, messageHandler)
        {
            Log($"ctor begin deviceId=0x{state.DeviceId:X2} player={state.PlayerName} state={state.State}");
            _state = state;
            _display = display;
            _lora = lora;
            _builder = builder;
            _txQueue = new TxQueue(capacity: 8);

            _heartbeatBytes = builder.Heartbeat(state.DeviceId).ToBytes();
            if (VerboseByteLogging)
                Log("ctor heartbeatBytes prepared");

            long nowTick = DateTime.UtcNow.Ticks;
            _lastDisplayActivityTick = nowTick;
            _lastHeartbeatIndicatorTick = nowTick;

            // If the caller already marked the game active before constructing us,
            // seed HUD mode so the UI loop behaves correctly immediately.
            if (_state.State == GameState.Active)
            {
                _uiMode = UiMode.Hud;
                _combatListDirty = true;
                Log("ctor seeded uiMode=Hud because state is Active");
            }

            // Wire message events
            Log("ctor wiring message events");
            messageHandler.HeartbeatReceived += OnHeartbeatReceived;
            messageHandler.AttackReceived += OnAttackReceived;
            messageHandler.AttackAckReceived += OnAttackAckReceived;
            messageHandler.CombatResultReceived += OnCombatResultReceived;
            messageHandler.FlagTransferReceived += OnFlagTransferReceived;
            messageHandler.KeyGrantReceived += OnKeyGrantReceived;
            messageHandler.RespawnAckReceived += OnRespawnAckReceived;

            Log("ctor wiring LoRa PacketReceived");
            _lora.PacketReceived += OnLoRaPacketReceived;
            Log("ctor end - background services not started yet");
        }

        // ---------------------------------------------------------------
        // HeartbeatLoop
        // ---------------------------------------------------------------

        private void HeartbeatLoop()
        {
            Log("HeartbeatLoop start");
            while (_running)
            {
                try
                {
                    if (VerboseTickLogging)
                        Log($"HeartbeatLoop tick state={_state.State} uiMode={_uiMode} canAdvertise={CanAdvertisePresence}");

                    if (CanAdvertisePresence && !IsCombatRadioWindow)
                    {
                        if (VerboseRadioLogging) Log("HeartbeatLoop sending heartbeat");
                        _lora.Send(_heartbeatBytes, timeoutMs: 1_000);
                        if (VerboseRadioLogging) Log("HeartbeatLoop send complete");
                    }
                }
                catch (Exception ex)
                {
                    Log($"HeartbeatLoop error={ex.Message}");
                }

                if (VerboseTickLogging)
                    Log($"HeartbeatLoop sleep={HeartbeatIntervalMs}");

                Thread.Sleep(HeartbeatIntervalMs);
            }
            Log("HeartbeatLoop stop");
        }

        // ---------------------------------------------------------------
        // TxLoop
        // ---------------------------------------------------------------

        private void TxLoop()
        {
            Log("TxLoop start");
            while (_running)
            {
                try
                {
                    byte[] pending;
                    while ((pending = _txQueue.Dequeue()) != null)
                    {
                        Log($"TxLoop sending packet remaining={_txQueue.Count}");
                        _lora.Send(pending, timeoutMs: 1_000);
                        Log("TxLoop send complete");
                    }
                }
                catch (Exception ex)
                {
                    Log($"TxLoop error={ex.Message}");
                }

                if (VerboseTickLogging)
                    Log($"TxLoop sleep={TxDrainIntervalMs} queueCount={_txQueue.Count}");

                Thread.Sleep(TxDrainIntervalMs);
            }
            Log("TxLoop stop");
        }

        // ---------------------------------------------------------------
        // UiLoop
        // Owns periodic HUD refresh / heartbeat animation / peer list rendering
        // ---------------------------------------------------------------

        private void UiLoop()
        {
            Log("UiLoop start");
            while (_running)
            {
                try
                {
                    if (VerboseTickLogging)
                        Log($"UiLoop tick state={_state.State} uiMode={_uiMode} canRenderHud={CanRenderHud} dirty={_combatListDirty}");

                    if (CanRenderHud)
                    {
                        if (VerboseTickLogging) Log("UiLoop RefreshCombatList begin");
                        _state.RefreshCombatList();

                        string[] targets = _state.GetCombatTargets(out int count);
                        int selectedIndex = _state.SelectedIndex;
                        if (VerboseTickLogging) Log($"UiLoop targets count={count} selected={selectedIndex}");

                        bool combatListChanged = HasCombatListChanged(targets, count, selectedIndex);
                        if (_combatListDirty && !combatListChanged)
                        {
                            Log("UiLoop combat list dirty but unchanged; clearing dirty flag");
                            _combatListDirty = false;
                        }

                        if (combatListChanged)
                        {
                            Log("UiLoop rendering combat list");
                            _display.UpdateCombatList(targets, count, selectedIndex);
                            MarkDisplayActivity("combat-list");
                            CacheCombatListSnapshot(targets, count, selectedIndex);
                            _combatListDirty = false;
                            Log("UiLoop combat list rendered dirty=false");
                        }
                        else
                        {
                            if (VerboseTickLogging) Log("UiLoop combat list unchanged");

                            if (ShouldRenderHeartbeatIndicator())
                            {
                                Log("UiLoop rendering heartbeat indicator");
                                _display.UpdateHeartbeat();
                                MarkDisplayActivity("heartbeat");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log($"UiLoop error={ex.Message}");
                }

                if (VerboseTickLogging)
                    Log($"UiLoop sleep={UiRefreshIntervalMs}");

                Thread.Sleep(UiRefreshIntervalMs);
            }
            Log("UiLoop stop");
        }

        // ---------------------------------------------------------------
        // IGamerDevice — UI input
        // ---------------------------------------------------------------

        public void Attack()
        {
            Log($"Attack begin state={_state.State} uiMode={_uiMode} pending={_combatPending} resolved={_combatResolved}");
            if (!IsActive || _uiMode != UiMode.Hud)
            {
                Log("Attack ignored - not in active HUD state");
                return;
            }

            if (_combatPending)
            {
                Log("Attack ignored - combat already pending");
                return;
            }

            // Refresh once at action time so we do not attack a target that just timed out.
            Log("Attack RefreshCombatList begin");
            _state.RefreshCombatList();

            PeerInfo target = _state.GetSelectedTarget();
            if (target == null)
            {
                Log("Attack ignored - no target selected");
                _combatListDirty = true;
                return;
            }

            Log($"Attack target deviceId=0x{target.DeviceId:X2} name={target.PlayerName} lastSeen={target.LastSeenAt}");
            _combatTarget = target.PlayerName;
            _combatTargetDeviceId = target.DeviceId;
            _combatResolved = false;
            _combatPending = true;
            _uiMode = UiMode.Combat;
            Log($"Attack state updated uiMode={_uiMode} pending={_combatPending} resolved={_combatResolved} combatTarget={_combatTarget}");

            Log("Attack ShowCombat begin");
            _display.ShowCombat(target.PlayerName, _state.CombatScore);
            MarkDisplayActivity("combat-enter");
            Log("Attack ShowCombat end");
            Log("Attack QueuePacket begin");
            QueuePacket(_builder.Attack(DeviceId, target.DeviceId));

            Log($"Attack queued target=0x{target.DeviceId:X2}");

            Log("Attack starting CombatTimeoutLoop thread");
            new Thread(CombatTimeoutLoop).Start();
            Log("Attack end");
        }

        public void CycleTargets()
        {
            Log($"CycleTargets begin state={_state.State} uiMode={_uiMode}");
            if (!IsActive || _uiMode != UiMode.Hud)
            {
                Log("CycleTargets ignored - not in active HUD state");
                return;
            }

            _state.CycleTargets();
            _combatListDirty = true;
            Log($"CycleTargets end selectedIndex={_state.SelectedIndex} dirty={_combatListDirty}");
        }

        // ---------------------------------------------------------------
        // Combat timeout
        // ---------------------------------------------------------------

        private void CombatTimeoutLoop()
        {
            Log("CombatTimeoutLoop start");
            for (int sec = 1; sec <= CombatTimeoutSeconds; sec++)
            {
                Log($"CombatTimeoutLoop sleep second={sec}");
                Thread.Sleep(1_000);

                if (_combatResolved || _uiMode != UiMode.Combat)
                {
                    Log($"CombatTimeoutLoop exit resolved={_combatResolved} uiMode={_uiMode}");
                    return;
                }

                Log($"CombatTimeoutLoop UpdateCombatBar second={sec}");
                _display.UpdateCombatBar(sec, CombatTimeoutSeconds);
                MarkDisplayActivity("combat-bar");
            }

            if (!_combatResolved && _uiMode == UiMode.Combat)
            {
                Log("CombatTimeoutLoop timeout - cancelling");

                _combatPending = false;
                _combatResolved = true;
                _combatTarget = null;
                _combatTargetDeviceId = 0;
                Log($"CombatTimeoutLoop reset pending={_combatPending} resolved={_combatResolved}");

                EnterHud();
            }
            Log("CombatTimeoutLoop end");
        }

        // ---------------------------------------------------------------
        // LoRa RX
        // ---------------------------------------------------------------

        private void OnLoRaPacketReceived(object sender, LoRaMessage message)
        {
            try
            {
                if (VerboseRadioLogging) Log($"OnLoRaPacketReceived len={message.Payload.Length} rssi={message.Rssi} snr={message.Snr}");
                if (ShouldIgnoreIncomingPacket(message.Payload))
                {
                    if (VerboseRadioLogging) Log("OnLoRaPacketReceived dropped non-combat packet during combat window");
                    return;
                }

                Handler.Handle(message.Payload, message.Rssi, message.Snr);
                if (VerboseRadioLogging) Log("OnLoRaPacketReceived handled");
            }
            catch (Exception)
            {
            }
        }

        // ---------------------------------------------------------------
        // Message handlers
        // ---------------------------------------------------------------

        public void OnHeartbeatReceived(byte fromDeviceId, int rssi, float snr)
        {
            if (VerboseRadioLogging) Log($"OnHeartbeatReceived from=0x{fromDeviceId:X2} rssi={rssi} snr={snr} uiMode={_uiMode}");
            _state.UpdatePeer(fromDeviceId, null, rssi, snr);

            // Heartbeats refresh peer presence, but they do not always need a display refresh.
            // The UI loop compares the rendered target snapshot and redraws only when the list changes.
            if (_uiMode == UiMode.Hud)
            {
                if (VerboseRadioLogging) Log("OnHeartbeatReceived peer updated; UI loop will diff combat list");
            }
            else
            {
                if (VerboseRadioLogging) Log("OnHeartbeatReceived not marking dirty outside HUD");
            }
        }

        public void OnAttackReceived(byte fromDeviceId)
        {
            Log($"OnAttackReceived from=0x{fromDeviceId:X2} state={_state.State} uiMode={_uiMode} pending={_combatPending}");
            // Do not participate in combat unless fully active and free to engage.
            if (!IsActive || _uiMode != UiMode.Hud || _combatPending)
            {
                Log($"OnAttackReceived ignored from=0x{fromDeviceId:X2}");
                return;
            }

            _combatTargetDeviceId = fromDeviceId;
            _combatTarget = _state.GetPlayerName(fromDeviceId);
            _combatResolved = false;
            _combatPending = true;
            _uiMode = UiMode.Combat;
            Log($"OnAttackReceived enter combat uiMode={_uiMode} target=0x{fromDeviceId:X2} name={_combatTarget}");

            _display.ShowCombat(_combatTarget, _state.CombatScore);
            MarkDisplayActivity("combat-defend");

            Log($"OnAttackReceived queue AttackAck score={_state.CombatScore}");
            QueuePacket(_builder.AttackAck(DeviceId, fromDeviceId, _state.CombatScore));

            Log("OnAttackReceived starting CombatTimeoutLoop thread");
            new Thread(CombatTimeoutLoop).Start();
        }

        public void OnAttackAckReceived(byte fromDeviceId, byte theirScore)
        {
            Log($"OnAttackAckReceived from=0x{fromDeviceId:X2} theirScore={theirScore} pending={_combatPending} uiMode={_uiMode}");
            // Ignore delayed / stale ACKs once we already left combat.
            if (!_combatPending || _uiMode != UiMode.Combat)
            {
                Log($"OnAttackAckReceived stale ignored from=0x{fromDeviceId:X2}");
                return;
            }

            if (fromDeviceId != _combatTargetDeviceId)
            {
                Log($"OnAttackAckReceived ignored from unexpected target=0x{fromDeviceId:X2} expected=0x{_combatTargetDeviceId:X2}");
                return;
            }

            _combatResolved = true;
            _combatPending = false;
            Log($"OnAttackAckReceived combat resolved pending={_combatPending} resolved={_combatResolved}");

            bool iWon = ResolveCombat(_state.CombatScore, theirScore);
            Log($"OnAttackAckReceived result iWon={iWon} myScore={_state.CombatScore} theirScore={theirScore}");

            if (iWon)
            {
                Log("OnAttackAckReceived queue CombatResult");
                QueuePacket(_builder.CombatResult(DeviceId, fromDeviceId, DeviceId));

                _uiMode = UiMode.CombatResult;
                Log($"OnAttackAckReceived ShowCombatResult won uiMode={_uiMode}");
                _display.ShowCombatResult(true, _combatTarget);
                MarkDisplayActivity("combat-result-win");

                new Thread(() =>
                {
                    Log($"OnAttackAckReceived win delay sleep={CombatResultDelayMs}");
                    Thread.Sleep(CombatResultDelayMs);
                    _combatTarget = null;
                    _combatTargetDeviceId = 0;
                    Log("OnAttackAckReceived win delay entering HUD");
                    EnterHud();
                }).Start();
            }
            else
            {
                Log("OnAttackAckReceived lost TakeDamage");
                _state.TakeDamage();
                _uiMode = UiMode.CombatResult;
                Log($"OnAttackAckReceived ShowCombatResult lost uiMode={_uiMode} lives={_state.Lives} state={_state.State}");
                _display.ShowCombatResult(false, _combatTarget);
                MarkDisplayActivity("combat-result-loss");

                new Thread(() =>
                {
                    Log($"OnAttackAckReceived lose delay sleep={CombatResultDelayMs}");
                    Thread.Sleep(CombatResultDelayMs);
                    Log("OnAttackAckReceived lose delay ShowPostDamageScreen");
                    ShowPostDamageScreen();
                }).Start();
            }
        }

        public void OnCombatResultReceived(byte fromDeviceId, byte winnerId)
        {
            Log($"OnCombatResultReceived from=0x{fromDeviceId:X2} winner=0x{winnerId:X2} state={_state.State} uiMode={_uiMode} pending={_combatPending} resolved={_combatResolved}");
            // Ignore duplicate or irrelevant results once already dead/idle.
            if (IsDead || _state.State == GameState.Idle)
            {
                Log("OnCombatResultReceived ignored because dead or idle");
                return;
            }

            if (winnerId == DeviceId)
            {
                Log("OnCombatResultReceived ignored because we won");
                return;
            }

            _combatResolved = true;
            _combatPending = false;
            _combatTarget = null;
            _combatTargetDeviceId = 0;
            Log($"OnCombatResultReceived resolving as loss pending={_combatPending} resolved={_combatResolved}");

            _state.TakeDamage();
            _uiMode = UiMode.CombatResult;
            Log($"OnCombatResultReceived ShowCombatResult uiMode={_uiMode} lives={_state.Lives} state={_state.State}");
            _display.ShowCombatResult(false, _combatTarget);
            MarkDisplayActivity("combat-result-rx");

            new Thread(() =>
            {
                Log($"OnCombatResultReceived delay sleep={CombatResultDelayMs}");
                Thread.Sleep(CombatResultDelayMs);
                Log("OnCombatResultReceived delay ShowPostDamageScreen");
                ShowPostDamageScreen();
            }).Start();
        }

        public void OnFlagTransferReceived(byte fromDeviceId, byte[] key)
        {
            Log($"OnFlagTransferReceived from=0x{fromDeviceId:X2} keyLen={(key == null ? 0 : key.Length)} state={_state.State} uiMode={_uiMode}");
            if (IsDead || _state.State == GameState.Idle)
            {
                Log("OnFlagTransferReceived ignored because dead or idle");
                return;
            }

            _state.PickupFlag(key);
            Log($"OnFlagTransferReceived picked up flag hasFlag={_state.HasFlag}");

            if (_uiMode == UiMode.Hud)
            {
                Log("OnFlagTransferReceived UpdateHasFlag");
                _display.UpdateHasFlag(true);
                MarkDisplayActivity("flag-transfer");
            }
        }

        public void OnKeyGrantReceived(byte fromFlagNodeId, byte[] key)
        {
            Log($"OnKeyGrantReceived fromFlagNode=0x{fromFlagNodeId:X2} keyLen={(key == null ? 0 : key.Length)} state={_state.State} uiMode={_uiMode}");
            if (IsDead || _state.State == GameState.Idle)
            {
                Log("OnKeyGrantReceived ignored because dead or idle");
                return;
            }

            _state.PickupFlag(key);
            Log($"OnKeyGrantReceived picked up flag hasFlag={_state.HasFlag}");

            if (_uiMode == UiMode.Hud)
            {
                Log("OnKeyGrantReceived UpdateHasFlag");
                _display.UpdateHasFlag(true);
                MarkDisplayActivity("key-grant");
            }
        }

        public void OnRespawnAckReceived(byte newCombatScore)
        {
            Log($"OnRespawnAckReceived newScore={newCombatScore} state={_state.State} uiMode={_uiMode}");
            _state.Respawn(newCombatScore);
            ResetCombatFlow();
            EnterHud();
            Log($"OnRespawnAckReceived end state={_state.State} uiMode={_uiMode}");
        }

        // ---------------------------------------------------------------
        // Game lifecycle
        // ---------------------------------------------------------------

        public override void OnGameStart()
        {
            Log($"OnGameStart begin state={_state.State} uiMode={_uiMode}");
            _state.SetState(GameState.Active);
            ResetCombatFlow();
            EnterHud();
            StartBackgroundServices();
            Log($"OnGameStart end state={_state.State} uiMode={_uiMode}");
        }

        public override void OnGameEnd(byte winnerId)
        {
            Log($"OnGameEnd begin winner=0x{winnerId:X2} state={_state.State} uiMode={_uiMode}");
            _state.SetState(GameState.Idle);
            ResetCombatFlow();

            _uiMode = UiMode.Message;
            _combatListDirty = false;
            ResetCombatListSnapshot();
            Log($"OnGameEnd render final HUD state={_state.State} uiMode={_uiMode}");

            // Keeping this compile-safe with the existing render methods you already use.
            _display.RenderHud(_state.ToHudData());
            MarkDisplayActivity("game-end");
            Log($"OnGameEnd end state={_state.State} uiMode={_uiMode}");
        }

        // ---------------------------------------------------------------
        // TX
        // ---------------------------------------------------------------

        public void SendHeartbeat() { /* owned by HeartbeatLoop */ }

        public void SendFlagTransfer(byte targetDeviceId, byte[] key)
            => QueuePacket(_builder.FlagTransfer(DeviceId, targetDeviceId, key));

        public void SendCapture(byte flagNodeId)
            => QueuePacket(_builder.Capture(DeviceId, flagNodeId));

        public void SendDeliver(byte flagNodeId, byte[] key)
            => QueuePacket(_builder.Deliver(DeviceId, flagNodeId, key));

        public void SendRespawnRequest(byte flagNodeId)
            => QueuePacket(_builder.RespawnReq(DeviceId, flagNodeId));

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private void QueuePacket(IPacket packet)
        {
            byte[] bytes = packet.ToBytes();
            bool queued = _txQueue.Enqueue(bytes);
            Log($"QueuePacket type=0x{packet.PacketType:X2} target=0x{packet.TargetId:X2} queued={queued} queueCount={_txQueue.Count}");
        }

        private bool ShouldIgnoreIncomingPacket(byte[] raw)
        {
            if (!IsCombatRadioWindow || raw == null || raw.Length < 2)
                return false;

            byte msgType = raw[1];
            switch (msgType)
            {
                case PacketType.AttackAck:
                case PacketType.CombatResult:
                case PacketType.RespawnAck:
                    return false;

                default:
                    return true;
            }
        }

        private void ResetCombatFlow()
        {
            Log($"ResetCombatFlow begin pending={_combatPending} resolved={_combatResolved} target={_combatTarget}");
            _combatPending = false;
            _combatResolved = true;
            _combatTarget = null;
            _combatTargetDeviceId = 0;
            Log($"ResetCombatFlow end pending={_combatPending} resolved={_combatResolved} target={_combatTarget}");
        }

        private bool ShouldRenderHeartbeatIndicator()
        {
            long nowTick = DateTime.UtcNow.Ticks;
            lock (_displayActivityLock)
            {
                long sinceActivityMs = (nowTick - _lastDisplayActivityTick) / TicksPerMillisecond;
                if (sinceActivityMs < HeartbeatIndicatorQuietWindowMs)
                    return false;

                long sinceHeartbeatMs = (nowTick - _lastHeartbeatIndicatorTick) / TicksPerMillisecond;
                return sinceHeartbeatMs >= HeartbeatIndicatorIntervalMs;
            }
        }

        private void MarkDisplayActivity(string reason)
        {
            long nowTick = DateTime.UtcNow.Ticks;
            lock (_displayActivityLock)
            {
                _lastDisplayActivityTick = nowTick;
                if (reason == "heartbeat")
                    _lastHeartbeatIndicatorTick = nowTick;
            }

            if (VerboseTickLogging)
                Log($"MarkDisplayActivity reason={reason}");
        }

        private void EnterHud()
        {
            Log($"EnterHud begin state={_state.State} uiMode={_uiMode}");
            _uiMode = UiMode.Hud;
            RenderHudAndList();
            Log($"EnterHud end state={_state.State} uiMode={_uiMode}");
        }

        private void RenderHudAndList()
        {
            Log("RenderHudAndList begin");
            _state.RefreshCombatList();

            string[] targets = _state.GetCombatTargets(out int count);
            int selectedIndex = _state.SelectedIndex;
            Log($"RenderHudAndList targets count={count} selected={selectedIndex}");

            Log("RenderHudAndList RenderHud+List begin");
            _display.RenderHud(_state.ToHudData(), targets, count, selectedIndex);
            MarkDisplayActivity("hud-full");
            Log("RenderHudAndList RenderHud+List end");
            CacheCombatListSnapshot(targets, count, selectedIndex);

            _combatListDirty = false;
            Log("RenderHudAndList end dirty=false");
        }

        private void ShowPostDamageScreen()
        {
            Log($"ShowPostDamageScreen begin state={_state.State} lives={_state.Lives} uiMode={_uiMode}");
            if (IsDead || _state.Lives == 0)
            {
                _uiMode = UiMode.Dead;
                Log($"ShowPostDamageScreen ShowDead uiMode={_uiMode}");
                _display.ShowDead(_state.Lives);
                MarkDisplayActivity("dead");
            }
            else
            {
                _combatTarget = null;
                Log("ShowPostDamageScreen entering HUD");
                EnterHud();
            }
            Log($"ShowPostDamageScreen end state={_state.State} uiMode={_uiMode}");
        }

        private void StartBackgroundServices()
        {
            if (_started)
            {
                Log("StartBackgroundServices ignored - already started");
                return;
            }

            Log("StartBackgroundServices begin");
            _started = true;
            _running = true;

            Log("StartBackgroundServices starting LoRa polling");
            _lora.StartPolling();

            Log("StartBackgroundServices creating threads");
            _heartbeatThread = new Thread(HeartbeatLoop);
            _txThread = new Thread(TxLoop);
            _uiThread = new Thread(UiLoop);

            Log("StartBackgroundServices starting heartbeat thread");
            _heartbeatThread.Start();
            Log("StartBackgroundServices starting tx thread");
            _txThread.Start();
            Log("StartBackgroundServices starting ui thread");
            _uiThread.Start();
            Log("StartBackgroundServices end");
        }

        private bool HasCombatListChanged(string[] targets, int count, int selectedIndex)
        {
            if (count != _lastRenderedCount)
            {
                if (VerboseTickLogging) Log($"HasCombatListChanged true count current={count} previous={_lastRenderedCount}");
                return true;
            }

            if (selectedIndex != _lastRenderedSelectedIndex)
            {
                if (VerboseTickLogging) Log($"HasCombatListChanged true selected current={selectedIndex} previous={_lastRenderedSelectedIndex}");
                return true;
            }

            for (int i = 0; i < MaxRenderedTargets; i++)
            {
                string current = i < count ? targets[i] : null;
                string previous = i < _lastRenderedCount ? _lastRenderedTargets[i] : null;

                if (current != previous)
                {
                    if (VerboseTickLogging) Log($"HasCombatListChanged true index={i} current={current} previous={previous}");
                    return true;
                }
            }

            if (VerboseTickLogging) Log("HasCombatListChanged false");
            return false;
        }

        private void CacheCombatListSnapshot(string[] targets, int count, int selectedIndex)
        {
            Log($"CacheCombatListSnapshot begin count={count} selected={selectedIndex}");
            for (int i = 0; i < MaxRenderedTargets; i++)
                _lastRenderedTargets[i] = i < count ? targets[i] : null;

            _lastRenderedCount = count;
            _lastRenderedSelectedIndex = selectedIndex;
            Log($"CacheCombatListSnapshot end count={_lastRenderedCount} selected={_lastRenderedSelectedIndex}");
        }

        private void ResetCombatListSnapshot()
        {
            Log("ResetCombatListSnapshot begin");
            for (int i = 0; i < MaxRenderedTargets; i++)
                _lastRenderedTargets[i] = null;

            _lastRenderedCount = -1;
            _lastRenderedSelectedIndex = -1;
            Log("ResetCombatListSnapshot end");
        }

        public bool ResolveCombat(byte mine, byte theirs)
        {
            Log($"ResolveCombat begin mine={mine} theirs={theirs}");
            // Special wraparound rule: 1 beats 10
            if (mine == 1 && theirs == 10)
            {
                Log("ResolveCombat true by wraparound");
                return true;
            }

            if (mine == 10 && theirs == 1)
            {
                Log("ResolveCombat false by wraparound");
                return false;
            }

            // Normal rule: higher score wins
            bool result = mine > theirs;
            Log($"ResolveCombat result={result}");
            return result;
        }

        // ---------------------------------------------------------------
        // Stubs
        // ---------------------------------------------------------------

        public void EnterActive() { throw new NotImplementedException(); }
        public void EnterCapturing(byte flagNodeId) { throw new NotImplementedException(); }
        public void EnterDelivering(byte flagNodeId) { throw new NotImplementedException(); }
        public void EnterStunned() { throw new NotImplementedException(); }

        private void Log(string message)
        {
            if (VerboseLogging)
                DebugLog.Write("[GamerDevice] " + message);
        }
    }
}
