using IOD.CaptureTheFlag.NanoFramework.Interfaces;
using IOD.CaptureTheFlag.NanoFramework.Types;
using Iot.Device.EPaper;
using Iot.Device.EPaper.Drivers.LcmEn2r13;
using Iot.Device.EPaper.Fonts;
using System.Drawing;
using System.Text;
using System.Diagnostics;
using System.Threading;

namespace IOD.CaptureTheFlag.NanoFramework.Implementations
{
    public class DisplayDriver : IDisplayDriver
    {
        private readonly LcmEn2r13 _display;
        private readonly Graphics _graphics;
        private readonly Font8x12 _font;
        private readonly object _lock = new object();
        private const bool VerbosePartialLogging = false;
        private const bool UsePartialHeartbeatRefresh = true;
        private const bool UsePartialHudListRefresh = true;
        private const bool UsePartialCombatBarRefresh = true;
        private const int MaxRenderedTargets = 16;

        /// <summary>
        /// Power-cycle the panel before full-frame updates so the controller leaves partial/unknown
        /// state. Slow but stable; set false to tune speed during development.
        /// </summary>
        private const bool PowerCycleBeforeFullRefresh = true;

        private const int PowerCycleDownMs = 200;
        private const int PowerCycleUpMs = 200;

        private enum ScreenMode
        {
            Message,
            Hud,
            Combat,
            CombatResult,
            Dead
        }

        private ScreenMode _screenMode = ScreenMode.Message;

        // ---------------------------------------------------------------
        // Layout constants (~30% stats column, ~70% combat list)
        // ---------------------------------------------------------------

        private readonly int _dividerX;
        private readonly int _combatListX;
        private const int CharWidth = 8;
        /// <summary>Width reserved for the 3-bar signal glyph.</summary>
        private const int SignalBarBlockW = 16;
        /// <summary>Space for RSSI text (dBm), e.g. <c>-128</c>, for tuning bar thresholds in the field.</summary>
        private const int SignalRssiTextW = 40;
        private const int SignalColumnW = SignalBarBlockW + SignalRssiTextW;
        private const int UnknownRssi = -200;

        private const int HeartX = 4;
        private const int HeartY = 6;
        private const int HeartW = 10;
        private const int HeartH = 10;

        private const int SwordX = 4;
        private const int SwordY = 42;
        private const int SwordW = 20;
        private const int SwordH = 20;

        private const int FlagX = 4;
        private const int FlagY = 90;
        private const int FlagW = 14;
        private const int FlagH = 16;

        private const int HeartTextX = HeartX + HeartW + 2;
        private const int HeartTextY = HeartY + 1;
        private const int SwordTextX = SwordX + SwordW + 2;
        private const int SwordTextY = SwordY + 6;
        private const int FlagTextX = FlagX + FlagW + 2;
        private const int FlagTextY = FlagY + 2;

        private const int CombatListStartY = 6;
        private const int CombatListSpacing = 16;

        private const int SkullW = 24;
        private const int SkullH = 20;

        private const int BarWidth = 16;
        private const int BarTotalWidth = (BarWidth + 2) * CharWidth;
        private const int BarX = (250 - BarTotalWidth) / 2;
        private const int BarY = 88;

        private bool _heartState = false;
        private HudData _lastHudData;
        private string _lastCombatTargetName;
        private byte _lastCombatScore;
        private readonly string[] _lastTargets = new string[MaxRenderedTargets];
        private readonly int[] _lastTargetRssi = new int[MaxRenderedTargets];
        private int _lastTargetCount = 0;
        private int _lastSelectedIndex = 0;

        public DisplayDriver(LcmEn2r13 display, Graphics graphics, Font8x12 font)
        {
            _display = display;
            _graphics = graphics;
            _font = font;
            int w = _graphics.Width;
            _dividerX = (w * 30) / 100;
            if (_dividerX < 56)
                _dividerX = 56;
            if (_dividerX > w - 56)
                _dividerX = w - 56;

            _combatListX = _dividerX + 4;
            for (int i = 0; i < MaxRenderedTargets; i++)
                _lastTargetRssi[i] = UnknownRssi;

            Log($"ctor width={w} height={_graphics.Height} dividerX={_dividerX} combatListX={_combatListX} mode={_screenMode}");
        }

