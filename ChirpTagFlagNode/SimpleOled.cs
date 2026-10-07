using System.Device.I2c;

namespace ChirpTagFlagNode
{
    /// <summary>
    /// Raw I2C OLED driver for SSD1306/SSD1315 128x64.
    /// Writes directly to the hardware using horizontal addressing mode.
    /// No framebuffer — each Print() call goes straight to the display.
    /// </summary>
    public sealed class SimpleOled
    {
        private const byte I2cAddress = 0x3C;

        private readonly I2cDevice _i2c;

        // Pre-allocated buffers — no heap churn during display updates
        private readonly byte[] _windowBuf;  // SetWindow command sequence
        private readonly byte[] _clearPage;  // 0x40 + 128 zeros for one page clear
        private readonly byte[] _charBuf;    // 0x40 + 5 glyph bytes + 1 space byte

        public SimpleOled(I2cDevice i2c)
        {
            _i2c = i2c;

            _windowBuf = new byte[] { 0x00, 0x21, 0x00, 0x7F, 0x22, 0x00, 0x07 };

            _clearPage = new byte[129];
            _clearPage[0] = 0x40;

            _charBuf = new byte[7];
            _charBuf[0] = 0x40;
            // _charBuf[6] = 0x00; inter-char gap, already zero
        }

        /// <summary>
        /// Send the full init sequence from the ATtiny85 ssd1306xled driver.
        /// Must be called once after hardware reset, before any draw calls.
        /// </summary>
        public void Init()
        {
            _i2c.Write(new byte[] {
                0x00,        // Co=0, D/C#=0 → command stream
                0xAE,        // display off
                0xD5, 0xF0,  // clock divide ratio / oscillator frequency
                0xA8, 0x3F,  // multiplex ratio: 63 (64 rows)
                0xD3, 0x00,  // display offset: none
                0x40,        // display start line: 0
                0x8D, 0x14,  // charge pump: enable
                0x20, 0x00,  // memory addressing mode: horizontal
                0xA1,        // segment remap: col 127 → SEG0
                0xC8,        // COM output scan: COM[N-1] to COM[0]
                0xDA, 0x12,  // COM pin hardware config: 128×64
                0x81, 0xCF,  // contrast: 0xCF (Heltec/ThingPulse default)
                0xD9, 0x22,  // pre-charge period
                0xDB, 0x20,  // VCOMH deselect level
                0xA4,        // display from RAM
                0xA6,        // normal display (non-inverted)
                0x2E,        // deactivate scroll
                0xAF         // display on
            });
        }

        /// <summary>Blank the entire display (all 8 pages × 128 columns).</summary>
        public void Clear()
        {
            SetWindow(0, 127, 0, 7);
            for (int p = 0; p < 8; p++)
                _i2c.Write(_clearPage);
        }

        /// <summary>
        /// Print text at a logical line (0=top … 3=bottom), starting at column <paramref name="col"/>.
        /// Each logical line occupies 2 display pages (16 px). Text is 7 px tall.
        /// Maximum 21 characters per line at col=0.
        /// </summary>
        public void Print(int line, int col, string text)
        {
            if (text == null || line < 0 || line > 3 || col >= 128) return;

            int page = line * 2;
            SetWindow((byte)col, 127, (byte)page, (byte)page);

            int maxChars = (128 - col) / 6;
            if (maxChars > 21) maxChars = 21;
            int len = text.Length < maxChars ? text.Length : maxChars;

            for (int i = 0; i < len; i++)
            {
                byte[] g = GetGlyph(text[i]);
                _charBuf[1] = g[0];
                _charBuf[2] = g[1];
                _charBuf[3] = g[2];
                _charBuf[4] = g[3];
                _charBuf[5] = g[4];
                _charBuf[6] = 0x00;
                _i2c.Write(_charBuf); // 7 bytes: [0x40, g0..g4, 0x00]
            }
        }

        // ----------------------------------------------------------------
        // Private helpers
        // ----------------------------------------------------------------

        private void SetWindow(byte colStart, byte colEnd, byte pageStart, byte pageEnd)
        {
            _windowBuf[2] = colStart;
            _windowBuf[3] = colEnd;
            _windowBuf[5] = pageStart;
            _windowBuf[6] = pageEnd;
            _i2c.Write(_windowBuf);
        }

        // ----------------------------------------------------------------
        // Font: 5×7 column-byte glyphs, same data as PixelTextRenderer.
        // Each byte = column of 7 bits, bit0=top row. This format writes
        // directly into SSD1306 page-mode (bit0 = top of page).
        // ----------------------------------------------------------------

