using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes one UASTC block of 4×4 texels (Khronos Data Format Specification 1.4, section 25) into 8-bit RGBA texels.
/// </summary>
/// <remarks>
/// <para>
/// The block is read from its least significant bit: the Huffman-coded mode index (section 25.3), the configuration of
/// the mode (Table 201, the transcoding hints included, which decoding skips), the packed trits or quints of the endpoints
/// (Table 203), the endpoint bits (Table 204), then the weights in raster order, the anchor texel of each subset one bit
/// shorter (Table 209). The endpoints are unquantized as ASTC unquantizes them (section 25.7.2.4) and the weights by bit
/// replication; the texels are interpolated as ASTC does in decode_unorm8 (section 25.7.2.8), with the sRGB expansion of
/// the endpoints when the texture has the sRGB transfer function.
/// </para>
/// <para>The reserved mode 19, a mode code that matches no mode and a partition pattern out of range are rejected.</para>
/// </remarks>
internal static class UastcBlockDecoder
{
    // The modes (section 25.5): endpoint bits, weight bits, subsets, dual plane, components (3 RGB, 4 RGBA, 2 LA),
    // packed trits (1) or quints (2), and the transcoding hints the configuration has
    private static readonly Mode[] Modes =
    [
        new(6, 4, 1, false, 3, 1, Bc1H1: true, EtcBi: true, Etc2: false),
        new(8, 2, 1, false, 3, 0, Bc1H1: true, EtcBi: true, Etc2: false),
        new(4, 3, 2, false, 3, 0, Bc1H1: true, EtcBi: true, Etc2: false),
        new(2, 2, 3, false, 3, 1, Bc1H1: true, EtcBi: true, Etc2: false),
        new(3, 2, 2, false, 3, 2, Bc1H1: true, EtcBi: true, Etc2: false),
        new(8, 3, 1, false, 3, 0, Bc1H1: true, EtcBi: true, Etc2: false),
        new(5, 2, 1, true, 3, 2, Bc1H1: true, EtcBi: true, Etc2: false),
        new(3, 2, 2, false, 3, 2, Bc1H1: true, EtcBi: true, Etc2: false),
        default,
        new(4, 2, 2, false, 4, 0, Bc1H1: true, EtcBi: true, Etc2: true),
        new(4, 4, 1, false, 4, 1, Bc1H1: false, EtcBi: false, Etc2: true),
        new(4, 2, 1, true, 4, 1, Bc1H1: false, EtcBi: false, Etc2: true),
        new(6, 3, 1, false, 4, 1, Bc1H1: false, EtcBi: false, Etc2: true),
        new(8, 1, 1, true, 4, 0, Bc1H1: true, EtcBi: true, Etc2: true),
        new(8, 2, 1, false, 4, 0, Bc1H1: true, EtcBi: true, Etc2: true),
        new(8, 4, 1, false, 2, 0, Bc1H1: true, EtcBi: true, Etc2: true),
        new(8, 2, 2, false, 2, 0, Bc1H1: true, EtcBi: true, Etc2: true),
        new(8, 2, 1, true, 2, 0, Bc1H1: true, EtcBi: true, Etc2: true),
        new(5, 5, 1, false, 3, 0, Bc1H1: true, EtcBi: true, Etc2: false),
    ];