        public void PowerOn()
        {
            Log("PowerOn begin");
            _display.PowerOn();
            Log("PowerOn end");
        }

        public void PowerDown()
        {
            Log("PowerDown begin");
            _display.PowerDown();
            Log("PowerDown end");
        }

        public void Clear()
        {
            lock (_lock)
            {
                Log($"Clear begin mode={_screenMode}");
                BeginFullFrameUnsafe(ScreenMode.Message, false, "Clear");
                CommitFullRefreshUnsafe("Clear");
                Log($"Clear end mode={_screenMode}");
            }
        }

        public void ShowMessage(string line1, string line2 = null)
        {
            lock (_lock)
            {
                Log($"ShowMessage begin mode={_screenMode} line1={line1} line2={line2}");

                BeginFullFrameUnsafe(ScreenMode.Message, false, "ShowMessage");

                Log("ShowMessage draw line1");
                _graphics.DrawText(line1, _font, 10, 40, Color.Black);
                if (line2 != null)
                {
                    Log("ShowMessage draw line2");
                    _graphics.DrawText(line2, _font, 10, 56, Color.Black);
                }

                CommitFullRefreshUnsafe("ShowMessage");
                Log($"ShowMessage end mode={_screenMode}");
            }
        }

        public void RenderHud(HudData data)
        {
            lock (_lock)
            {
                Log($"RenderHud begin mode={_screenMode} lives={data.Lives} score={data.CombatScore} hasFlag={data.HasFlag} timer={data.Timer}");

                _lastHudData = data;
                RenderHudFrameUnsafe(data, _lastTargets, _lastTargetCount, _lastSelectedIndex);
                Log($"RenderHud end mode={_screenMode}");
            }
        }

        public void RenderHud(HudData data, string[] targets, int count, int selectedIndex)
        {
            RenderHud(data, targets, count, selectedIndex, null);
        }

        public void RenderHud(HudData data, string[] targets, int count, int selectedIndex, int[] targetRssi)
        {
            lock (_lock)
            {
                Log($"RenderHud+List begin mode={_screenMode} lives={data.Lives} score={data.CombatScore} hasFlag={data.HasFlag} timer={data.Timer} count={count} selected={selectedIndex}");

                _lastHudData = data;
                CacheTargetsUnsafe(targets, count, selectedIndex, targetRssi);
                RenderHudFrameUnsafe(data, _lastTargets, _lastTargetCount, _lastSelectedIndex);
                Log($"RenderHud+List end mode={_screenMode}");
            }
        }

        public void ShowCombat(string targetName, byte myScore)
        {
            lock (_lock)
            {
                Log($"ShowCombat begin mode={_screenMode} target={targetName} score={myScore}");

                _lastCombatTargetName = targetName;
                _lastCombatScore = myScore;
                RenderCombatFrameUnsafe(targetName, myScore, 0, 8, powerCycleBeforeDraw: true);
                Log($"ShowCombat end mode={_screenMode}");
            }
        }

        public void UpdateCombatBar(int second, int totalSeconds)
        {
            lock (_lock)
            {
                Log($"UpdateCombatBar begin mode={_screenMode} second={second} total={totalSeconds}");
                if (_screenMode != ScreenMode.Combat)
                {
                    Log("UpdateCombatBar ignored because mode is not Combat");
                    return;
                }

                Log("UpdateCombatBar erase bar");
                if (UsePartialCombatBarRefresh)
                {
                    RefreshCombatBarPartialUnsafe(second, totalSeconds);
                }
                else
                {
                    RenderCombatFrameUnsafe(_lastCombatTargetName, _lastCombatScore, second, totalSeconds, powerCycleBeforeDraw: false);
                }
                Log("UpdateCombatBar end");
            }
        }