        private static readonly byte[] s_glyphSpace   = { 0x00, 0x00, 0x00, 0x00, 0x00 };
        private static readonly byte[] s_glyphUnknown = { 0x02, 0x01, 0x59, 0x09, 0x06 };

        private static byte[] GetGlyph(char c)
        {
            if (c >= 'a' && c <= 'z') c = (char)(c - 32);
            switch (c)
            {
                case ' ':  return s_glyphSpace;
                case '-':  return new byte[] { 0x08, 0x08, 0x08, 0x08, 0x08 };
                case ':':  return new byte[] { 0x00, 0x36, 0x36, 0x00, 0x00 };
                case '.':  return new byte[] { 0x00, 0x60, 0x60, 0x00, 0x00 };
                case '/':  return new byte[] { 0x20, 0x10, 0x08, 0x04, 0x02 };
                case '0':  return new byte[] { 0x3E, 0x51, 0x49, 0x45, 0x3E };
                case '1':  return new byte[] { 0x00, 0x42, 0x7F, 0x40, 0x00 };
                case '2':  return new byte[] { 0x42, 0x61, 0x51, 0x49, 0x46 };
                case '3':  return new byte[] { 0x21, 0x41, 0x45, 0x4B, 0x31 };
                case '4':  return new byte[] { 0x18, 0x14, 0x12, 0x7F, 0x10 };
                case '5':  return new byte[] { 0x27, 0x45, 0x45, 0x45, 0x39 };
                case '6':  return new byte[] { 0x3C, 0x4A, 0x49, 0x49, 0x30 };
                case '7':  return new byte[] { 0x01, 0x71, 0x09, 0x05, 0x03 };
                case '8':  return new byte[] { 0x36, 0x49, 0x49, 0x49, 0x36 };
                case '9':  return new byte[] { 0x06, 0x49, 0x49, 0x29, 0x1E };
                case 'A':  return new byte[] { 0x7E, 0x11, 0x11, 0x11, 0x7E };
                case 'B':  return new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x36 };
                case 'C':  return new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x22 };
                case 'D':  return new byte[] { 0x7F, 0x41, 0x41, 0x22, 0x1C };
                case 'E':  return new byte[] { 0x7F, 0x49, 0x49, 0x49, 0x41 };
                case 'F':  return new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x01 };
                case 'G':  return new byte[] { 0x3E, 0x41, 0x49, 0x49, 0x7A };
                case 'H':  return new byte[] { 0x7F, 0x08, 0x08, 0x08, 0x7F };
                case 'I':  return new byte[] { 0x00, 0x41, 0x7F, 0x41, 0x00 };
                case 'J':  return new byte[] { 0x20, 0x40, 0x41, 0x3F, 0x01 };
                case 'K':  return new byte[] { 0x7F, 0x08, 0x14, 0x22, 0x41 };
                case 'L':  return new byte[] { 0x7F, 0x40, 0x40, 0x40, 0x40 };
                case 'M':  return new byte[] { 0x7F, 0x02, 0x0C, 0x02, 0x7F };
                case 'N':  return new byte[] { 0x7F, 0x04, 0x08, 0x10, 0x7F };
                case 'O':  return new byte[] { 0x3E, 0x41, 0x41, 0x41, 0x3E };
                case 'P':  return new byte[] { 0x7F, 0x09, 0x09, 0x09, 0x06 };
                case 'Q':  return new byte[] { 0x3E, 0x41, 0x51, 0x21, 0x5E };
                case 'R':  return new byte[] { 0x7F, 0x09, 0x19, 0x29, 0x46 };
                case 'S':  return new byte[] { 0x46, 0x49, 0x49, 0x49, 0x31 };
                case 'T':  return new byte[] { 0x01, 0x01, 0x7F, 0x01, 0x01 };
                case 'U':  return new byte[] { 0x3F, 0x40, 0x40, 0x40, 0x3F };
                case 'V':  return new byte[] { 0x1F, 0x20, 0x40, 0x20, 0x1F };
                case 'W':  return new byte[] { 0x7F, 0x20, 0x18, 0x20, 0x7F };
                case 'X':  return new byte[] { 0x63, 0x14, 0x08, 0x14, 0x63 };
                case 'Y':  return new byte[] { 0x03, 0x04, 0x78, 0x04, 0x03 };
                case 'Z':  return new byte[] { 0x61, 0x51, 0x49, 0x45, 0x43 };
                default:   return s_glyphUnknown;
            }
        }
    }
}
