using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The decoders of the blocks of 4×4 texels of the S3TC (BC1, BC2, BC3), RGTC (BC4, BC5) and BPTC (BC6H, BC7) formats,
/// written from the Khronos Data Format Specification 1.3.1, sections 18 to 20, and the Direct3D 11.3 functional
/// specification. They are independent of any container: DDS and KTX 2.0 use them.
/// </summary>
/// <remarks>
/// <para>
/// A block is decoded to its 16 texels in row-major order (texel <c>x + 4y</c>). The specification defines BC1 to BC5 as
/// real numbers (endpoints of 5, 6 or 8 bits as fractions of their maximum, interpolated by thirds, halves, sevenths or
/// fifths); the 8-bit results here are those numbers rounded once to the nearest 8-bit value, halves up. BC7 is defined in
/// integers (Equation 2) and BC6H in half-float bit patterns: both are exact. The signed RGTC formats are decoded to the
/// single-precision float nearest to their value rounded to the nearest multiple of 1/127.
/// </para>
/// <para>
/// A reserved block is not an error: the specification gives its value. A BC7 block whose low byte is 0 decodes to 0 in
/// every channel, a BC6H block of a reserved mode to (0, 0, 0, 1).
/// </para>
/// </remarks>
internal static class BlockDecoder
{
    /// <summary>The number of bytes of a BC1 or BC4 block.</summary>
    public const int SmallBlockLength = 8;

    /// <summary>The number of bytes of a BC2, BC3, BC5, BC6H or BC7 block.</summary>
    public const int LargeBlockLength = 16;

    /// <summary>The half-float bit pattern of 1.</summary>
    private const ushort HalfOne = 0x3C00;

    /// <summary>
    /// Decodes a BC1 block (section 18.2, "BC1 with alpha": DXGI_FORMAT_BC1_UNORM has the 1-bit alpha of the three-color
    /// mode) to 16 RGBA texels of 8 bits.
    /// </summary>
    /// <param name="block">The 8 bytes of the block.</param>
    /// <param name="rgba">The 64 bytes of the texels.</param>
    public static void DecodeBc1(ReadOnlySpan<byte> block, Span<byte> rgba) => DecodeColors(block, rgba, threeColorMode: true);

    /// <summary>Decodes a BC2 block (section 18.3): 4-bit alpha, then the colors of BC1 in the four-color mode.</summary>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="rgba">The 64 bytes of the texels.</param>
    public static void DecodeBc2(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        DecodeColors(block[8..], rgba, threeColorMode: false);
        var alpha = BinaryPrimitives.ReadUInt64LittleEndian(block);
        for (var i = 0; i < 16; i++)
        {
            // alpha(x, y) / 15, which is exactly a multiple of 17 on the 8-bit scale
            rgba[(i * 4) + 3] = (byte)(((alpha >> (4 * i)) & 0xF) * 17);
        }
    }

    /// <summary>Decodes a BC3 block (section 18.4): the alpha of BC4, then the colors of BC1 in the four-color mode.</summary>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="rgba">The 64 bytes of the texels.</param>
    public static void DecodeBc3(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        DecodeColors(block[8..], rgba, threeColorMode: false);
        DecodeUnsignedChannel(block, rgba[3..], stride: 4);
    }

    /// <summary>
    /// Decodes an unsigned RGTC channel (the alpha of BC3, BC4 unsigned, a channel of BC5 unsigned; section 19.1) to 16
    /// samples of 8 bits.
    /// </summary>
    /// <param name="block">The 8 bytes of the channel.</param>
    /// <param name="samples">The samples: texel <c>i</c> is written at <c>i × stride</c>.</param>
    /// <param name="stride">The distance between two samples, in bytes.</param>
    public static void DecodeUnsignedChannel(ReadOnlySpan<byte> block, Span<byte> samples, int stride)
    {
        int red0 = block[0];
        int red1 = block[1];
        Span<byte> palette = stackalloc byte[8];
        palette[0] = (byte)red0;
        palette[1] = (byte)red1;
        if (red0 > red1)
        {
            for (var i = 1; i < 7; i++)
            {
                // ((7 - i) × RED0 + i × RED1) / 7: never a half on the 8-bit scale
                palette[i + 1] = (byte)RoundDivide(((7 - i) * red0) + (i * red1), 7);
            }
        }
        else
        {
            for (var i = 1; i < 5; i++)
            {
                palette[i + 1] = (byte)RoundDivide(((5 - i) * red0) + (i * red1), 5);
            }

            palette[6] = 0;
            palette[7] = 255;
        }

        var bits = ReadIndices48(block);
        for (var i = 0; i < 16; i++)
        {
            samples[i * stride] = palette[(int)(bits >> (3 * i)) & 7];
        }
    }