        public void ShowCombatResult(bool won, string targetName)
        {
            lock (_lock)
            {
                Log($"ShowCombatResult begin mode={_screenMode} won={won} target={targetName}");

                BeginFullFrameUnsafe(ScreenMode.CombatResult, false, "ShowCombatResult");

                string line1 = won ? "VICTORY!" : "DEFEATED";
                string line2 = targetName != null
                    ? (won ? $"vs {targetName}" : $"by {targetName}")
                    : string.Empty;

                Log($"ShowCombatResult draw line1={line1}");
                _graphics.DrawText(line1, _font, CentreX(line1.Length), 44, Color.Black);

                if (line2.Length > 0)
                {
                    Log($"ShowCombatResult draw line2={line2}");
                    _graphics.DrawText(line2, _font, CentreX(line2.Length), 60, Color.Black);
                }

                CommitFullRefreshUnsafe("ShowCombatResult");
                Log($"ShowCombatResult end mode={_screenMode}");
            }
        }

        public void ShowDead(byte lives)
        {
            lock (_lock)
            {
                Log($"ShowDead begin mode={_screenMode} lives={lives}");

                BeginFullFrameUnsafe(ScreenMode.Dead, true, "ShowDead");

                int skullX = (_graphics.Width - SkullW) / 2;
                int skullY = 12;
                Log($"ShowDead draw skull x={skullX} y={skullY}");
                DrawBitmapInvertedUnsafe(Icons.Skull, skullX, skullY);

                Log("ShowDead draw title");
                _graphics.DrawText("YOU DIED", _font, CentreX(8), skullY + SkullH + 8, Color.White);

                string livesText = lives == 0 ? "No lives left" : $"Lives left: {lives}";
                Log($"ShowDead draw livesText={livesText}");
                _graphics.DrawText(livesText, _font, CentreX(livesText.Length), skullY + SkullH + 24, Color.White);

                CommitFullRefreshUnsafe("ShowDead");
                Log($"ShowDead end mode={_screenMode}");
            }
        }

        public void UpdateHeartbeat()
        {
            lock (_lock)
            {
                if (VerbosePartialLogging) Log($"UpdateHeartbeat begin mode={_screenMode} heartState={_heartState}");
                if (_screenMode != ScreenMode.Hud)
                {
                    if (VerbosePartialLogging) Log("UpdateHeartbeat ignored because mode is not Hud");
                    return;
                }

                _heartState = !_heartState;
                if (VerbosePartialLogging) Log($"UpdateHeartbeat toggled heartState={_heartState}");
                if (UsePartialHeartbeatRefresh)
                {
                    RefreshHeartbeatPartialUnsafe();
                }
                if (VerbosePartialLogging) Log("UpdateHeartbeat end");
            }
        }

        public void UpdateLives(byte value)
        {
            lock (_lock)
            {
                if (VerbosePartialLogging) Log($"UpdateLives begin mode={_screenMode} value={value}");
                if (_screenMode != ScreenMode.Hud)
                {
                    if (VerbosePartialLogging) Log("UpdateLives ignored because mode is not Hud");
                    return;
                }

                if (_lastHudData != null) _lastHudData.Lives = value;
                if (_lastHudData != null)
                {
                    RenderHudFrameUnsafe(_lastHudData, _lastTargets, _lastTargetCount, _lastSelectedIndex);
                }
                if (VerbosePartialLogging) Log("UpdateLives end");
            }
        }

        public void UpdateCombatScore(byte value)
        {
            lock (_lock)
            {
                if (VerbosePartialLogging) Log($"UpdateCombatScore begin mode={_screenMode} value={value}");
                if (_screenMode != ScreenMode.Hud)
                {
                    if (VerbosePartialLogging) Log("UpdateCombatScore ignored because mode is not Hud");
                    return;
                }

                if (_lastHudData != null) _lastHudData.CombatScore = value;
                if (_lastHudData != null)
                {
                    RenderHudFrameUnsafe(_lastHudData, _lastTargets, _lastTargetCount, _lastSelectedIndex);
                }
                if (VerbosePartialLogging) Log("UpdateCombatScore end");
            }
        }

