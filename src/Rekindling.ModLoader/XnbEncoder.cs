using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;

namespace Rekindling.ModLoader
{
    /// <summary>
    /// Wraps an ordinary image file in the XNB container MonoGame's <c>ContentManager</c> expects,
    /// so mod authors can ship a <c>.png</c> instead of running the XNA content pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layout below was read off Rekindling's own content files, which are all
    /// <c>XNBw</c> version 5, uncompressed, single <c>Texture2DReader</c>, <c>SurfaceFormat.Color</c>:
    /// </para>
    /// <code>
    /// "XNBw"                  magic + target platform ('w' = Windows)
    /// byte    5               format version
    /// byte    0               flags: bit0 HiDef, bit6 LZ4, bit7 LZX. 0 = Reach, uncompressed
    /// int32   fileLength      total size of the file, including this header
    /// 7bit    1               type reader count
    /// 7bitStr "Microsoft.Xna.Framework.Content.Texture2DReader"
    /// int32   0               reader version
    /// 7bit    0               shared resource count
    /// 7bit    1               object type id (1 = first type reader)
    /// int32   0               SurfaceFormat.Color
    /// int32   width
    /// int32   height
    /// int32   1               mip level count
    /// int32   dataLength      width * height * 4
    /// bytes   pixels          RGBA, premultiplied
    /// </code>
    /// <para>
    /// Pixels are written premultiplied because that is what the content pipeline does by
    /// default, and the game draws with <c>BlendState.AlphaBlend</c>. Skipping the multiply
    /// would give every transparent edge a bright halo.
    /// </para>
    /// </remarks>
    internal static class XnbEncoder
    {
        private const string Texture2DReaderName = "Microsoft.Xna.Framework.Content.Texture2DReader";
        private const byte FormatVersion = 5;
        private const byte FlagsReachUncompressed = 0x00;
        private const int SurfaceFormatColor = 0;

        /// <summary>File extensions this encoder can convert.</summary>
        public static bool IsConvertibleImage(string path)
        {
            string extension = Path.GetExtension(path);
            return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reads an image file and returns the equivalent XNB bytes.
        /// </summary>
        /// <exception cref="InvalidDataException">The file is not a readable image.</exception>
        public static byte[] FromImageFile(string imagePath)
        {
            try
            {
                // Copy into a Bitmap we own: Image.FromFile keeps the file locked for the
                // lifetime of the object, which would stop authors editing art while the game runs.
                using (var loaded = Image.FromFile(imagePath))
                using (var bitmap = new Bitmap(loaded))
                {
                    return FromBitmap(bitmap);
                }
            }
            catch (OutOfMemoryException ex)
            {
                // GDI+ reports an unrecognised image format as OutOfMemoryException.
                throw new InvalidDataException(
                    $"'{Path.GetFileName(imagePath)}' is not a valid image file.", ex);
            }
        }

        /// <summary>Converts an in-memory bitmap to XNB bytes.</summary>
        public static byte[] FromBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            byte[] pixels = ToPremultipliedRgba(bitmap);
            return Wrap(pixels, bitmap.Width, bitmap.Height);
        }

        /// <summary>
        /// Extracts pixels as premultiplied RGBA. GDI+ hands back BGRA, so the red and blue
        /// channels are swapped on the way out.
        /// </summary>
        private static byte[] ToPremultipliedRgba(Bitmap bitmap)
        {
            var bounds = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int width = bitmap.Width;
                int height = bitmap.Height;
                var pixels = new byte[width * height * 4];

                unsafe
                {
                    for (int y = 0; y < height; y++)
                    {
                        byte* row = (byte*)data.Scan0 + (y * data.Stride);
                        int rowOffset = y * width * 4;

                        for (int x = 0; x < width; x++)
                        {
                            int source = x * 4;
                            int target = rowOffset + source;

                            byte b = row[source + 0];
                            byte g = row[source + 1];
                            byte r = row[source + 2];
                            byte a = row[source + 3];

                            if (a == 0)
                            {
                                // Fully transparent: zero the colour so filtering cannot bleed it.
                                pixels[target + 0] = 0;
                                pixels[target + 1] = 0;
                                pixels[target + 2] = 0;
                                pixels[target + 3] = 0;
                                continue;
                            }

                            if (a == 255)
                            {
                                pixels[target + 0] = r;
                                pixels[target + 1] = g;
                                pixels[target + 2] = b;
                                pixels[target + 3] = 255;
                                continue;
                            }

                            // Rounded rather than truncated: (v * a + 127) / 255.
                            pixels[target + 0] = (byte)((r * a + 127) / 255);
                            pixels[target + 1] = (byte)((g * a + 127) / 255);
                            pixels[target + 2] = (byte)((b * a + 127) / 255);
                            pixels[target + 3] = a;
                        }
                    }
                }

                return pixels;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>Builds the XNB container around raw RGBA pixel data.</summary>
        private static byte[] Wrap(byte[] pixels, int width, int height)
        {
            using (var buffer = new MemoryStream())
            using (var writer = new BinaryWriter(buffer, Encoding.UTF8))
            {
                writer.Write((byte)'X');
                writer.Write((byte)'N');
                writer.Write((byte)'B');
                writer.Write((byte)'w');
                writer.Write(FormatVersion);
                writer.Write(FlagsReachUncompressed);

                // Patched once the real length is known.
                writer.Write(0);

                Write7BitEncodedInt(writer, 1);           // one type reader
                WriteLengthPrefixedString(writer, Texture2DReaderName);
                writer.Write(0);                           // reader version
                Write7BitEncodedInt(writer, 0);           // no shared resources
                Write7BitEncodedInt(writer, 1);           // object is an instance of type reader 0

                writer.Write(SurfaceFormatColor);
                writer.Write(width);
                writer.Write(height);
                writer.Write(1);                           // mip levels
                writer.Write(pixels.Length);
                writer.Write(pixels);

                writer.Flush();

                byte[] result = buffer.ToArray();
                // File length lives at offset 6, little-endian.
                Buffer.BlockCopy(BitConverter.GetBytes(result.Length), 0, result, 6, 4);
                return result;
            }
        }

        /// <summary>
        /// XNA's own 7-bit encoding. <c>BinaryWriter.Write7BitEncodedInt</c> is protected on
        /// .NET Framework, so it is reimplemented here.
        /// </summary>
        private static void Write7BitEncodedInt(BinaryWriter writer, int value)
        {
            uint remaining = (uint)value;
            while (remaining >= 0x80)
            {
                writer.Write((byte)(remaining | 0x80));
                remaining >>= 7;
            }
            writer.Write((byte)remaining);
        }

        private static void WriteLengthPrefixedString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            Write7BitEncodedInt(writer, bytes.Length);
            writer.Write(bytes);
        }
    }
}
