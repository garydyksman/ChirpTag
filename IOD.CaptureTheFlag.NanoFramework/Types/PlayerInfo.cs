namespace IOD.CaptureTheFlag.NanoFramework.Types
{
    public class PlayerInfo
    {
        public byte DeviceId { get; set; }
        public string Name { get; set; }
        public string Team { get; set; }
        public string Type { get; set; }
        public byte CombatScore { get; set; }
        public byte EnemyFlagId { get; set; }
    }
}