        public void UpdateHasFlag(bool value)
        {
            lock (_lock)
            {
                if (VerbosePartialLogging) Log($"UpdateHasFlag begin mode={_screenMode} value={value}");
                if (_screenMode != ScreenMode.Hud)
                {
                    if (VerbosePartialLogging) Log("UpdateHasFlag ignored because mode is not Hud");
                    return;
                }

                if (_lastHudData != null) _lastHudData.HasFlag = value;
                if (_lastHudData != null)
                {
                    RenderHudFrameUnsafe(_lastHudData, _lastTargets, _lastTargetCount, _lastSelectedIndex);
                }
                if (VerbosePartialLogging) Log("UpdateHasFlag end");
            }
        }

        public void UpdateCombatList(string[] targets, int count, int selectedIndex)
        {
            UpdateCombatList(targets, count, selectedIndex, null);
        }

        public void UpdateCombatList(string[] targets, int count, int selectedIndex, int[] targetRssi)
        {
            lock (_lock)
            {
                if (VerbosePartialLogging) Log($"UpdateCombatList begin mode={_screenMode} count={count} selected={selectedIndex}");
                if (_screenMode != ScreenMode.Hud)
                {
                    if (VerbosePartialLogging) Log("UpdateCombatList ignored because mode is not Hud");
                    if (CombatListDiagnostics.Enabled)
                    {
                        CombatListDiagnostics.Write(
                            "Display UpdateCombatList skipped: screenMode=" + _screenMode.ToString() + " count=" + count.ToString());
                    }

                    return;
                }

                if (CombatListDiagnostics.Enabled)
                {
                    CombatListDiagnostics.Write(
                        "Display UpdateCombatList drawing count=" + count.ToString() + " sel=" + selectedIndex.ToString());
                }

                CacheTargetsUnsafe(targets, count, selectedIndex, targetRssi);

                if (UsePartialHudListRefresh)
                {
                    RefreshCombatListPartialUnsafe();
                }
                else if (_lastHudData != null)
                {
                    RenderHudFrameUnsafe(_lastHudData, _lastTargets, _lastTargetCount, _lastSelectedIndex);
                }
                if (VerbosePartialLogging) Log("UpdateCombatList end");
            }
        }

        public void UpdateLastRx(string hex, int rssi)
        {
            lock (_lock)
            {
                if (VerbosePartialLogging) Log($"UpdateLastRx begin mode={_screenMode} hex={hex} rssi={rssi}");
                if (_screenMode != ScreenMode.Hud)
                {
                    if (VerbosePartialLogging) Log("UpdateLastRx ignored because mode is not Hud");
                    return;
                }

                if (_lastHudData != null)
                {
                    RenderHudFrameUnsafe(_lastHudData, _lastTargets, _lastTargetCount, _lastSelectedIndex);
                }
                if (VerbosePartialLogging) Log("UpdateLastRx end");
            }
        }

        public void UpdateLastTx(string hex)
        {
            lock (_lock)
            {
                if (VerbosePartialLogging) Log($"UpdateLastTx begin mode={_screenMode} hex={hex}");
                if (_screenMode != ScreenMode.Hud)
                {
                    if (VerbosePartialLogging) Log("UpdateLastTx ignored because mode is not Hud");
                    return;
                }

                if (_lastHudData != null)
                {
                    RenderHudFrameUnsafe(_lastHudData, _lastTargets, _lastTargetCount, _lastSelectedIndex);
                }
                if (VerbosePartialLogging) Log("UpdateLastTx end");
            }
        }

        private void WaitDisplayReadyUnsafe()
        {
            _display.WaitReady(CancellationToken.None);
        }

