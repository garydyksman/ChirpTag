using System;
using Iot.Device.Ssd13xx;

namespace ChirpTagFlagNode
{
    /// <summary>
    /// Display modes for the flag node OLED.
    /// </summary>
    public enum DisplayMode
    {
        Activity = 1,   // Real-time activity (who just interacted)
        Stats = 2,      // Session statistics (captures, deliveries, uptime)
        Rotating = 3    // Alternates between Activity and Stats
    }

    /// <summary>
    /// Game statistics tracked by the flag node.
    /// </summary>
    public struct FlagNodeStats
    {
        public byte DeviceId;
        public string TeamName;
        public byte LastSeenDevice;
        public string LastActivity;
        public int IdleSeconds;
        public int CaptureCount;
        public int DeliverCount;
        public int UptimeSeconds;
        public string MacAddress;
    }

    /// <summary>
    /// Manages OLED display rendering for the flag node.
    /// Supports multiple display modes switchable via PRG button.
    /// </summary>
    public class FlagNodeDisplayManager
    {
        private readonly Ssd1306 _display;
        private readonly object _displayLock;
        private DisplayMode _mode;
        private int _rotatingCycle;

        public FlagNodeDisplayManager(Ssd1306 display, object displayLock, DisplayMode initialMode = DisplayMode.Rotating)
        {
            _display = display ?? throw new ArgumentNullException(nameof(display));
            _displayLock = displayLock ?? throw new ArgumentNullException(nameof(displayLock));
            _mode = initialMode;
            _rotatingCycle = 0;
        }

        public DisplayMode CurrentMode
        {
            get => _mode;
            set => _mode = value;
        }

        /// <summary>
        /// Cycles to the next display mode.
        /// </summary>
        public void CycleMode()
        {
            _mode = (DisplayMode)((((int)_mode) % 3) + 1);
        }

        /// <summary>
        /// Renders the current display mode with the given stats.
        /// </summary>
        public void Render(FlagNodeStats stats)
        {
            lock (_displayLock)
            {
                _display.ClearScreen();

                switch (_mode)
                {
                    case DisplayMode.Activity:
                        RenderActivity(stats);
                        break;

                    case DisplayMode.Stats:
                        RenderStats(stats);
                        break;

                    case DisplayMode.Rotating:
                        _rotatingCycle++;
                        int subMode = (_rotatingCycle / 3) % 2;
                        if (subMode == 0)
                        {
                            RenderActivity(stats);
                        }
                        else
                        {
                            RenderStats(stats);
                        }
                        break;
                }

                _display.Display();
            }
        }

        /// <summary>
        /// Shows mode change feedback on the display.
        /// </summary>
        public void ShowModeChange()
        {
            lock (_displayLock)
            {
                _display.ClearScreen();

                string modeName;
                if (_mode == DisplayMode.Activity) modeName = "ACTIVITY";
                else if (_mode == DisplayMode.Stats) modeName = "STATS";
                else modeName = "ROTATING";

                _display.DrawString(6, 24, "MODE: " + modeName, 1);
                _display.Display();
            }
        }

        private void RenderActivity(FlagNodeStats stats)
        {
            _display.DrawString(6, 6, "FLAG: " + stats.DeviceId, 1);
            _display.DrawString(6, 20, "TEAM: " + stats.TeamName, 1);
            if (stats.LastSeenDevice > 0)
            {
                _display.DrawString(6, 34, "LAST: ID" + stats.LastSeenDevice, 1);
                _display.DrawString(6, 48, stats.LastActivity, 1);
            }
            else
            {
                _display.DrawString(6, 34, "IDLE: " + stats.IdleSeconds + "s", 1);
                _display.DrawString(6, 48, "WAITING", 1);
            }
        }

        private void RenderStats(FlagNodeStats stats)
        {
            _display.DrawString(6, 6, stats.TeamName + " FLAG", 1);
            _display.DrawString(6, 20, "CAPS: " + stats.CaptureCount, 1);
            _display.DrawString(6, 34, "DELS: " + stats.DeliverCount, 1);
            _display.DrawString(6, 48, "UP: " + (stats.UptimeSeconds / 60) + "m", 1);
        }
    }
}
