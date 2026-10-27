using System;

namespace Hollow.HoUnityTools.Editor.Pmx
{
    /// <summary>
    /// Data-only decoders for common BMP/TGA source images. RGBA rows are bottom-to-top,
    /// matching Texture2D.SetPixels32, and channels are never gamma transformed.
    /// Unsupported formats return false so the caller can report a useful limitation.
    /// </summary>
    internal static class HoPmxSourceImage
    {
        private const long MaxPixels = 64L * 1024 * 1024;

        internal static bool Read(byte[] bytes, string extension, out int width, out int height, out byte[] rgba)
        {
            width = height = 0;
            rgba = null;
            if (bytes == null || string.IsNullOrEmpty(extension)) return false;
            if (string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, "bmp", StringComparison.OrdinalIgnoreCase))
                return ReadBmp(bytes, out width, out height, out rgba);
            if (string.Equals(extension, ".tga", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, "tga", StringComparison.OrdinalIgnoreCase))
                return ReadTga(bytes, out width, out height, out rgba);
            return false;
        }

        private static bool ReadBmp(byte[] bytes, out int width, out int height, out byte[] rgba)
        {
            width = height = 0;
            rgba = null;
            if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M') return false;
            uint header = U32(bytes, 14);
            uint pixelOffset = U32(bytes, 10);
            if (header < 40 || header > int.MaxValue || !Has(bytes, 14, (int)header) || pixelOffset < 14L + header) return false;
            int w = I32(bytes, 18), signedHeight = I32(bytes, 22);
            if (signedHeight == int.MinValue) return false;
            int h = Math.Abs(signedHeight);
            int depth = U16(bytes, 28);
            if (!Dimensions(w, h) || U16(bytes, 26) != 1 || (depth != 24 && depth != 32) || U32(bytes, 30) != 0) return false;
            int channels = depth / 8;
            long stride = ((long)w * channels + 3) & ~3L;
            long end = pixelOffset + stride * h;
            if (pixelOffset > int.MaxValue || end > bytes.Length) return false;
            var result = new byte[w * h * 4];
            bool anyAlpha = false;
            for (int fileRow = 0; fileRow < h; fileRow++)
            {
                int y = signedHeight > 0 ? fileRow : h - 1 - fileRow;
                int read = (int)(pixelOffset + stride * fileRow);
                int write = y * w * 4;
                for (int x = 0; x < w; x++, read += channels, write += 4)
                {
                    result[write] = bytes[read + 2];
                    result[write + 1] = bytes[read + 1];
                    result[write + 2] = bytes[read];
                    byte alpha = channels == 4 ? bytes[read + 3] : (byte)255;
                    result[write + 3] = alpha;
                    anyAlpha |= alpha != 0;
                }
            }
            // BI_RGB's fourth byte is historically unused and is often all zero. Preserve
            // a populated alpha plane, but do not turn opaque Windows BMPs transparent.
            if (channels == 4 && !anyAlpha)
                for (int i = 3; i < result.Length; i += 4) result[i] = 255;
            width = w; height = h; rgba = result;
            return true;
        }

