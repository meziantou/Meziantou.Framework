using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes ETC1, ETC2 and EAC blocks of 4×4 texels (Khronos Data Format Specification 1.4, sections 21 and 22).
/// </summary>
/// <remarks>
/// <para>
/// A 64-bit block is read big-endian (<c>q0</c> is the high byte). The color blocks are in the individual or the
/// differential mode of ETC1, or, when a sum of the differential mode leaves [0, 31], in the T, H or planar mode of ETC2
/// (section 22.1). With punchthrough alpha (section 22.9) the bit of the individual mode is the opaque bit: a block without
/// it has no small modifiers, and its pixel index 2 is transparent black.
/// </para>
/// <para>
/// An EAC block gives 8-bit alpha (section 22.3), or 11-bit values (sections 22.5 and 22.7): the unsigned ones are written
/// as 16-bit samples by bit replication, as the specification extends them, and the signed ones as floats, the value
/// divided by 1023.
/// </para>
/// </remarks>
internal static class Etc2BlockDecoder
{
    // Table 142 (and 148): the intensity modifiers of the individual and differential modes, by table codeword
    public static ReadOnlySpan<short> IntensityModifiers =>
    [
        -8, -2, 2, 8,
        -17, -5, 5, 17,
        -29, -9, 9, 29,
        -42, -13, 13, 42,
        -60, -18, 18, 60,
        -80, -24, 24, 80,
        -106, -33, 33, 106,
        -183, -47, 47, 183,
    ];

    // Table 144: the distances of the T and H modes
    public static ReadOnlySpan<byte> Distances => [3, 6, 11, 16, 23, 32, 41, 64];

    // Table 146: the modifiers of the alpha blocks and of EAC
    public static ReadOnlySpan<sbyte> AlphaModifiers =>
    [
        -3, -6, -9, -15, 2, 5, 8, 14,
        -3, -7, -10, -13, 2, 6, 9, 12,
        -2, -5, -8, -13, 1, 4, 7, 12,
        -2, -4, -6, -13, 1, 3, 5, 12,
        -3, -6, -8, -12, 2, 5, 7, 11,
        -3, -7, -9, -11, 2, 6, 8, 10,
        -4, -7, -8, -11, 3, 6, 7, 10,
        -3, -5, -8, -11, 2, 4, 7, 10,
        -2, -6, -8, -10, 1, 5, 7, 9,
        -2, -5, -8, -10, 1, 4, 7, 9,
        -2, -4, -8, -10, 1, 3, 7, 9,
        -2, -5, -7, -10, 1, 4, 6, 9,
        -3, -4, -7, -10, 2, 3, 6, 9,
        -1, -2, -3, -10, 0, 1, 2, 9,
        -4, -6, -8, -9, 3, 5, 7, 8,
        -3, -5, -7, -9, 2, 4, 6, 8,
    ];

    /// <summary>Decodes an ETC2 color block (RGB, RGB with punchthrough alpha, or RGBA with its alpha block) to 8-bit RGBA texels.</summary>
    /// <param name="format">The format.</param>
    /// <param name="block">The block: 8 bytes, or 16 for RGBA (the alpha block first).</param>
    /// <param name="destination">The top-left texel of the block in a strip of RGBA texels.</param>
    /// <param name="stride">The length of a row of the strip, in bytes.</param>
    public static void Decode(Ktx2VkFormat format, ReadOnlySpan<byte> block, Span<byte> destination, int stride)
    {
        if (format.BytesPerBlock == 16)
        {
            DecodeColor(BinaryPrimitives.ReadUInt64BigEndian(block[8..]), punchthrough: false, destination, stride);
            DecodeAlpha(BinaryPrimitives.ReadUInt64BigEndian(block), destination[3..], stride);
            return;
        }

        DecodeColor(BinaryPrimitives.ReadUInt64BigEndian(block), punchthrough: (format.ChannelMask & 8) != 0, destination, stride);
    }