    /// <summary>
    /// Decodes a signed RGTC channel (BC4 signed, a channel of BC5 signed; section 19.2) to 16 floats in [-1, 1]: each value
    /// is rounded to the nearest multiple of 1/127, the precision of the format, as the unsigned values are rounded to the
    /// nearest multiple of 1/255 (DirectXTex decodes both so).
    /// </summary>
    /// <param name="block">The 8 bytes of the channel.</param>
    /// <param name="samples">The samples: texel <c>i</c> is written at <c>i × stride</c>.</param>
    /// <param name="stride">The distance between two samples, in floats.</param>
    public static void DecodeSignedChannel(ReadOnlySpan<byte> block, Span<float> samples, int stride)
    {
        // The comparison is made on the stored values; -128 is then read as -127 (RED = -1.0)
        int red0 = (sbyte)block[0];
        int red1 = (sbyte)block[1];
        var value0 = Math.Max(red0, -127);
        var value1 = Math.Max(red1, -127);
        Span<int> palette = stackalloc int[8];
        palette[0] = value0;
        palette[1] = value1;
        if (red0 > red1)
        {
            for (var i = 1; i < 7; i++)
            {
                palette[i + 1] = RoundDivideSigned(((7 - i) * value0) + (i * value1), 7);
            }
        }
        else
        {
            for (var i = 1; i < 5; i++)
            {
                palette[i + 1] = RoundDivideSigned(((5 - i) * value0) + (i * value1), 5);
            }

            palette[6] = -127;
            palette[7] = 127;
        }

        var bits = ReadIndices48(block);
        for (var i = 0; i < 16; i++)
        {
            samples[i * stride] = (float)(palette[(int)(bits >> (3 * i)) & 7] / 127.0);
        }
    }

    /// <summary>Decodes a BC7 block (section 20.1) to 16 RGBA texels of 8 bits.</summary>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="rgba">The 64 bytes of the texels.</param>
    public static void DecodeBc7(ReadOnlySpan<byte> block, Span<byte> rgba)
    {
        var reader = new BitReader(block);
        var mode = 0;
        while (mode < 8 && reader.Read(1) == 0)
        {
            mode++;
        }

        if (mode == 8)
        {
            // A low byte of 0 is reserved: every channel of every texel is 0
            rgba[..64].Clear();
            return;
        }

        var parameters = Bc7Modes[mode];
        var subsets = parameters.Subsets;
        var partition = (int)reader.Read(parameters.PartitionBits);
        var rotation = (int)reader.Read(parameters.RotationBits);
        var indexSelection = (int)reader.Read(parameters.IndexSelectionBits);

        // The endpoints: every red value (endpoint 0 and 1 of each subset), then green, blue and alpha
        var endpointCount = subsets * 2;
        Span<byte> endpoints = stackalloc byte[6 * 4];
        for (var channel = 0; channel < 3; channel++)
        {
            for (var e = 0; e < endpointCount; e++)
            {
                endpoints[(e * 4) + channel] = (byte)reader.Read(parameters.ColorBits);
            }
        }

        for (var e = 0; e < endpointCount; e++)
        {
            endpoints[(e * 4) + 3] = parameters.AlphaBits == 0 ? (byte)255 : (byte)reader.Read(parameters.AlphaBits);
        }

        // P-bits: one per endpoint, or one per subset shared by its two endpoints
        Span<int> pBits = stackalloc int[6];
        var hasPBit = parameters.EndpointPBits || parameters.SharedPBits;
        if (parameters.EndpointPBits)
        {
            for (var e = 0; e < endpointCount; e++)
            {
                pBits[e] = (int)reader.Read(1);
            }
        }
        else if (parameters.SharedPBits)
        {
            for (var s = 0; s < subsets; s++)
            {
                var bit = (int)reader.Read(1);
                pBits[s * 2] = bit;
                pBits[(s * 2) + 1] = bit;
            }
        }

        // Each value becomes the high bits of a byte, the P-bit below them, and its top bits are replicated below
        for (var e = 0; e < endpointCount; e++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var bits = channel == 3 ? parameters.AlphaBits : parameters.ColorBits;
                if (bits == 0)
                    continue;

                int value = endpoints[(e * 4) + channel];
                if (hasPBit)
                {
                    value = (value << 1) | pBits[e];
                    bits++;
                }

                value <<= 8 - bits;
                value |= value >> bits;
                endpoints[(e * 4) + channel] = (byte)value;
            }
        }