    /// <summary>Decodes a block.</summary>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="srgb">Whether the texture has the sRGB transfer function, which expands the endpoints with 0x80.</param>
    /// <param name="destination">The top-left texel of the block in a strip of 8-bit RGBA texels.</param>
    /// <param name="stride">The length of a row of the strip, in bytes.</param>
    /// <exception cref="InvalidImageContentException">The mode is reserved or not defined, or the partition pattern is out of range.</exception>
    public static void Decode(ReadOnlySpan<byte> block, bool srgb, Span<byte> destination, int stride)
    {
        var bits = BinaryPrimitives.ReadUInt128LittleEndian(block);
        var (modeIndex, modeLength) = DecodeModeIndex((int)(ulong)(bits & 0x7F));
        var position = modeLength;
        if (modeIndex == 8)
        {
            // Section 25.4: a solid color
            for (var y = 0; y < 4; y++)
            {
                for (var x = 0; x < 4; x++)
                {
                    var texel = destination.Slice((y * stride) + (x * 4), 4);
                    for (var c = 0; c < 4; c++)
                    {
                        texel[c] = (byte)Read(bits, 5 + (8 * c), 8);
                    }
                }
            }

            return;
        }

        var mode = Modes[modeIndex];

        // Table 201: the hints, then the partition pattern or the component selector
        position += 1 + (mode.Bc1H1 ? 1 : 0) + 1 + 1 + 3 + 3 + (mode.EtcBi ? 5 : 0) + (mode.Etc2 ? 8 : 0);
        var pattern = 0;
        if (mode.Subsets > 1)
        {
            var patternBits = modeIndex == 3 ? 4 : 5;
            pattern = (int)Read(bits, position, patternBits);
            position += patternBits;
            var patternCount = modeIndex switch { 3 => 11, 7 => 19, _ => 30 };
            if (pattern >= patternCount)
                throw Ktx2Format.Invalid(string.Create(CultureInfo.InvariantCulture, $"a UASTC block of the mode {modeIndex} has the partition pattern {pattern}; the mode has {patternCount}."));
        }

        var selector = -1;
        if (modeIndex is 6 or 11 or 13)
        {
            selector = (int)Read(bits, position, 2);
            position += 2;
        }
        else if (modeIndex == 17)
        {
            selector = 3;
        }

        // Tables 203 and 204: the packed trits or quints, then the bits of the endpoints in the order RL0, RH0, GL0, ...
        var valueCount = mode.Components * 2 * mode.Subsets;
        Span<int> high = stackalloc int[18];
        high.Clear();
        if (mode.Packing == 1)
        {
            for (var i = 0; i < valueCount; i += 5)
            {
                var count = Math.Min(5, valueCount - i);
                var length = count switch { 5 => 8, 3 => 5, 1 => 2, _ => throw new InvalidOperationException("Unexpected number of trits.") };
                var packed = (int)Read(bits, position, length);
                position += length;
                for (var j = 0; j < count; j++)
                {
                    high[i + j] = packed % 3;
                    packed /= 3;
                }
            }
        }
        else if (mode.Packing == 2)
        {
            for (var i = 0; i < valueCount; i += 3)
            {
                var packed = (int)Read(bits, position, 7);
                position += 7;
                for (var j = 0; j < 3 && i + j < valueCount; j++)
                {
                    high[i + j] = packed % 5;
                    packed /= 5;
                }
            }
        }

        // The unquantized endpoints of each subset: RL, RH, GL, GH, BL, BH, AL, AH (alpha 255 without it; L for R, G and B)
        Span<int> endpoints = stackalloc int[3 * 8];
        var value = 0;
        for (var subset = 0; subset < mode.Subsets; subset++)
        {
            var e = endpoints.Slice(subset * 8, 8);
            e[6] = 255;
            e[7] = 255;
            for (var c = 0; c < mode.Components; c++)
            {
                for (var h = 0; h < 2; h++)
                {
                    var low = (int)Read(bits, position, mode.EndpointBits);
                    position += mode.EndpointBits;
                    var trits = mode.Packing == 1 ? 1 : 0;
                    var quints = mode.Packing == 2 ? 1 : 0;
                    var unquantized = mode.Packing == 0
                        ? AstcBlockDecoder.UnquantizeColor(low, 0, 0, mode.EndpointBits)
                        : AstcBlockDecoder.UnquantizeColor((high[value] << mode.EndpointBits) | low, trits, quints, mode.EndpointBits);
                    value++;

                    // Section 25.7.2.5: luminance gives the three colors
                    var component = mode.Components == 2 ? (c == 0 ? 0 : 3) : c;
                    e[(component * 2) + h] = unquantized;
                    if (mode.Components == 2 && c == 0)
                    {
                        e[2 + h] = unquantized;
                        e[4 + h] = unquantized;
                    }
                }
            }
        }

        // Table 209: the weights in raster order, plane 0 then plane 1 for each texel; an anchor texel lacks its top bit
        var partitions = mode.Subsets switch
        {
            1 => default,
            2 => modeIndex == 7 ? Patterns2Mode7.Slice(pattern * 16, 16) : Patterns2.Slice(pattern * 16, 16),
            _ => Patterns3.Slice(pattern * 16, 16),
        };
        Span<bool> seen = stackalloc bool[3];
        seen.Clear();
        Span<int> weights = stackalloc int[32];
        var planes = mode.DualPlane ? 2 : 1;
        for (var i = 0; i < 16; i++)
        {
            var subset = mode.Subsets == 1 ? 0 : partitions[i];
            var anchor = !seen[subset];
            seen[subset] = true;
            for (var plane = 0; plane < planes; plane++)
            {
                var length = anchor ? mode.WeightBits - 1 : mode.WeightBits;
                var weight = (int)Read(bits, position, length);
                position += length;
                var unquantized = Replicate6(weight, mode.WeightBits);
                weights[(i * 2) + plane] = unquantized > 32 ? unquantized + 1 : unquantized;
            }
        }

        // Section 25.7.2.8: the endpoints expanded to 16 bits, interpolated, and the top 8 bits kept
        for (var i = 0; i < 16; i++)
        {
            var (x, y) = (i & 3, i >> 2);
            var subset = mode.Subsets == 1 ? 0 : partitions[i];
            var e = endpoints.Slice(subset * 8, 8);
            var texel = destination.Slice((y * stride) + (x * 4), 4);
            for (var c = 0; c < 4; c++)
            {
                var weight = c == selector ? weights[(i * 2) + 1] : weights[i * 2];
                var low = (e[c * 2] << 8) | (srgb ? 0x80 : e[c * 2]);
                var highValue = (e[(c * 2) + 1] << 8) | (srgb ? 0x80 : e[(c * 2) + 1]);
                texel[c] = (byte)((((low * (64 - weight)) + (highValue * weight) + 32) >> 6) >> 8);
            }
        }
    }