        private void PowerCycleDisplayUnsafe(string reason)
        {
            if (!PowerCycleBeforeFullRefresh)
            {
                return;
            }

            Log($"PowerCycleDisplayUnsafe begin reason={reason}");
            WaitDisplayReadyUnsafe();
            _display.PowerDown();
            Thread.Sleep(PowerCycleDownMs);
            _display.PowerOn();
            Thread.Sleep(PowerCycleUpMs);
            WaitDisplayReadyUnsafe();
            Log($"PowerCycleDisplayUnsafe end reason={reason}");
        }

        private void BeginFullFrameUnsafe(ScreenMode nextMode, bool inverted, string reason, bool powerCycleBeforeDraw = true)
        {
            Log($"BeginFullFrameUnsafe {_screenMode}->{nextMode} reason={reason}");

            if (powerCycleBeforeDraw)
            {
                PowerCycleDisplayUnsafe(reason);
            }
            else
            {
                WaitDisplayReadyUnsafe();
            }

            Log($"BeginFullFrameUnsafe {reason} BeginFrameDraw");
            _display.BeginFrameDraw();
            _screenMode = nextMode;
            Log($"BeginFullFrameUnsafe mode set={_screenMode}");

            if (inverted)
                ClearScreenBlackUnsafe();
            else
                ClearScreenUnsafe();
        }

        private void CommitFullRefreshUnsafe(string reason)
        {
            Log($"CommitFullRefreshUnsafe {reason} Flush");
            _display.Flush();
            Log($"CommitFullRefreshUnsafe {reason} PerformFullRefresh");
            _display.PerformFullRefresh();
            WaitDisplayReadyUnsafe();
        }

        private void RefreshCombatListPartialUnsafe()
        {
            Log("RefreshCombatListPartialUnsafe begin");
            EraseBlockUnsafe(_dividerX + 1, 0, _graphics.Width - _dividerX - 1, _graphics.Height);
            DrawCombatListUnsafe(_lastTargets, _lastTargetCount, _lastSelectedIndex);
            PartialRefreshUnsafe();
            Log("RefreshCombatListPartialUnsafe end");
        }

        private void RefreshCombatBarPartialUnsafe(int second, int totalSeconds)
        {
            Log($"RefreshCombatBarPartialUnsafe begin second={second} total={totalSeconds}");
            EraseBlockUnsafe(BarX, BarY - 2, BarTotalWidth, 16);
            DrawBarUnsafe(second, totalSeconds);
            PartialRefreshUnsafe();
            Log("RefreshCombatBarPartialUnsafe end");
        }

        private void RefreshHeartbeatPartialUnsafe()
        {
            Log($"RefreshHeartbeatPartialUnsafe begin heartState={_heartState}");
            EraseBlockUnsafe(HeartX, HeartY, HeartW, HeartH);
            DrawBitmapUnsafe(_heartState ? Icons.HeartBeat : Icons.HeartNormal, HeartX, HeartY);
            PartialRefreshUnsafe();
            Log("RefreshHeartbeatPartialUnsafe end");
        }

        private void RenderHudFrameUnsafe(HudData data, string[] targets, int count, int selectedIndex)
        {
            BeginFullFrameUnsafe(ScreenMode.Hud, false, "RenderHud");

            Log("RenderHudFrameUnsafe draw divider");
            _graphics.DrawLine(_dividerX, 0, _dividerX, _graphics.Height, Color.Black);

            Log($"RenderHudFrameUnsafe draw heart state={_heartState}");
            DrawBitmapUnsafe(_heartState ? Icons.HeartBeat : Icons.HeartNormal, HeartX, HeartY);
            _graphics.DrawText($":{data.Lives}", _font, HeartTextX, HeartTextY, Color.Black);

            Log("RenderHudFrameUnsafe draw score");
            DrawBitmapUnsafe(Icons.Reticle, SwordX, SwordY);
            _graphics.DrawText($":{data.CombatScore}", _font, SwordTextX, SwordTextY, Color.Black);

            Log("RenderHudFrameUnsafe draw flag");
            DrawBitmapUnsafe(Icons.Flag, FlagX, FlagY);
            _graphics.DrawText($":{(data.HasFlag ? "Y" : "N")}", _font, FlagTextX, FlagTextY, Color.Black);

            Log($"RenderHudFrameUnsafe draw targets count={count} selected={selectedIndex}");
            DrawCombatListUnsafe(targets, count, selectedIndex);

            CommitFullRefreshUnsafe("RenderHud");
        }

