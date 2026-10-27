using System;
using System.IO;
using System.Linq;
using Hollow.HoUnityTools.Editor.Pmx;

namespace HoPmxDataValidation
{
    internal static class SourceImageValidation
    {
        private static int tests;
        // Bottom-left RGBA: red, green / blue, white. All fixtures are generated in memory.
        private static readonly byte[] Expected =
        {
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 255, 255, 255, 255
        };

        internal static void Run()
        {
            tests = 0;
            foreach (int bits in new[] { 24, 32 })
            foreach (bool top in new[] { false, true })
                Decode(Bmp(bits, top), ".bmp", Expected, "BMP " + bits + " top=" + top);

            byte[] unused = Bmp(32, false);
            for (int i = 57; i < unused.Length; i += 4) unused[i] = 0;
            Decode(unused, "bmp", Expected, "BMP32 unused alpha is opaque");
            byte[] mixed = Bmp(32, false);
            mixed[57] = 128;
            byte[] alpha = (byte[])Expected.Clone();
            alpha[3] = 128;
            Decode(mixed, "BMP", alpha, "BMP32 populated alpha preserved");

            foreach (int bits in new[] { 24, 32 })
            foreach (int origin in new[] { 0, 16, 32, 48 })
            foreach (bool rle in new[] { false, true })
                Decode(Tga(bits, origin, rle), ".tga", Expected, "TGA " + bits + " origin=" + origin + " RLE=" + rle);

            byte[] gray = { 0, 0, 0, 255, 1, 1, 1, 255, 2, 2, 2, 255, 3, 3, 3, 255 };
            foreach (int origin in new[] { 0, 16, 32, 48 })
            foreach (bool rle in new[] { false, true })
                Decode(Tga(8, origin, rle), "tga", gray, "TGA8 gray origin=" + origin + " RLE=" + rle);

            byte[] run = Tga(24, 0, true).Take(18).Concat(new byte[] { 131, 0, 0, 255 }).ToArray();
            Decode(run, "tga", Enumerable.Repeat(new byte[] { 255, 0, 0, 255 }, 4).SelectMany(x => x).ToArray(), "TGA repeated packet");
            byte[] overrun = (byte[])run.Clone();
            overrun[18] = 132;
            Invalid(overrun, "tga", "TGA packet overshoot");
            Invalid(run.Take(21).ToArray(), "tga", "TGA truncated RLE pixel");
            Invalid(Bmp(24, false).Take(60).ToArray(), "bmp", "BMP truncated row");
            byte[] big = Bmp(24, false);
            Write32(big, 18, int.MaxValue);
            Invalid(big, "bmp", "BMP dimension overflow");
            Invalid(new byte[0], "bmp", "Empty BMP");
            Invalid(new byte[18], "tga", "Empty TGA");
            Console.WriteLine("PASS " + tests + " raw-byte BMP/TGA cases");
        }

        private static byte[] Bmp(int bits, bool top)
        {
            int channels = bits / 8, stride = (2 * channels + 3) & ~3;
            var bytes = new byte[54 + stride * 2];
            bytes[0] = 66; bytes[1] = 77;
            Write32(bytes, 2, bytes.Length); Write32(bytes, 10, 54); Write32(bytes, 14, 40);
            Write32(bytes, 18, 2); Write32(bytes, 22, top ? -2 : 2);
            Write16(bytes, 26, 1); Write16(bytes, 28, bits);
            for (int row = 0; row < 2; row++)
            for (int x = 0; x < 2; x++)
            {
                int input = (top ? 1 - row : row) * 8 + x * 4;
                int output = 54 + row * stride + x * channels;
                bytes[output] = Expected[input + 2];
                bytes[output + 1] = Expected[input + 1];
                bytes[output + 2] = Expected[input];
                if (bits == 32) bytes[output + 3] = Expected[input + 3];
            }
            return bytes;
        }

        private static byte[] Tga(int bits, int origin, bool rle)
        {
            using (var stream = new MemoryStream())
            {
                var header = new byte[18];
                header[2] = (byte)(bits == 8 ? (rle ? 11 : 3) : (rle ? 10 : 2));
                Write16(header, 12, 2); Write16(header, 14, 2);
                header[16] = (byte)bits;
                header[17] = (byte)(origin | (bits == 32 ? 8 : 0));
                stream.Write(header, 0, header.Length);
                if (rle) stream.WriteByte(3); // A raw packet with four pixels.
                for (int row = 0; row < 2; row++)
                for (int x = 0; x < 2; x++)
                {
                    int y = (origin & 32) != 0 ? 1 - row : row;
                    int xx = (origin & 16) != 0 ? 1 - x : x;
                    int input = y * 8 + xx * 4;
                    if (bits == 8) { stream.WriteByte((byte)(y * 2 + xx)); continue; }
                    stream.WriteByte(Expected[input + 2]);
                    stream.WriteByte(Expected[input + 1]);
                    stream.WriteByte(Expected[input]);
                    if (bits == 32) stream.WriteByte(Expected[input + 3]);
                }
                return stream.ToArray();
            }
        }

        private static void Decode(byte[] bytes, string extension, byte[] expected, string description)
        {
            int width, height; byte[] result;
            Check(HoPmxSourceImage.Read(bytes, extension, out width, out height, out result) && width == 2 && height == 2 && result.SequenceEqual(expected), description);
        }
        private static void Invalid(byte[] bytes, string extension, string description)
        {
            int width, height; byte[] result;
            Check(!HoPmxSourceImage.Read(bytes, extension, out width, out height, out result) && width == 0 && height == 0 && result == null, description);
        }
        private static void Check(bool value, string description)
        {
            tests++;
            if (!value) throw new InvalidOperationException(description);
        }
        private static void Write16(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8);
        }
        private static void Write32(byte[] bytes, int offset, int value)
        {
            for (int i = 0; i < 4; i++) bytes[offset + i] = (byte)(value >> (8 * i));
        }
    }
}
