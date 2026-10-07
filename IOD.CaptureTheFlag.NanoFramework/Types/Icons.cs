namespace IOD.CaptureTheFlag.NanoFramework.Types
{
    public static class Icons
    {
        public const int HeartWidth = 10;
        public const int ReticleWidth = 16;
        public const int FlagWidth = 14;
        public const int SkullWidth = 24;

        public static readonly ushort[] HeartNormal = new ushort[]
        {
            0x00CC,
            0x01FE,
            0x03FF,
            0x03FF,
            0x03FF,
            0x01FE,
            0x00FC,
            0x0078,
            0x0030,
            0x0000,
        };

        public static readonly ushort[] HeartBeat = new ushort[]
        {
            0x00CC,
            0x0132,
            0x0201,
            0x0201,
            0x0201,
            0x0102,
            0x0084,
            0x0048,
            0x0030,
            0x0000,
        };

        public static readonly ushort[] Reticle = new ushort[]
        {
            0x0660,
            0x1818,
            0x23C4,
            0x4422,
            0x4812,
            0x9009,
            0xA245,
            0x2184,
            0x2184,
            0xA245,
            0x9009,
            0x4812,
            0x4422,
            0x23C4,
            0x1818,
            0x0660,
        };

        public static readonly ushort[] Flag = new ushort[]
        {
            0x0800,
            0x0FE0,
            0x0FF0,
            0x0FF8,
            0x0FF0,
            0x0FE0,
            0x0800,
            0x0800,
            0x0800,
            0x0800,
            0x0800,
            0x1C00,
            0x0000,
            0x0000,
            0x0000,
            0x0000,
        };

        public static readonly uint[] Skull = new uint[]
        {
            0x0FFFF0,
            0x1FFFF8,
            0x3FFFFC,
            0x7FFFFE,
            0x7FFFFE,
            0xFFFFFF,
            0xE1FF87,
            0xC0FF03,
            0xC0FF03,
            0xC0FF03,
            0xE1FF87,
            0xFFFFFF,
            0xFFFFFF,
            0xFEFF7F,
            0xFC7E3F,
            0xFEFF7F,
            0xFFFFFF,
            0x555555,
            0x555555,
            0x000000,
        };
    }
}