        private void RenderCombatFrameUnsafe(string targetName, byte myScore, int second, int totalSeconds, bool powerCycleBeforeDraw = true)
        {
            if (targetName == null)
                targetName = string.Empty;

            BeginFullFrameUnsafe(ScreenMode.Combat, false, "RenderCombat", powerCycleBeforeDraw);

            int swordX = (_graphics.Width - SwordW) / 2;
            Log($"RenderCombatFrameUnsafe draw sword x={swordX}");
            DrawBitmapUnsafe(Icons.Reticle, swordX, 10);

            Log("RenderCombatFrameUnsafe draw top divider");
            _graphics.DrawLine(0, 36, _graphics.Width, 36, Color.Black);

            string versus = "vs " + targetName;
            Log($"RenderCombatFrameUnsafe draw target text={versus}");
            _graphics.DrawText(versus, _font, CentreX(versus.Length), 44, Color.Black);

            Log("RenderCombatFrameUnsafe draw middle divider");
            _graphics.DrawLine(0, 66, _graphics.Width, 66, Color.Black);

            string score = "Score: " + myScore;
            Log($"RenderCombatFrameUnsafe draw score text={score}");
            _graphics.DrawText(score, _font, CentreX(score.Length), 72, Color.Black);

            Log("RenderCombatFrameUnsafe draw bar");
            DrawBarUnsafe(second, totalSeconds);

            CommitFullRefreshUnsafe("RenderCombat");
        }

        private void DrawCombatListUnsafe(string[] targets, int count, int selectedIndex)
        {
            if (targets == null || count <= 0)
                return;

            int limit = count;
            if (limit > targets.Length)
                limit = targets.Length;

            for (int i = 0; i < limit; i++)
            {
                if (targets[i] == null)
                    continue;

                int rowY = CombatListStartY + (i * CombatListSpacing);
                int rssiVal = _lastTargetRssi[i];
                DrawSignalStrengthUnsafe(_combatListX, rowY + 2, rssiVal);
                DrawRssiTextUnsafe(_combatListX + SignalBarBlockW, rowY, rssiVal);

                _graphics.DrawText(
                    $"{(i == selectedIndex ? ">" : " ")}{targets[i]}",
                    _font,
                    _combatListX + SignalColumnW,
                    rowY,
                    Color.Black);
            }
        }

        /// <summary>Maps RSSI (dBm) to 0–3 filled bars (mobile-style), field-tuned.</summary>
        private static int RssiToBarCount(int rssi)
        {
            if (rssi >= 0 || rssi <= UnknownRssi + 1)
                return 0;
            // Stronger than -40 dBm → 3 bars; between -60 and -40 (exclusive of -60) → 2 bars; -60 and weaker → 1 bar
            if (rssi > -40)
                return 3;
            if (rssi > -60)
                return 2;
            return 1;
        }

        private void DrawSignalStrengthUnsafe(int xBase, int yBase, int rssi)
        {
            int level = RssiToBarCount(rssi);
            const int barW = 2;
            const int gap = 3;
            int bottom = yBase + 10;
            for (int b = 0; b < 3; b++)
            {
                int h = 4 + (b * 2);
                int x = xBase + (b * (barW + gap));
                int y = bottom - h;
                // Outlined-only rectangles on 1-bit e-paper often read as solid blocks; wipe then fill.
                _graphics.DrawRectangle(x, y, barW, h, Color.White, true);
                if (b < level)
                    _graphics.DrawRectangle(x, y, barW, h, Color.Black, true);
            }
        }

        private void DrawRssiTextUnsafe(int x, int y, int rssi)
        {
            string label;
            if (rssi >= 0 || rssi <= UnknownRssi + 1)
                label = "  ? ";
            else
                label = rssi.ToString();

            _graphics.DrawText(label, _font, x, y, Color.Black);
        }

