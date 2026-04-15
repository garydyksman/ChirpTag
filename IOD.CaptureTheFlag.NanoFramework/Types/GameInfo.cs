using IOD.CaptureTheFlag.NanoFramework.Enum;

namespace IOD.CaptureTheFlag.NanoFramework.Types
{
    public class GameInfo
    {
        public GameStatus Status { get; set; }
        public PlayerInfo[] Players { get; set; }
    }
}