        // Primary indices, then secondary ones; the anchor of a subset has one bit less
        Span<byte> primary = stackalloc byte[16];
        Span<byte> secondary = stackalloc byte[16];
        for (var i = 0; i < 16; i++)
        {
            var bits = BptcTables.IsAnchor(subsets, partition, i) ? parameters.IndexBits - 1 : parameters.IndexBits;
            primary[i] = (byte)reader.Read(bits);
        }

        if (parameters.SecondaryIndexBits > 0)
        {
            for (var i = 0; i < 16; i++)
            {
                var bits = i == 0 ? parameters.SecondaryIndexBits - 1 : parameters.SecondaryIndexBits;
                secondary[i] = (byte)reader.Read(bits);
            }
        }

        var hasSecondary = parameters.SecondaryIndexBits > 0;
        var colorFromSecondary = hasSecondary && indexSelection == 1;
        var alphaFromSecondary = hasSecondary && indexSelection == 0;
        var colorWeights = BptcTables.GetWeights(colorFromSecondary ? parameters.SecondaryIndexBits : parameters.IndexBits);
        var alphaWeights = BptcTables.GetWeights(alphaFromSecondary ? parameters.SecondaryIndexBits : parameters.IndexBits);
        for (var i = 0; i < 16; i++)
        {
            var subset = BptcTables.GetSubset(subsets, partition, i);
            var e0 = endpoints.Slice(subset * 8, 4);
            var e1 = endpoints.Slice((subset * 8) + 4, 4);
            int colorWeight = colorWeights[colorFromSecondary ? secondary[i] : primary[i]];
            int alphaWeight = alphaWeights[alphaFromSecondary ? secondary[i] : primary[i]];
            var texel = rgba.Slice(i * 4, 4);
            for (var channel = 0; channel < 3; channel++)
            {
                texel[channel] = (byte)Interpolate(e0[channel], e1[channel], colorWeight);
            }

            texel[3] = (byte)Interpolate(e0[3], e1[3], alphaWeight);
            if (rotation != 0)
            {
                // Table 120: 1 swaps alpha and red, 2 alpha and green, 3 alpha and blue
                (texel[3], texel[rotation - 1]) = (texel[rotation - 1], texel[3]);
            }
        }
    }

    /// <summary>Decodes a BC6H block (section 20.2) to 16 texels of four half-float bit patterns (red, green, blue, alpha 1).</summary>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="rgba">The 64 half-float patterns of the texels.</param>
    /// <param name="isSigned">Whether the format is BC6H_SF16 (signed) rather than BC6H_UF16.</param>
    public static void DecodeBc6H(ReadOnlySpan<byte> block, Span<ushort> rgba, bool isSigned)
    {
        var reader = new BitReader(block);
        var modeNumber = (int)reader.Peek(2);
        if (modeNumber >= 2)
        {
            modeNumber = (int)reader.Peek(5);
        }

        var modeIndex = -1;
        var modes = BptcTables.Bc6HModes;
        for (var i = 0; i < modes.Length; i++)
        {
            if (modes[i].Number == modeNumber)
            {
                modeIndex = i;
                break;
            }
        }

        if (modeIndex < 0)
        {
            // A reserved mode (0x13, 0x17, 0x1B, 0x1F) decodes to (0, 0, 0, 1)
            for (var i = 0; i < 16; i++)
            {
                rgba.Slice(i * 4, 3).Clear();
                rgba[(i * 4) + 3] = HalfOne;
            }

            return;
        }

        var mode = modes[modeIndex];

        // The fields of the mode: 0 the mode, 1 the partition, then R0, G0, B0, R1, ... B3
        Span<int> fields = stackalloc int[14];
        fields.Clear();
        var layout = BptcTables.Bc6HLayouts.Slice(mode.LayoutOffset, mode.LayoutLength);
        foreach (var destination in layout)
        {
            fields[destination >> 4] |= (int)reader.Read(1) << (destination & 0xF);
        }

        var partition = fields[1];
        var subsets = mode.HasTwoSubsets ? 2 : 1;
        var endpointCount = subsets * 2;
        var endpointBits = mode.EndpointBits;
        Span<int> deltaBits = [mode.RedBits, mode.GreenBits, mode.BlueBits];
        Span<int> endpoints = stackalloc int[12];
        for (var e = 0; e < endpointCount; e++)
        {
            for (var channel = 0; channel < 3; channel++)
            {
                endpoints[(e * 3) + channel] = fields[2 + (e * 3) + channel];
            }
        }

        // E0 is sign-extended for the signed format; E1 to E3 for the signed format or transformed endpoints (deltas)
        for (var channel = 0; channel < 3; channel++)
        {
            if (isSigned)
            {
                endpoints[channel] = SignExtend(endpoints[channel], endpointBits);
            }

            for (var e = 1; e < endpointCount; e++)
            {
                ref var value = ref endpoints[(e * 3) + channel];
                if (mode.Transformed)
                {
                    // The deltas from E0, wrapped at the number of bits of an endpoint
                    var delta = SignExtend(value, deltaBits[channel]);
                    value = (endpoints[channel] + delta) & ((1 << endpointBits) - 1);
                    if (isSigned)
                    {
                        value = SignExtend(value, endpointBits);
                    }
                }
                else if (isSigned)
                {
                    value = SignExtend(value, endpointBits);
                }
            }
        }

        for (var i = 0; i < endpointCount * 3; i++)
        {
            endpoints[i] = isSigned ? UnquantizeSigned(endpoints[i], endpointBits) : UnquantizeUnsigned(endpoints[i], endpointBits);
        }

        // The indices follow the layout: 4 bits per texel with one subset, 3 bits with two; an anchor has one bit less
        var indexBits = subsets == 1 ? 4 : 3;
        var weights = BptcTables.GetWeights(indexBits);
        for (var i = 0; i < 16; i++)
        {
            var bits = BptcTables.IsAnchor(subsets, partition, i) ? indexBits - 1 : indexBits;
            int weight = weights[(int)reader.Read(bits)];
            var subset = BptcTables.GetSubset(subsets, partition, i);
            for (var channel = 0; channel < 3; channel++)
            {
                var e0 = endpoints[(subset * 6) + channel];
                var e1 = endpoints[(subset * 6) + 3 + channel];
                var value = (((64 - weight) * e0) + (weight * e1) + 32) >> 6;
                rgba[(i * 4) + channel] = isSigned ? FinishSigned(value) : (ushort)((value * 31) >> 6);
            }

            rgba[(i * 4) + 3] = HalfOne;
        }
    }

    /// <summary>The colors of BC1, BC2 and BC3 (section 18.1).</summary>
    private static void DecodeColors(ReadOnlySpan<byte> block, Span<byte> rgba, bool threeColorMode)
    {
        var color0 = BinaryPrimitives.ReadUInt16LittleEndian(block);
        var color1 = BinaryPrimitives.ReadUInt16LittleEndian(block[2..]);
        var bits = BinaryPrimitives.ReadUInt32LittleEndian(block[4..]);
        int r0 = color0 >> 11, g0 = (color0 >> 5) & 0x3F, b0 = color0 & 0x1F;
        int r1 = color1 >> 11, g1 = (color1 >> 5) & 0x3F, b1 = color1 & 0x1F;

        // The four colors of the palette, RGBA
        Span<byte> palette = stackalloc byte[16];
        SetColor(palette[0..], RoundDivide(r0 * 255, 31), RoundDivide(g0 * 255, 63), RoundDivide(b0 * 255, 31), 255);
        SetColor(palette[4..], RoundDivide(r1 * 255, 31), RoundDivide(g1 * 255, 63), RoundDivide(b1 * 255, 31), 255);
        if (!threeColorMode || color0 > color1)
        {
            // (2 × RGB0 + RGB1) / 3 and (RGB0 + 2 × RGB1) / 3, on the 8-bit scale
            SetColor(palette[8..], RoundDivide(((2 * r0) + r1) * 255, 3 * 31), RoundDivide(((2 * g0) + g1) * 255, 3 * 63), RoundDivide(((2 * b0) + b1) * 255, 3 * 31), 255);
            SetColor(palette[12..], RoundDivide((r0 + (2 * r1)) * 255, 3 * 31), RoundDivide((g0 + (2 * g1)) * 255, 3 * 63), RoundDivide((b0 + (2 * b1)) * 255, 3 * 31), 255);
        }
        else
        {
            // (RGB0 + RGB1) / 2, then transparent black
            SetColor(palette[8..], RoundDivide((r0 + r1) * 255, 2 * 31), RoundDivide((g0 + g1) * 255, 2 * 63), RoundDivide((b0 + b1) * 255, 2 * 31), 255);
            SetColor(palette[12..], 0, 0, 0, 0);
        }

        for (var i = 0; i < 16; i++)
        {
            var code = (int)(bits >> (2 * i)) & 3;
            palette.Slice(code * 4, 4).CopyTo(rgba[(i * 4)..]);
        }
    }

    private static void SetColor(Span<byte> destination, int r, int g, int b, int a)
    {
        destination[0] = (byte)r;
        destination[1] = (byte)g;
        destination[2] = (byte)b;
        destination[3] = (byte)a;
    }

    /// <summary>Rounds a non-negative fraction to the nearest integer, halves up.</summary>
    private static int RoundDivide(int numerator, int denominator) => ((2 * numerator) + denominator) / (2 * denominator);

    /// <summary>Rounds a fraction of an odd denominator (never a half) to the nearest integer.</summary>
    private static int RoundDivideSigned(int numerator, int denominator) => numerator >= 0 ? RoundDivide(numerator, denominator) : -RoundDivide(-numerator, denominator);

    /// <summary>Reads the 48 bits of the indices of an RGTC channel, after its two endpoints.</summary>
    private static ulong ReadIndices48(ReadOnlySpan<byte> block) => BinaryPrimitives.ReadUInt64LittleEndian(block) >> 16;

    /// <summary>Equation 2 of section 20.1.</summary>
    private static int Interpolate(int e0, int e1, int weight) => (((64 - weight) * e0) + (weight * e1) + 32) >> 6;

    private static int SignExtend(int value, int bits) => (value << (32 - bits)) >> (32 - bits);

    /// <summary>The unquantization of an endpoint of the unsigned format (section 20.2).</summary>
    public static int UnquantizeUnsigned(int value, int bits)
    {
        if (bits >= 15)
            return value;

        if (value == 0)
            return 0;

        if (value == (1 << bits) - 1)
            return 0xFFFF;

        return ((value << 15) + 0x4000) >> (bits - 1);
    }

    /// <summary>The unquantization of an endpoint of the signed format (section 20.2).</summary>
    public static int UnquantizeSigned(int value, int bits)
    {
        if (bits >= 16)
            return value;

        var negative = value < 0;
        if (negative)
        {
            value = -value;
        }

        int result;
        if (value == 0)
        {
            result = 0;
        }
        else if (value >= (1 << (bits - 1)) - 1)
        {
            result = 0x7FFF;
        }
        else
        {
            result = ((value << 15) + 0x4000) >> (bits - 1);
        }

        return negative ? -result : result;
    }

    /// <summary>The final unquantization of the signed format: the magnitude scaled by 31/32, and the sign bit.</summary>
    private static ushort FinishSigned(int value) => value < 0 ? (ushort)((((-value) * 31) >> 5) | 0x8000) : (ushort)((value * 31) >> 5);

    /// <summary>The parameters of a BC7 mode (Table 109).</summary>
    internal readonly record struct Bc7Mode(int Subsets, int PartitionBits, int RotationBits, int IndexSelectionBits, int ColorBits, int AlphaBits, bool EndpointPBits, bool SharedPBits, int IndexBits, int SecondaryIndexBits);

    internal static readonly Bc7Mode[] Bc7Modes =
    [
        new(3, 4, 0, 0, 4, 0, EndpointPBits: true, SharedPBits: false, 3, 0),
        new(2, 6, 0, 0, 6, 0, EndpointPBits: false, SharedPBits: true, 3, 0),
        new(3, 6, 0, 0, 5, 0, EndpointPBits: false, SharedPBits: false, 2, 0),
        new(2, 6, 0, 0, 7, 0, EndpointPBits: true, SharedPBits: false, 2, 0),
        new(1, 0, 2, 1, 5, 6, EndpointPBits: false, SharedPBits: false, 2, 3),
        new(1, 0, 2, 0, 7, 8, EndpointPBits: false, SharedPBits: false, 2, 2),
        new(1, 0, 0, 0, 7, 7, EndpointPBits: true, SharedPBits: false, 4, 0),
        new(2, 6, 0, 0, 5, 5, EndpointPBits: true, SharedPBits: false, 2, 0),
    ];

    /// <summary>Reads the bits of a 128-bit block from the least significant one.</summary>
    [StructLayout(LayoutKind.Auto)]
    private ref struct BitReader
    {
        private ulong _low;
        private ulong _high;

        public BitReader(ReadOnlySpan<byte> block)
        {
            _low = BinaryPrimitives.ReadUInt64LittleEndian(block);
            _high = BinaryPrimitives.ReadUInt64LittleEndian(block[8..]);
        }

        public readonly ulong Peek(int count) => _low & ((1UL << count) - 1);

        public ulong Read(int count)
        {
            if (count == 0)
                return 0;

            var value = _low & ((1UL << count) - 1);
            _low = (_low >> count) | (_high << (64 - count));
            _high >>= count;
            return value;
        }
    }
}