    /// <summary>Decodes an ETC1 block (an ETC1S block included) to 8-bit RGBA texels.</summary>
    public static void DecodeEtc1(ulong block, Span<byte> destination, int stride) => DecodeColor(block, punchthrough: false, destination, stride);

    /// <summary>Decodes an EAC R11 or RG11 block to 16-bit (unsigned) or floating-point (signed) RGBA texels: red, green, 0, 1.</summary>
    public static void DecodeEac(Ktx2VkFormat format, ReadOnlySpan<byte> block, Span<byte> destination, int stride)
    {
        var channels = format.BytesPerBlock / 8;
        Span<short> values = stackalloc short[16];
        for (var channel = 0; channel < channels; channel++)
        {
            DecodeEleven(BinaryPrimitives.ReadUInt64BigEndian(block[(channel * 8)..]), format.IsSigned, values);
            for (var i = 0; i < 16; i++)
            {
                // The pixel indices are in the order a..p: column by column
                var (x, y) = (i >> 2, i & 3);
                if (format.IsSigned)
                {
                    var texel = destination.Slice((y * stride) + (x * 16), 16);
                    BinaryPrimitives.WriteSingleLittleEndian(texel[(channel * 4)..], values[i] / 1023f);
                    if (channel == 0)
                    {
                        BinaryPrimitives.WriteSingleLittleEndian(texel[4..], 0);
                        BinaryPrimitives.WriteSingleLittleEndian(texel[8..], 0);
                        BinaryPrimitives.WriteSingleLittleEndian(texel[12..], 1);
                    }
                }
                else
                {
                    var texel = destination.Slice((y * stride) + (x * 8), 8);
                    var value = (ushort)values[i];
                    BitConverter.TryWriteBytes(texel[(channel * 2)..], (ushort)((value << 5) | (value >> 6)));
                    if (channel == 0)
                    {
                        BitConverter.TryWriteBytes(texel[2..], (ushort)0);
                        BitConverter.TryWriteBytes(texel[4..], (ushort)0);
                        BitConverter.TryWriteBytes(texel[6..], ushort.MaxValue);
                    }
                }
            }
        }
    }

    private static void DecodeColor(ulong block, bool punchthrough, Span<byte> destination, int stride)
    {
        var high = (uint)(block >> 32);
        var low = (uint)block;
        var differential = (high & 2) != 0;
        var opaque = !punchthrough || differential;
        Span<byte> paint = stackalloc byte[16];
        if (!punchthrough && !differential)
        {
            // Individual mode: two 4-bit base colors
            var color1 = (Extend4((high >> 28) & 0xF), Extend4((high >> 20) & 0xF), Extend4((high >> 12) & 0xF));
            var color2 = (Extend4((high >> 24) & 0xF), Extend4((high >> 16) & 0xF), Extend4((high >> 8) & 0xF));
            DecodeSubblocks(high, low, color1, color2, opaque: true, destination, stride);
            return;
        }

        // Differential mode, unless a sum overflows: T, H or planar mode
        var r = (int)((high >> 27) & 0x1F);
        var g = (int)((high >> 19) & 0x1F);
        var b = (int)((high >> 11) & 0x1F);
        var rd = SignExtend3((high >> 24) & 7);
        var gd = SignExtend3((high >> 16) & 7);
        var bd = SignExtend3((high >> 8) & 7);
        if (r + rd is < 0 or > 31)
        {
            // T mode
            var red1 = (((high >> 27) & 3) << 2) | ((high >> 24) & 3);
            var color1 = (Extend4(red1), Extend4((high >> 20) & 0xF), Extend4((high >> 16) & 0xF));
            var color2 = (Extend4((high >> 12) & 0xF), Extend4((high >> 8) & 0xF), Extend4((high >> 4) & 0xF));
            var distance = Distances[(int)((((high >> 2) & 3) << 1) | (high & 1))];
            SetPaint(paint, 0, color1, 0);
            SetPaint(paint, 1, color2, distance);
            SetPaint(paint, 2, color2, 0);
            SetPaint(paint, 3, color2, -distance);
            WritePaint(paint, low, opaque, destination, stride);
        }
        else if (g + gd is < 0 or > 31)
        {
            // H mode
            var green1 = (((high >> 24) & 7) << 1) | ((high >> 20) & 1);
            var blue1 = (((high >> 19) & 1) << 3) | ((high >> 15) & 7);
            var color1 = (Extend4((high >> 27) & 0xF), Extend4(green1), Extend4(blue1));
            var color2 = (Extend4((high >> 11) & 0xF), Extend4((high >> 7) & 0xF), Extend4((high >> 3) & 0xF));
            var value1 = (color1.Item1 << 16) | (color1.Item2 << 8) | color1.Item3;
            var value2 = (color2.Item1 << 16) | (color2.Item2 << 8) | color2.Item3;
            var index = (int)((((high >> 2) & 1) << 2) | ((high & 1) << 1)) | (value1 >= value2 ? 1 : 0);
            var distance = Distances[index];
            SetPaint(paint, 0, color1, distance);
            SetPaint(paint, 1, color1, -distance);
            SetPaint(paint, 2, color2, distance);
            SetPaint(paint, 3, color2, -distance);
            WritePaint(paint, low, opaque, destination, stride);
        }
        else if (b + bd is < 0 or > 31)
        {
            // Planar mode: always opaque (section 22.9)
            DecodePlanar(block, destination, stride);
        }
        else
        {
            var color1 = (Extend5((uint)r), Extend5((uint)g), Extend5((uint)b));
            var color2 = (Extend5((uint)(r + rd)), Extend5((uint)(g + gd)), Extend5((uint)(b + bd)));
            DecodeSubblocks(high, low, color1, color2, opaque, destination, stride);
        }
    }

