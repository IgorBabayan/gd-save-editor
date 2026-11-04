using System.Buffers.Binary;
using System.IO;

namespace GD.SaveEditor.Services.GameData.Textures;

/// <summary>An image decoded to 32-bit BGRA pixels (top row first, not premultiplied).</summary>
public sealed record DecodedImage(int Width, int Height, byte[] Bgra);

/// <summary>
/// Decodes Grim Dawn textures (<c>.tex</c>, e.g. item icons in <c>Items.arc</c>):
/// <code>
/// "TEX" + version byte, a few int32 values (frame rate, frame length), then a DDS image whose magic is
/// "DDS " or "DDSR" (the game's variant with an incomplete header).
/// </code>
/// Only the first (largest) mipmap is decoded. Supported pixel formats: DXT1, DXT3, DXT5 and uncompressed
/// 32 / 24-bit color.
/// </summary>
public static class TexDecoder
{
    private const int DdsHeaderSize = 128; // magic + 124-byte header
    private const int MaxSize = 4096;

    private const uint PixelFormatFourCc = 0x4;

    private static readonly uint FourCcDxt1 = FourCc("DXT1");
    private static readonly uint FourCcDxt3 = FourCc("DXT3");
    private static readonly uint FourCcDxt5 = FourCc("DXT5");

    /// <exception cref="InvalidDataException">Not a texture this decoder can read.</exception>
    public static DecodedImage Decode(byte[] data)
    {
        var dds = FindDds(data);

        var height = BinaryPrimitives.ReadInt32LittleEndian(dds[12..]);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dds[16..]);
        if (width is <= 0 or > MaxSize || height is <= 0 or > MaxSize)
            throw new InvalidDataException($"Invalid texture size {width}x{height}.");

        var formatFlags = BinaryPrimitives.ReadUInt32LittleEndian(dds[80..]);
        var fourCc = BinaryPrimitives.ReadUInt32LittleEndian(dds[84..]);
        var bitCount = BinaryPrimitives.ReadInt32LittleEndian(dds[88..]);
        var pixels = dds[DdsHeaderSize..];

        if ((formatFlags & PixelFormatFourCc) != 0 || fourCc == FourCcDxt1 || fourCc == FourCcDxt3 || fourCc == FourCcDxt5)
        {
            if (fourCc == FourCcDxt1) return DecodeBlocks(pixels, width, height, 8, DecodeDxt1Block);
            if (fourCc == FourCcDxt3) return DecodeBlocks(pixels, width, height, 16, DecodeDxt3Block);
            if (fourCc == FourCcDxt5) return DecodeBlocks(pixels, width, height, 16, DecodeDxt5Block);
            throw new InvalidDataException("Unsupported compressed texture format.");
        }