        private void CacheTargetsUnsafe(string[] targets, int count, int selectedIndex, int[] rssi)
        {
            for (int i = 0; i < MaxRenderedTargets; i++)
            {
                _lastTargets[i] = null;
                _lastTargetRssi[i] = UnknownRssi;
            }

            _lastTargetCount = 0;
            _lastSelectedIndex = selectedIndex;

            if (targets == null || count <= 0)
                return;

            int limit = count;
            if (limit > MaxRenderedTargets)
                limit = MaxRenderedTargets;
            if (limit > targets.Length)
                limit = targets.Length;

            for (int i = 0; i < limit; i++)
            {
                _lastTargets[i] = targets[i];
                if (rssi != null && i < rssi.Length)
                    _lastTargetRssi[i] = rssi[i];
                _lastTargetCount++;
            }
        }

        private void ClearScreenUnsafe()
        {
            Log($"ClearScreenUnsafe framebuffer color=White");
            _graphics.EPaperDisplay.FrameBuffer.Clear(Color.White);
        }

        private void ClearScreenBlackUnsafe()
        {
            Log($"ClearScreenBlackUnsafe framebuffer color=Black");
            _graphics.EPaperDisplay.FrameBuffer.Clear(Color.Black);
        }

        private void DrawBitmapUnsafe(byte[][] bitmap, int x, int y)
        {
            for (int row = 0; row < bitmap.Length; row++)
            {
                for (int col = 0; col < bitmap[row].Length; col++)
                {
                    if (bitmap[row][col] == 1)
                        _graphics.DrawPixel(x + col, y + row, Color.Black);
                }
            }
        }

        private void DrawBitmapInvertedUnsafe(byte[][] bitmap, int x, int y)
        {
            for (int row = 0; row < bitmap.Length; row++)
            {
                for (int col = 0; col < bitmap[row].Length; col++)
                {
                    if (bitmap[row][col] == 1)
                        _graphics.DrawPixel(x + col, y + row, Color.White);
                }
            }
        }

        private void EraseBlockUnsafe(int x, int y, int w, int h)
        {
            if (VerbosePartialLogging) Log($"EraseBlockUnsafe x={x} y={y} w={w} h={h}");
            _graphics.DrawRectangle(x, y, w, h, Color.White, true);
        }

        private void PartialRefreshUnsafe()
        {
            if (VerbosePartialLogging) Log("PartialRefreshUnsafe Flush begin");
            _display.Flush();
            if (VerbosePartialLogging) Log("PartialRefreshUnsafe Flush end");
            if (VerbosePartialLogging) Log("PartialRefreshUnsafe PerformPartialRefresh begin");
            _display.PerformPartialRefresh();
            if (VerbosePartialLogging) Log("PartialRefreshUnsafe PerformPartialRefresh end");
        }

        private int CentreX(int charCount)
        {
            int x = (_graphics.Width - (charCount * CharWidth)) / 2;
            Log($"CentreX chars={charCount} x={x}");
            return x;
        }

        private void DrawBarUnsafe(int second, int totalSeconds)
        {
            int filled = totalSeconds == 0
                ? 0
                : (second * (BarWidth - 2)) / totalSeconds;
            Log($"DrawBarUnsafe second={second} total={totalSeconds} filled={filled}");

            var sb = new StringBuilder();
            sb.Append('[');
            for (int i = 0; i < filled; i++) sb.Append('x');
            for (int i = filled; i < BarWidth; i++) sb.Append(' ');
            sb.Append(']');

            _graphics.DrawText(sb.ToString(), _font, BarX, BarY, Color.Black);
        }

        // When VERBOSE_DISPLAY is not in DefineConstants, all Log(...) calls (and their arguments) compile out.
        [Conditional("VERBOSE_DISPLAY")]
        private void Log(string message)
        {
            DebugLog.Write("[Display] " + message);
        }
    }
}