    /// <summary>Decodes the mode index of Table 199 from the low 7 bits of a block.</summary>
    private static (int Mode, int Length) DecodeModeIndex(int bits)
    {
        if ((bits & 3) == 0)
            return (11, 2);

        switch (bits & 7)
        {
            case 0x2:
                return (10, 3);
            case 0x6:
                return (12, 3);
        }

        switch (bits & 0xF)
        {
            case 0x1:
                return (0, 4);
            case 0x9:
                return (18, 4);
        }

        var five = bits & 0x1F;
        var mode = five switch
        {
            0x03 => 3,
            0x07 => 7,
            0x0B => 5,
            0x0D => 14,
            0x0F => 9,
            0x13 => 4,
            0x17 => 8,
            0x1B => 6,
            0x1D => 2,
            0x1F => 13,
            _ => -1,
        };
        if (mode >= 0)
            return (mode, 5);

        switch (bits & 0x3F)
        {
            case 0x15:
                return (16, 6);
            case 0x25:
                return (17, 6);
            case 0x35:
                return (1, 6);
        }

        if (bits == 0x05)
            return (15, 7);

        throw Ktx2Format.Invalid(bits == 0x45
            ? "a UASTC block uses the reserved mode 19."
            : "a UASTC block has a mode code that matches no mode.");
    }

    private static int Replicate6(int value, int bitCount)
    {
        var result = 0;
        for (var filled = 0; filled < 6; filled += bitCount)
        {
            var shift = 6 - filled - bitCount;
            result |= shift >= 0 ? value << shift : value >> -shift;
        }

        return result & 0x3F;
    }

    private static uint Read(UInt128 bits, int position, int count) => count == 0 || position >= 128 ? 0 : (uint)(ulong)((bits >> position) & ((UInt128.One << count) - 1));

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Mode(int EndpointBits, int WeightBits, int Subsets, bool DualPlane, int Components, int Packing, bool Bc1H1, bool EtcBi, bool Etc2);

    // Tables 224, 225 and 226: the subset of each texel, in raster order, for each pattern
    private static ReadOnlySpan<byte> Patterns2Mode7 =>
    [
        0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0,
        1, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1,
        0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0,
        0, 0, 0, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
        1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0,
        0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 0,
        1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0,
        0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 0,
        1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0,
        0, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 0,
        1, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
    ];

    private static ReadOnlySpan<byte> Patterns2 =>
    [
        0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
        0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1,
        1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0,
        0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 0, 0,
        0, 0, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 0, 0, 1, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 1, 1,
        1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 0, 0, 0,
        1, 1, 1, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0,
        1, 0, 0, 0, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1,
        1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 1,
        0, 1, 1, 1, 0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0,
        0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0,
        1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 0, 0, 1, 1,
        1, 0, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 1, 0,
        0, 0, 1, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0,
        1, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0, 0, 1, 1,
        0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1, 0,
        1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1,
        1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0, 1, 0,
        1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0,
        1, 0, 0, 1, 0, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 0,
    ];

    private static ReadOnlySpan<byte> Patterns3 =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 2, 2, 1, 1, 2, 2,
        1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 2, 2, 2, 2,
        1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 2, 2, 2, 2,
        1, 1, 1, 1, 2, 2, 2, 2, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2, 0,
        0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2, 0, 1, 1, 2,
        0, 2, 1, 1, 0, 2, 1, 1, 0, 2, 1, 1, 0, 2, 1, 1,
        2, 0, 0, 0, 2, 0, 0, 0, 2, 1, 1, 1, 2, 1, 1, 1,
        2, 0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2, 2, 0, 1, 2,
        1, 1, 1, 1, 0, 0, 0, 0, 2, 2, 2, 2, 1, 1, 1, 1,
        0, 0, 2, 2, 0, 0, 1, 1, 0, 0, 1, 1, 0, 0, 2, 2,
    ];
}