        return bitCount switch
        {
            32 => DecodeUncompressed(pixels, width, height, 4, ReadMasks(dds)),
            24 => DecodeUncompressed(pixels, width, height, 3, ReadMasks(dds)),
            _ => throw new InvalidDataException($"Unsupported texture format ({bitCount} bits per pixel).")
        };
    }

    /// <summary>The DDS part (from its magic on) of a .tex file, or of a plain .dds file.</summary>
    private static ReadOnlySpan<byte> FindDds(byte[] data)
    {
        var limit = Math.Min(data.Length - DdsHeaderSize, 64);
        for (var i = 0; i <= limit; i++)
        {
            if (data[i] == 'D' && data[i + 1] == 'D' && data[i + 2] == 'S' && (data[i + 3] == ' ' || data[i + 3] == 'R'))
                return data.AsSpan(i);
        }

        throw new InvalidDataException("No DDS image in the texture.");
    }

    private static (uint R, uint G, uint B, uint A) ReadMasks(ReadOnlySpan<byte> dds) => (
        BinaryPrimitives.ReadUInt32LittleEndian(dds[92..]),
        BinaryPrimitives.ReadUInt32LittleEndian(dds[96..]),
        BinaryPrimitives.ReadUInt32LittleEndian(dds[100..]),
        BinaryPrimitives.ReadUInt32LittleEndian(dds[104..]));

    #region Uncompressed

    private static DecodedImage DecodeUncompressed(ReadOnlySpan<byte> pixels, int width, int height, int bytesPerPixel, (uint R, uint G, uint B, uint A) masks)
    {
        if (pixels.Length < width * height * bytesPerPixel)
            throw new InvalidDataException("The texture is shorter than its size.");

        // "DDSR" headers often leave the masks empty: the game stores BGRA (BGR for 24 bits).
        if (masks.R == 0 && masks.G == 0 && masks.B == 0)
            masks = (0x00FF0000, 0x0000FF00, 0x000000FF, bytesPerPixel == 4 ? 0xFF000000 : 0);

        var output = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            var p = pixels.Slice(i * bytesPerPixel, bytesPerPixel);
            var value = bytesPerPixel == 4
                ? BinaryPrimitives.ReadUInt32LittleEndian(p)
                : (uint)(p[0] | (p[1] << 8) | (p[2] << 16));

            output[i * 4] = Extract(value, masks.B);
            output[i * 4 + 1] = Extract(value, masks.G);
            output[i * 4 + 2] = Extract(value, masks.R);
            output[i * 4 + 3] = masks.A == 0 ? (byte)255 : Extract(value, masks.A);
        }

        return new DecodedImage(width, height, output);
    }

    /// <summary>The channel of <paramref name="value"/> selected by <paramref name="mask"/>, scaled to 0..255.</summary>
    private static byte Extract(uint value, uint mask)
    {
        if (mask == 0)
            return 0;

        var shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        var channel = (value & mask) >> shift;
        return (byte)(max == 255 ? channel : channel * 255 / max);
    }

    #endregion

    #region Block compression (DXT)

    private delegate void BlockDecoder(ReadOnlySpan<byte> block, Span<uint> texels);

    private static DecodedImage DecodeBlocks(ReadOnlySpan<byte> data, int width, int height, int blockSize, BlockDecoder decodeBlock)
    {
        var blocksX = (width + 3) / 4;
        var blocksY = (height + 3) / 4;
        if (data.Length < blocksX * blocksY * blockSize)
            throw new InvalidDataException("The texture is shorter than its size.");

        var output = new byte[width * height * 4];
        Span<uint> texels = stackalloc uint[16];

        for (var by = 0; by < blocksY; by++)
        for (var bx = 0; bx < blocksX; bx++)
        {
            decodeBlock(data.Slice((by * blocksX + bx) * blockSize, blockSize), texels);

            for (var ty = 0; ty < 4; ty++)
            {
                var y = by * 4 + ty;
                if (y >= height)
                    break;

                for (var tx = 0; tx < 4; tx++)
                {
                    var x = bx * 4 + tx;
                    if (x >= width)
                        break;

                    BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan((y * width + x) * 4), texels[ty * 4 + tx]);
                }
            }
        }

        return new DecodedImage(width, height, output);
    }

    /// <summary>DXT1: two RGB565 colors and 2-bit indices; color 3 is transparent when color0 &lt;= color1.</summary>
    private static void DecodeDxt1Block(ReadOnlySpan<byte> block, Span<uint> texels) =>
        DecodeColorBlock(block, texels, allowTransparent: true);

    /// <summary>DXT3: explicit 4-bit alpha per texel, then a DXT1 color block.</summary>
    private static void DecodeDxt3Block(ReadOnlySpan<byte> block, Span<uint> texels)
    {
        DecodeColorBlock(block[8..], texels, allowTransparent: false);
        var alphaBits = BinaryPrimitives.ReadUInt64LittleEndian(block);
        for (var i = 0; i < 16; i++)
        {
            var alpha = (uint)((alphaBits >> (i * 4)) & 0xF) * 17;
            texels[i] = (texels[i] & 0x00FFFFFF) | (alpha << 24);
        }
    }

    /// <summary>DXT5: two alpha values with 3-bit interpolated indices, then a DXT1 color block.</summary>
    private static void DecodeDxt5Block(ReadOnlySpan<byte> block, Span<uint> texels)
    {
        DecodeColorBlock(block[8..], texels, allowTransparent: false);

        Span<uint> alphas = stackalloc uint[8];
        alphas[0] = block[0];
        alphas[1] = block[1];
        if (alphas[0] > alphas[1])
        {
            for (var i = 1; i <= 6; i++)
                alphas[i + 1] = (uint)(((7 - i) * alphas[0] + i * alphas[1]) / 7);
        }
        else
        {
            for (var i = 1; i <= 4; i++)
                alphas[i + 1] = (uint)(((5 - i) * alphas[0] + i * alphas[1]) / 5);
            alphas[6] = 0;
            alphas[7] = 255;
        }

        ulong indices = 0;
        for (var i = 0; i < 6; i++)
            indices |= (ulong)block[2 + i] << (8 * i);

        for (var i = 0; i < 16; i++)
        {
            var alpha = alphas[(int)((indices >> (i * 3)) & 0x7)];
            texels[i] = (texels[i] & 0x00FFFFFF) | (alpha << 24);
        }
    }

    private static void DecodeColorBlock(ReadOnlySpan<byte> block, Span<uint> texels, bool allowTransparent)
    {
        var c0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var c1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var (r0, g0, b0) = Rgb565(c0);
        var (r1, g1, b1) = Rgb565(c1);

        Span<uint> colors = stackalloc uint[4];
        colors[0] = Bgra(r0, g0, b0, 255);
        colors[1] = Bgra(r1, g1, b1, 255);
        if (c0 > c1 || !allowTransparent)
        {
            colors[2] = Bgra((2 * r0 + r1) / 3, (2 * g0 + g1) / 3, (2 * b0 + b1) / 3, 255);
            colors[3] = Bgra((r0 + 2 * r1) / 3, (g0 + 2 * g1) / 3, (b0 + 2 * b1) / 3, 255);
        }
        else
        {
            colors[2] = Bgra((r0 + r1) / 2, (g0 + g1) / 2, (b0 + b1) / 2, 255);
            colors[3] = 0; // transparent black
        }

        var indices = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        for (var i = 0; i < 16; i++)
            texels[i] = colors[(int)((indices >> (i * 2)) & 0x3)];
    }

    private static (int R, int G, int B) Rgb565(ushort c)
    {
        var r = (c >> 11) & 0x1F;
        var g = (c >> 5) & 0x3F;
        var b = c & 0x1F;
        return ((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2));
    }

    /// <summary>A pixel as the little-endian uint32 of the bytes B, G, R, A.</summary>
    private static uint Bgra(int r, int g, int b, int a) => (uint)(b | (g << 8) | (r << 16) | (a << 24));

    #endregion

    private static uint FourCc(string code) =>
        (uint)(code[0] | (code[1] << 8) | (code[2] << 16) | (code[3] << 24));
}