    private static void DecodeSubblocks(uint high, uint low, (int R, int G, int B) color1, (int R, int G, int B) color2, bool opaque, Span<byte> destination, int stride)
    {
        var flip = (high & 1) != 0;
        var table1 = (int)((high >> 5) & 7);
        var table2 = (int)((high >> 2) & 7);
        for (var i = 0; i < 16; i++)
        {
            var (x, y) = (i >> 2, i & 3);
            var second = flip ? y >= 2 : x >= 2;
            var (color, table) = second ? (color2, table2) : (color1, table1);
            var index = (int)((((low >> (16 + i)) & 1) << 1) | ((low >> i) & 1));
            var texel = destination.Slice((y * stride) + (x * 4), 4);
            if (!opaque && index == 2)
            {
                texel.Clear();
                continue;
            }

            // Table 143: 00 is +a, 01 +b, 10 -a, 11 -b; without the opaque bit, the small modifiers are 0 (tables 149 and 151)
            var modifier = index switch
            {
                0 => IntensityModifiers[(table * 4) + 2],
                1 => IntensityModifiers[(table * 4) + 3],
                2 => IntensityModifiers[(table * 4) + 1],
                _ => IntensityModifiers[table * 4],
            };
            if (!opaque && index == 0)
            {
                modifier = 0;
            }

            texel[0] = Clamp255(color.R + modifier);
            texel[1] = Clamp255(color.G + modifier);
            texel[2] = Clamp255(color.B + modifier);
            texel[3] = 255;
        }
    }

    private static void SetPaint(Span<byte> paint, int index, (int R, int G, int B) color, int distance)
    {
        paint[index * 4] = Clamp255(color.R + distance);
        paint[(index * 4) + 1] = Clamp255(color.G + distance);
        paint[(index * 4) + 2] = Clamp255(color.B + distance);
        paint[(index * 4) + 3] = 255;
    }

    private static void WritePaint(ReadOnlySpan<byte> paint, uint low, bool opaque, Span<byte> destination, int stride)
    {
        for (var i = 0; i < 16; i++)
        {
            var (x, y) = (i >> 2, i & 3);
            var index = (int)((((low >> (16 + i)) & 1) << 1) | ((low >> i) & 1));
            var texel = destination.Slice((y * stride) + (x * 4), 4);
            if (!opaque && index == 2)
            {
                texel.Clear();
            }
            else
            {
                paint.Slice(index * 4, 4).CopyTo(texel);
            }
        }
    }

