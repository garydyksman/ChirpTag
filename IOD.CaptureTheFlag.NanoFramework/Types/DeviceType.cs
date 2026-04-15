namespace IOD.CaptureTheFlag.NanoFramework.Types
{
    // ---------------------------------------------------------------
    // Device types
    // ---------------------------------------------------------------

    public static class DeviceType
    {
        public const byte Broadcast = 0x00;  // TargetID sentinel for broadcasts
        public const byte Player    = 0x01;
        public const byte FlagNode  = 0x02;
        public const byte GmNode    = 0x03;
    }
}