        private static bool ReadTga(byte[] bytes, out int width, out int height, out byte[] rgba)
        {
            width = height = 0;
            rgba = null;
            if (bytes.Length < 18) return false;
            int type = bytes[2], depth = bytes[16], descriptor = bytes[17];
            bool grayscale = type == 3 || type == 11;
            bool colorMapped = type == 1 || type == 9;
            bool trueColor = type == 2 || type == 10;
            bool rle = type == 9 || type == 10 || type == 11;
            if ((!grayscale && !colorMapped && !trueColor) || (descriptor & 0xc0) != 0) return false;
            if (grayscale ? depth != 8 : colorMapped ? depth != 8 : depth != 24 && depth != 32) return false;
            if (bytes[1] != (colorMapped ? 1 : 0)) return false;
            if ((descriptor & 15) > 8) return false;
            int w = U16(bytes, 12), h = U16(bytes, 14);
            if (!Dimensions(w, h)) return false;
            int position = 18 + bytes[0];
            if (!Has(bytes, 18, bytes[0])) return false;

            Pixel[] palette = null;
            int paletteFirst = 0;
            if (colorMapped)
            {
                paletteFirst = U16(bytes, 3);
                int count = U16(bytes, 5), paletteBits = bytes[7];
                if (count == 0 || paletteFirst + count > 256 || (paletteBits != 24 && paletteBits != 32)) return false;
                palette = new Pixel[count];
                for (int i = 0; i < count; i++)
                {
                    Pixel pixel;
                    if (!PixelAt(bytes, ref position, paletteBits, false, paletteBits == 32, null, 0, out pixel)) return false;
                    palette[i] = pixel;
                }
            }

            int pixelCount = w * h;
            var result = new byte[pixelCount * 4];
            bool alpha = depth == 32 && (descriptor & 15) != 0;
            int decoded = 0;
            while (decoded < pixelCount)
            {
                bool repeated = false;
                int count = pixelCount - decoded;
                if (rle)
                {
                    if (!Has(bytes, position, 1)) return false;
                    byte packet = bytes[position++];
                    repeated = (packet & 128) != 0;
                    count = (packet & 127) + 1;
                    if (count > pixelCount - decoded) return false;
                }
                Pixel pixel = new Pixel();
                if (repeated && !PixelAt(bytes, ref position, depth, grayscale, alpha, palette, paletteFirst, out pixel)) return false;
                for (int i = 0; i < count; i++, decoded++)
                {
                    if (!repeated && !PixelAt(bytes, ref position, depth, grayscale, alpha, palette, paletteFirst, out pixel)) return false;
                    int fileX = decoded % w, fileY = decoded / w;
                    int x = (descriptor & 16) != 0 ? w - 1 - fileX : fileX;
                    int y = (descriptor & 32) != 0 ? h - 1 - fileY : fileY;
                    int write = (y * w + x) * 4;
                    result[write] = pixel.R;
                    result[write + 1] = pixel.G;
                    result[write + 2] = pixel.B;
                    result[write + 3] = pixel.A;
                }
            }
            width = w; height = h; rgba = result;
            return true;
        }

        private static bool PixelAt(byte[] bytes, ref int position, int bits, bool grayscale, bool alpha,
            Pixel[] palette, int paletteFirst, out Pixel result)
        {
            result = new Pixel();
            int count = bits / 8;
            if (!Has(bytes, position, count)) return false;
            if (palette != null)
            {
                int index = bytes[position++] - paletteFirst;
                if (index < 0 || index >= palette.Length) return false;
                result = palette[index];
                return true;
            }
            if (grayscale)
            {
                byte value = bytes[position++];
                result = new Pixel { R = value, G = value, B = value, A = 255 };
                return true;
            }
            result.B = bytes[position++];
            result.G = bytes[position++];
            result.R = bytes[position++];
            result.A = bits == 32 ? bytes[position++] : (byte)255;
            if (!alpha) result.A = 255;
            return true;
        }

        private static bool Dimensions(int width, int height)
        {
            return width > 0 && height > 0 && width <= 32768 && height <= 32768 && (long)width * height <= MaxPixels;
        }
        private static bool Has(byte[] bytes, int offset, int count)
        {
            return offset >= 0 && count >= 0 && offset <= bytes.Length - count;
        }
        private static int U16(byte[] bytes, int offset) { return bytes[offset] | bytes[offset + 1] << 8; }
        private static uint U32(byte[] bytes, int offset)
        {
            return (uint)bytes[offset] | (uint)bytes[offset + 1] << 8 | (uint)bytes[offset + 2] << 16 | (uint)bytes[offset + 3] << 24;
        }
        private static int I32(byte[] bytes, int offset) { return unchecked((int)U32(bytes, offset)); }
        private struct Pixel { public byte R, G, B, A; }
    }
}