    private static void DecodePlanar(ulong block, Span<byte> destination, int stride)
    {
        // Table 141 part g: three colors of 6, 7 and 6 bits, some split in pieces
        var r = Extend6((uint)(block >> 57) & 0x3F);
        var g = Extend7((uint)((((block >> 56) & 1) << 6) | ((block >> 49) & 0x3F)));
        var b = Extend6((uint)((((block >> 48) & 1) << 5) | (((block >> 43) & 3) << 3) | ((block >> 39) & 7)));
        var rh = Extend6((uint)((((block >> 34) & 0x1F) << 1) | ((block >> 32) & 1)));
        var gh = Extend7((uint)(block >> 25) & 0x7F);
        var bh = Extend6((uint)(block >> 19) & 0x3F);
        var rv = Extend6((uint)(block >> 13) & 0x3F);
        var gv = Extend7((uint)(block >> 6) & 0x7F);
        var bv = Extend6((uint)block & 0x3F);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var texel = destination.Slice((y * stride) + (x * 4), 4);
                texel[0] = Clamp255(((x * (rh - r)) + (y * (rv - r)) + (4 * r) + 2) >> 2);
                texel[1] = Clamp255(((x * (gh - g)) + (y * (gv - g)) + (4 * g) + 2) >> 2);
                texel[2] = Clamp255(((x * (bh - b)) + (y * (bv - b)) + (4 * b) + 2) >> 2);
                texel[3] = 255;
            }
        }
    }

    /// <summary>Decodes the 8-bit alpha block of RGBA ETC2 (section 22.3) into the alpha of the texels.</summary>
    private static void DecodeAlpha(ulong block, Span<byte> destination, int stride)
    {
        var baseCodeword = (int)(block >> 56);
        var multiplier = (int)((block >> 52) & 0xF);
        var table = (int)((block >> 48) & 0xF);
        for (var i = 0; i < 16; i++)
        {
            var (x, y) = (i >> 2, i & 3);
            var index = (int)((block >> (45 - (3 * i))) & 7);
            destination[(y * stride) + (x * 4)] = Clamp255(baseCodeword + (AlphaModifiers[(table * 8) + index] * multiplier));
        }
    }

    /// <summary>Decodes an R11 EAC block (sections 22.5 and 22.7) into 11-bit values, in the order a..p.</summary>
    private static void DecodeEleven(ulong block, bool signed, Span<short> values)
    {
        var baseCodeword = signed ? Math.Max((int)(sbyte)(byte)(block >> 56), -127) : (int)(block >> 56);
        var multiplier = (int)((block >> 52) & 0xF);
        var table = (int)((block >> 48) & 0xF);
        for (var i = 0; i < 16; i++)
        {
            var index = (int)((block >> (45 - (3 * i))) & 7);
            var modifier = AlphaModifiers[(table * 8) + index];

            // A multiplier of 0 is 1/8 (equations 6 and 9)
            var term = multiplier == 0 ? modifier : modifier * multiplier * 8;
            values[i] = signed
                ? (short)Math.Clamp((baseCodeword * 8) + term, -1023, 1023)
                : (short)Math.Clamp((baseCodeword * 8) + 4 + term, 0, 2047);
        }
    }

    private static int Extend4(uint value) => (int)((value << 4) | value);

    private static int Extend5(uint value) => (int)((value << 3) | (value >> 2));

    private static int Extend6(uint value) => (int)((value << 2) | (value >> 4));

    private static int Extend7(uint value) => (int)((value << 1) | (value >> 6));

    private static int SignExtend3(uint value) => ((int)value << 29) >> 29;

    private static byte Clamp255(int value) => (byte)Math.Clamp(value, 0, 255);
}
