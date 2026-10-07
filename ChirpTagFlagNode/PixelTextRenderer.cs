using Iot.Device.Ssd13xx;

namespace ChirpTagFlagNode
{
    internal static class PixelTextRenderer
    {
        private static readonly byte[] GlyphSpace = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] GlyphUnknown = new byte[] { 0x02, 0x01, 0x59, 0x09, 0x06 };

        public static void DrawText(Ssd1306 display, int x, int y, string text, int scale = 1)
        {
            if (display == null || text == null)
            {
                return;
            }

            if (scale < 1)
            {
                scale = 1;
            }

            var cursorX = x;

            for (var i = 0; i < text.Length; i++)
            {
                var glyph = GetGlyph(text[i]);
                DrawGlyph(display, cursorX, y, glyph, scale);
                cursorX += 6 * scale;
            }
        }

        private static void DrawGlyph(Ssd1306 display, int x, int y, byte[] glyph, int scale)
        {
            for (var col = 0; col < 5; col++)
            {
                var bits = glyph[col];

                for (var row = 0; row < 7; row++)
                {
                    if (((bits >> row) & 0x01) == 0)
                    {
                        continue;
                    }

                    if (scale == 1)
                    {
                        display.DrawPixel(x + col, y + row, true);
                    }
                    else
                    {
                        display.DrawFilledRectangle(x + (col * scale), y + (row * scale), scale, scale, true);
                    }
                }
            }
        }

        private static byte[] GetGlyph(char c)
        {
            if (c >= 'a' && c <= 'z')
            {
                c = (char)(c - 32);
            }

            switch (c)
            {
                case ' ':
                    return GlyphSpace;
                case '-':
                    return new byte[] { 0x08, 0x08, 0x08, 0x08, 0x08 };
                case ':':
                    return new byte[] { 0x00, 0x36, 0x36, 0x00, 0x00 };
                case '0':
                    return new byte[] { 0x3E, 0x51, 0x49, 0x45, 0x3E };
                case '1':
                    return new byte[] { 0x00, 0x42, 0x7F, 0x40, 0x00 };
                case '2':
                    return new byte[] { 0x42, 0x61, 0x51, 0x49, 0x46 };
                case '3':
                    return new byte[] { 0x21, 0x41, 0x45, 0x4B, 0x31 };
                case '4':
                    return new byte[] { 0x18, 0x14, 0x12, 0x7F, 0x10 };
                case '5':
                    return new byte[] { 0x27, 0x45, 0x45, 0x45, 0x39 };
                case '6':
                    return new byte[] { 0x3C, 0x4A, 0x49, 0x49, 0x30 };
                case '7':
                    return new byte[] { 0x01, 0x71, 0x09, 0x05, 0x03 };
                case '8':
                    return new byte[] { 0x36, 0x49, 0x49, 0x49, 0x36 };
                case '9':
                    return new byte[] { 0x06, 0x49, 0x49, 0x29, 0x1E };
                case 'A':
                    return new byte[] { 0x7E, 0x11, 0x11, 0x11, 0x7E };
                case 'B':
                    return new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x36 };
                case 'C':
                    return new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x22 };
                case 'D':
                    return new byte[] { 0x7F, 0x41, 0x41, 0x22, 0x1C };
                case 'E':
                    return new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x41 };
                case 'F':
                    return new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x01 };
                case 'G':
                    return new byte[] { 0x3E, 0x41, 0x49, 0x49, 0x7A };
                case 'H':
                    return new byte[] { 0x7F, 0x08, 0x08, 0x08, 0x7F };
                case 'I':
                    return new byte[] { 0x00, 0x41, 0x7F, 0x41, 0x00 };
                case 'J':
                    return new byte[] { 0x20, 0x40, 0x41, 0x3F, 0x01 };
                case 'K':
                    return new byte[] { 0x7F, 0x08, 0x14, 0x22, 0x41 };
                case 'L':
                    return new byte[] { 0x7F, 0x40, 0x40, 0x40, 0x40 };
                case 'M':
                    return new byte[] { 0x7F, 0x02, 0x0C, 0x02, 0x7F };
                case 'N':
                    return new byte[] { 0x7F, 0x04, 0x08, 0x10, 0x7F };
                case 'O':
                    return new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x3E };
                case 'P':
                    return new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x06 };
                case 'Q':
                    return new byte[] { 0x3E, 0x41, 0x51, 0x21, 0x5E };
                case 'R':
                    return new byte[] { 0x7F, 0x09, 0x19, 0x29, 0x46 };
                case 'S':
                    return new byte[] { 0x46, 0x49, 0x49, 0x49, 0x31 };
                case 'T':
                    return new byte[] { 0x01, 0x01, 0x7F, 0x01, 0x01 };
                case 'U':
                    return new byte[] { 0x3F, 0x40, 0x40, 0x40, 0x3F };
                case 'V':
                    return new byte[] { 0x1F, 0x20, 0x40, 0x20, 0x1F };
                case 'W':
                    return new byte[] { 0x7F, 0x20, 0x18, 0x20, 0x7F };
                case 'X':
                    return new byte[] { 0x63, 0x14, 0x08, 0x14, 0x63 };
                case 'Y':
                    return new byte[] { 0x03, 0x04, 0x78, 0x04, 0x03 };
                case 'Z':
                    return new byte[] { 0x61, 0x51, 0x49, 0x45, 0x43 };
                default:
                    return GlyphUnknown;
            }
        }
    }
}
