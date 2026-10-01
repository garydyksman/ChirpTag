using System;

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
        private readonly SimpleOled _display;
        private DisplayMode _mode;
        private int _rotatingCycle;

        public FlagNodeDisplayManager(SimpleOled display, DisplayMode initialMode = DisplayMode.Rotating)
        {
            _display = display ?? throw new ArgumentNullException(nameof(display));
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
            _display.Clear();

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
        }

        /// <summary>
        /// Shows mode change feedback on the display.
        /// </summary>
        public void ShowModeChange()
        {
            _display.Clear();
            string modeName;
            if (_mode == DisplayMode.Activity) modeName = "ACTIVITY";
            else if (_mode == DisplayMode.Stats) modeName = "STATS";
            else modeName = "ROTATING";
            _display.Print(1, 6, "MODE: " + modeName);
        }

        private void RenderActivity(FlagNodeStats stats)
        {
            _display.Print(0, 6, "FLAG: " + stats.DeviceId);
            _display.Print(1, 6, "TEAM: " + stats.TeamName);
            if (stats.LastSeenDevice > 0)
            {
                _display.Print(2, 6, "LAST: ID" + stats.LastSeenDevice);
                _display.Print(3, 6, stats.LastActivity);
            }
            else
            {
                _display.Print(2, 6, "IDLE: " + stats.IdleSeconds + "S");
                _display.Print(3, 6, "WAITING");
            }
        }

        private void RenderStats(FlagNodeStats stats)
        {
            _display.Print(0, 6, stats.TeamName + " FLAG");
            _display.Print(1, 6, "CAPS: " + stats.CaptureCount);
            _display.Print(2, 6, "DELS: " + stats.DeliverCount);
            _display.Print(3, 6, "UP: " + (stats.UptimeSeconds / 60) + "M");
        }
    }
}
