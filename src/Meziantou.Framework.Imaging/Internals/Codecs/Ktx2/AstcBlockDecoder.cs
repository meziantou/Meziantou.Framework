using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes one two-dimensional ASTC block of 128 bits (Khronos Data Format Specification 1.4, section 23) into RGBA texels.
/// </summary>
/// <remarks>
/// <para>
/// The procedure is the one of the specification, section by section: the block mode (23.10), the partitions and the color
/// endpoint modes (23.9, 23.11), the integer sequence encoding (23.12) of the endpoints and of the weights, their
/// unquantization (23.13, 23.16), the endpoint interpretation of the LDR and HDR modes (23.14), the weight infill (23.17),
/// the weight application (23.18), the dual-plane selector (23.19), the partition hash (23.20) and void-extent blocks
/// (23.22).
/// </para>
/// <para>
/// The specification decodes an illegal block (23.23), an HDR endpoint or an HDR void extent in an LDR operation mode to
/// the error color; the decoder rejects them instead, as malformed data (<see cref="InvalidImageContentException"/>): a
/// texture of valid blocks never shows the error color.
/// </para>
/// <para>
/// The results are those of the operation mode and the decode mode: the top 8 bits of the interpolation for an sRGB
/// format, the halves of decode_float16 for a linear LDR (UNORM) format, which KTX makes the default (section 5.7), or the
/// top 8 bits of decode_unorm8 when the texture says it can be decoded so, and the halves of decode_float16 for an HDR
/// (SFLOAT) format.
/// </para>
/// </remarks>
internal static class AstcBlockDecoder
{
    internal const int MaxWeights = 64;

    // The color endpoint ranges, the largest first (section 23.13): (trits, quints, bits)
    internal static readonly (int Trits, int Quints, int Bits)[] ColorRanges =
    [
        (0, 0, 8), (1, 0, 6), (0, 1, 5), (0, 0, 7), (1, 0, 5), (0, 1, 4), (0, 0, 6), (1, 0, 4), (0, 1, 3), (0, 0, 5),
        (1, 0, 3), (0, 1, 2), (0, 0, 4), (1, 0, 2), (0, 1, 1), (0, 0, 3), (1, 0, 1),
    ];

    /// <summary>
    /// Gets the operation mode and the decode mode of a format: sRGB, linear LDR (UNORM, decode_float16 unless
    /// <c>KTXastcDecodeMode</c> is <c>unorm8</c>) or HDR (SFLOAT).
    /// </summary>
    public static AstcProfile GetProfile(Ktx2VkFormat format, Ktx2OutputPrecision precision)
    {
        if (format.IsSrgb)
            return AstcProfile.Srgb;

        if (format.NumericType == Ktx2NumericType.Sfloat)
            return AstcProfile.Hdr;

        return precision == Ktx2OutputPrecision.Byte ? AstcProfile.LinearUnorm8 : AstcProfile.Linear;
    }

    /// <summary>Decodes a block.</summary>
    /// <param name="block">The 16 bytes of the block.</param>
    /// <param name="blockWidth">The width of the footprint.</param>
    /// <param name="blockHeight">The height of the footprint.</param>
    /// <param name="profile">The operation mode.</param>
    /// <param name="destination">The top-left texel of the block in a strip of RGBA texels: bytes (sRGB), or 16-bit values (UNORM16 or halves).</param>
    /// <param name="stride">The length of a row of the strip, in bytes.</param>
    /// <exception cref="InvalidImageContentException">The block is illegal, or uses HDR data in an LDR operation mode.</exception>
    public static void Decode(ReadOnlySpan<byte> block, int blockWidth, int blockHeight, AstcProfile profile, Span<byte> destination, int stride)
    {
        var bits = BinaryPrimitives.ReadUInt128LittleEndian(block);
        var blockMode = (int)Bits(bits, 0, 11);

        // Section 23.9.3: a void-extent block
        if ((blockMode & 0x1FF) == 0x1FC)
        {
            DecodeVoidExtent(bits, blockWidth, blockHeight, profile, destination, stride);
            return;
        }

        if (!TryDecodeBlockMode(blockMode, out var gridWidth, out var gridHeight, out var dualPlane, out var weightRange))
            throw Illegal("its block mode is reserved");

        if (gridWidth > blockWidth || gridHeight > blockHeight)
            throw Illegal("its weight grid is larger than its footprint");

        var weightCount = gridWidth * gridHeight * (dualPlane ? 2 : 1);
        if (weightCount > MaxWeights)
            throw Illegal("it has more than 64 weights");

        var weightBits = GetSequenceBits(weightCount, weightRange.Trits, weightRange.Quints, weightRange.Bits);
        if (weightBits is < 24 or > 96)
            throw Illegal("its weights take fewer than 24 bits or more than 96");

        var partitions = (int)Bits(bits, 11, 2) + 1;
        if (dualPlane && partitions == 4)
            throw Illegal("it has two weight planes and four partitions");

        // Sections 23.9.1, 23.9.2 and 23.11: the color endpoint modes
        Span<int> modes = stackalloc int[4];
        int colorStart;
        var extraCemBits = 0;
        var partitionIndex = 0;
        if (partitions == 1)
        {
            modes[0] = (int)Bits(bits, 13, 4);
            colorStart = 17;
        }
        else
        {
            partitionIndex = (int)Bits(bits, 13, 10);
            var selector = (int)Bits(bits, 23, 2);
            colorStart = 29;
            if (selector == 0)
            {
                var mode = (int)Bits(bits, 25, 4);
                for (var i = 0; i < partitions; i++)
                {
                    modes[i] = mode;
                }
            }
            else
            {
                extraCemBits = (3 * partitions) - 4;
                var high = (int)Bits(bits, 128 - weightBits - extraCemBits, extraCemBits);
                var combined = (high << 6) | (int)Bits(bits, 23, 6);
                var baseClass = selector - 1;
                for (var i = 0; i < partitions; i++)
                {
                    var classBit = (combined >> (2 + i)) & 1;
                    var low = (combined >> (2 + partitions + (2 * i))) & 3;
                    modes[i] = ((baseClass + classBit) << 2) | low;
                }

            }
        }

        var configBits = partitions == 1 ? 17 : extraCemBits == 0 ? 29 : 25 + (3 * partitions);
        if (dualPlane)
        {
            configBits += 2;
        }

        var colorValueCount = 0;
        for (var i = 0; i < partitions; i++)
        {
            colorValueCount += ((modes[i] >> 2) + 1) * 2;
        }

        if (colorValueCount > 18)
            throw Illegal("its color endpoints need more than 18 integers");

        var remainingBits = 128 - configBits - weightBits;
        if (remainingBits < ((13 * colorValueCount) + 4) / 5)
            throw Illegal("too few bits remain for its color endpoints");

        var colorRange = SelectColorRange(colorValueCount, remainingBits);

        // The dual-plane component selector is below the weights and the extra CEM bits
        var ccs = dualPlane ? (int)Bits(bits, 128 - weightBits - extraCemBits - 2, 2) : -1;

        // Section 23.12 and 23.13: the color endpoint integers, unquantized to 0..255
        Span<int> colorValues = stackalloc int[18];
        DecodeSequence(bits, colorStart, colorValueCount, colorRange.Trits, colorRange.Quints, colorRange.Bits, colorValues);
        for (var i = 0; i < colorValueCount; i++)
        {
            colorValues[i] = UnquantizeColor(colorValues[i], colorRange.Trits, colorRange.Quints, colorRange.Bits);
        }

        // Section 23.14: two endpoints per partition, and whether they are HDR values
        Span<int> endpoints = stackalloc int[4 * 8];
        Span<bool> hdrColor = stackalloc bool[4];
        Span<bool> hdrAlpha = stackalloc bool[4];
        var offset = 0;
        for (var i = 0; i < partitions; i++)
        {
            var count = ((modes[i] >> 2) + 1) * 2;
            DecodeEndpoints(modes[i], colorValues.Slice(offset, count), endpoints.Slice(i * 8, 8), out hdrColor[i], out hdrAlpha[i]);
            if (profile != AstcProfile.Hdr && (hdrColor[i] || hdrAlpha[i]))
                throw Illegal(string.Create(CultureInfo.InvariantCulture, $"it uses the HDR color endpoint mode {modes[i]} in an LDR format"));

            offset += count;
        }

        // Sections 23.15 and 23.16: the weights, read from the top of the block downward, unquantized to 0..64
        Span<int> weights = stackalloc int[MaxWeights + 2];
        weights.Clear();
        DecodeSequence(ReverseBits(bits), 0, weightCount, weightRange.Trits, weightRange.Quints, weightRange.Bits, weights);
        for (var i = 0; i < weightCount; i++)
        {
            weights[i] = UnquantizeWeight(weights[i], weightRange.Trits, weightRange.Quints, weightRange.Bits);
        }

        var smallBlock = blockWidth * blockHeight < 31;
        var ds = GetInfillScale(blockWidth);
        var dt = GetInfillScale(blockHeight);
        Span<int> texel = stackalloc int[4];
        Span<int> effective = stackalloc int[2];
        for (var y = 0; y < blockHeight; y++)
        {
            for (var x = 0; x < blockWidth; x++)
            {
                var partition = partitions == 1 ? 0 : SelectPartition(partitionIndex, x, y, 0, partitions, smallBlock);
                var endpoint = endpoints.Slice(partition * 8, 8);

                var (v0, w00, w01, w10, w11) = GetInfill(x, y, ds, dt, gridWidth, gridHeight);
                var planes = dualPlane ? 2 : 1;
                for (var plane = 0; plane < planes; plane++)
                {
                    var p00 = Weight(weights, v0, plane, planes, weightCount);
                    var p01 = Weight(weights, v0 + 1, plane, planes, weightCount);
                    var p10 = Weight(weights, v0 + gridWidth, plane, planes, weightCount);
                    var p11 = Weight(weights, v0 + gridWidth + 1, plane, planes, weightCount);
                    effective[plane] = ((p00 * w00) + (p01 * w01) + (p10 * w10) + (p11 * w11) + 8) >> 4;
                }

                // Sections 23.18 and 23.19: each component interpolated with the weight of its plane
                for (var c = 0; c < 4; c++)
                {
                    var weight = c == ccs ? effective[1] : effective[0];
                    var hdr = c == 3 ? hdrAlpha[partition] : hdrColor[partition];
                    texel[c] = Interpolate(endpoint[c], endpoint[4 + c], weight, hdr, profile);
                }

                WriteTexel(texel, profile, destination, (y * stride) + (x * (IsByte(profile) ? 4 : 8)));
            }
        }
    }

    /// <summary>The scale factors of the weight infill of a footprint dimension (section 23.17): Ds or Dt.</summary>
    internal static int GetInfillScale(int blockSize) => (1024 + (blockSize / 2)) / (blockSize - 1);

    /// <summary>
    /// The bilinear infill of the weight grid (section 23.17): the index of the top-left grid weight of a texel and the
    /// four factors (in 1/16) of the grid weights v0, v0 + 1, v0 + gridWidth and v0 + gridWidth + 1.
    /// </summary>
    internal static (int V0, int W00, int W01, int W10, int W11) GetInfill(int x, int y, int ds, int dt, int gridWidth, int gridHeight)
    {
        var gs = ((ds * x * (gridWidth - 1)) + 32) >> 6;
        var gt = ((dt * y * (gridHeight - 1)) + 32) >> 6;
        var (js, fs) = (gs >> 4, gs & 0xF);
        var (jt, ft) = (gt >> 4, gt & 0xF);
        var w11 = ((fs * ft) + 8) >> 4;
        var w10 = ft - w11;
        var w01 = fs - w11;
        var w00 = 16 - fs - ft + w11;
        return (js + (jt * gridWidth), w00, w01, w10, w11);
    }

    private static int Weight(ReadOnlySpan<int> weights, int index, int plane, int planes, int count)
    {
        // A grid position outside the grid only happens with a fraction of 0: its weight does not count
        var position = (index * planes) + plane;
        return position < count ? weights[position] : 0;
    }

    /// <summary>Interpolates a component (section 23.18) and returns its result in the operation mode: 8 bits, UNORM16 or half bits.</summary>
    private static int Interpolate(int c0, int c1, int weight, bool hdr, AstcProfile profile)
    {
        if (hdr)
        {
            // Section 23.18.2: 12-bit pseudo-logarithmic values
            var c = ((c0 << 4) * (64 - weight) + ((c1 << 4) * weight) + 32) / 64;
            var e = (c & 0xF800) >> 11;
            var m = c & 0x7FF;
            var mt = m < 512 ? 3 * m : m >= 1536 ? (5 * m) - 2048 : (4 * m) - 512;
            var cf = (e << 10) + (mt >> 3);
            return cf >= 0x7C00 ? 0x7BFF : cf;
        }

        if (profile == AstcProfile.Srgb)
        {
            var c = (((c0 << 8) | 0x80) * (64 - weight) + (((c1 << 8) | 0x80) * weight) + 32) / 64;
            return c >> 8;
        }

        var unorm = (((c0 << 8) | c0) * (64 - weight) + (((c1 << 8) | c1) * weight) + 32) / 64;
        return profile == AstcProfile.LinearUnorm8 ? unorm >> 8 : UnormToHalf(unorm);
    }

    /// <summary>The decode_float16 result of a UNORM16 value: 1.0 for 65535, otherwise C / 65536 rounded toward zero.</summary>
    private static int UnormToHalf(int c)
    {
        if (c == 65535)
            return 0x3C00;

        if (c == 0)
            return 0;

        // c × 2^-16: a normal half from 2^-14 (c >= 4), its 10 mantissa bits below the leading bit of c
        var leading = 31 - System.Numerics.BitOperations.LeadingZeroCount((uint)c);
        var exponent = leading - 16 + 15;
        if (exponent <= 0)
        {
            // A subnormal half is a multiple of 2^-24: c × 2^-16 = (c × 256) × 2^-24
            return c << 8;
        }

        var mantissa = leading >= 10 ? (c >> (leading - 10)) & 0x3FF : (c << (10 - leading)) & 0x3FF;
        return (exponent << 10) | mantissa;
    }

    private static bool IsByte(AstcProfile profile) => profile is AstcProfile.Srgb or AstcProfile.LinearUnorm8;

    private static void WriteTexel(ReadOnlySpan<int> texel, AstcProfile profile, Span<byte> destination, int offset)
    {
        if (IsByte(profile))
        {
            for (var c = 0; c < 4; c++)
            {
                destination[offset + c] = (byte)texel[c];
            }

            return;
        }

        for (var c = 0; c < 4; c++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[(offset + (2 * c))..], (ushort)texel[c]);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Block mode (section 23.10)
    // -----------------------------------------------------------------------------------------------------------------

    internal static bool TryDecodeBlockMode(int mode, out int width, out int height, out bool dualPlane, out (int Trits, int Quints, int Bits) range)
    {
        width = 0;
        height = 0;
        dualPlane = false;
        range = default;
        int rho;
        var highPrecision = false;
        if ((mode & 3) != 0)
        {
            rho = ((mode & 3) << 1) | ((mode >> 4) & 1);
            highPrecision = ((mode >> 9) & 1) != 0;
            dualPlane = ((mode >> 10) & 1) != 0;
            var a = (mode >> 5) & 3;
            var b = (mode >> 7) & 3;
            switch ((mode >> 2) & 3)
            {
                case 0:
                    (width, height) = (b + 4, a + 2);
                    break;
                case 1:
                    (width, height) = (b + 8, a + 2);
                    break;
                case 2:
                    (width, height) = (a + 2, b + 8);
                    break;
                default:
                    if ((mode & 0x100) == 0)
                    {
                        (width, height) = (a + 2, ((mode >> 7) & 1) + 6);
                    }
                    else
                    {
                        (width, height) = (((mode >> 7) & 1) + 2, a + 2);
                    }

                    break;
            }
        }
        else
        {
            if ((mode & 0xF) == 0)
                return false;

            rho = ((mode >> 1) & 6) | ((mode >> 4) & 1);
            var a = (mode >> 5) & 3;
            switch ((mode >> 7) & 3)
            {
                case 0:
                    highPrecision = ((mode >> 9) & 1) != 0;
                    dualPlane = ((mode >> 10) & 1) != 0;
                    (width, height) = (12, a + 2);
                    break;
                case 1:
                    highPrecision = ((mode >> 9) & 1) != 0;
                    dualPlane = ((mode >> 10) & 1) != 0;
                    (width, height) = (a + 2, 12);
                    break;
                case 3:
                    highPrecision = ((mode >> 9) & 1) != 0;
                    dualPlane = ((mode >> 10) & 1) != 0;
                    // Bits 8..6 of 111 are a void extent (handled before) or reserved
                    if ((mode & 0x40) != 0)
                        return false;

                    (width, height) = (mode & 0x20) == 0 ? (6, 10) : (10, 6);

                    break;
                default:
                    (width, height) = (a + 6, ((mode >> 9) & 3) + 6);
                    break;
            }
        }

        // Table 160: the weight ranges
        range = (rho, highPrecision) switch
        {
            (2, false) => (0, 0, 1),
            (3, false) => (1, 0, 0),
            (4, false) => (0, 0, 2),
            (5, false) => (0, 1, 0),
            (6, false) => (1, 0, 1),
            (7, false) => (0, 0, 3),
            (2, true) => (0, 1, 1),
            (3, true) => (1, 0, 2),
            (4, true) => (0, 0, 4),
            (5, true) => (0, 1, 2),
            (6, true) => (1, 0, 3),
            (7, true) => (0, 0, 5),
            _ => (-1, -1, -1),
        };
        return range.Trits >= 0;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Integer sequence encoding (section 23.12) and unquantization (sections 23.13, 23.16)
    // -----------------------------------------------------------------------------------------------------------------

    internal static int GetSequenceBits(int count, int trits, int quints, int bits)
        => (((count * 8 * trits) + 4) / 5) + (((count * 7 * quints) + 2) / 3) + (count * bits);

    private static (int Trits, int Quints, int Bits) SelectColorRange(int count, int remainingBits)
    {
        foreach (var range in ColorRanges)
        {
            if (GetSequenceBits(count, range.Trits, range.Quints, range.Bits) <= remainingBits)
                return range;
        }

        throw Illegal("too few bits remain for its color endpoints");
    }

    internal static void DecodeSequence(UInt128 bits, int start, int count, int trits, int quints, int bitCount, Span<int> values)
    {
        var position = start;
        var end = start + GetSequenceBits(count, trits, quints, bitCount);
        if (trits != 0)
        {
            Span<int> t = stackalloc int[5];
            for (var i = 0; i < count; i += 5)
            {
                // Table 169: m0, T1..0, m1, T3..2, m2, T4, m3, T6..5, m4, T7
                var m0 = Read(bits, ref position, end, bitCount);
                var packed = Read(bits, ref position, end, 2);
                var m1 = Read(bits, ref position, end, bitCount);
                packed |= Read(bits, ref position, end, 2) << 2;
                var m2 = Read(bits, ref position, end, bitCount);
                packed |= Read(bits, ref position, end, 1) << 4;
                var m3 = Read(bits, ref position, end, bitCount);
                packed |= Read(bits, ref position, end, 2) << 5;
                var m4 = Read(bits, ref position, end, bitCount);
                packed |= Read(bits, ref position, end, 1) << 7;
                DecodeTrits(packed, t);
                int[] m = [m0, m1, m2, m3, m4];
                for (var j = 0; j < 5 && i + j < count; j++)
                {
                    values[i + j] = (t[j] << bitCount) | m[j];
                }
            }

            return;
        }

        if (quints != 0)
        {
            Span<int> q = stackalloc int[3];
            for (var i = 0; i < count; i += 3)
            {
                // Table 170: m0, Q2..0, m1, Q4..3, m2, Q6..5
                var m0 = Read(bits, ref position, end, bitCount);
                var packed = Read(bits, ref position, end, 3);
                var m1 = Read(bits, ref position, end, bitCount);
                packed |= Read(bits, ref position, end, 2) << 3;
                var m2 = Read(bits, ref position, end, bitCount);
                packed |= Read(bits, ref position, end, 2) << 5;
                DecodeQuints(packed, q);
                int[] m = [m0, m1, m2];
                for (var j = 0; j < 3 && i + j < count; j++)
                {
                    values[i + j] = (q[j] << bitCount) | m[j];
                }
            }

            return;
        }

        for (var i = 0; i < count; i++)
        {
            values[i] = Read(bits, ref position, end, bitCount);
        }
    }

    /// <summary>Reads bits of a sequence; the bits past the end of the sequence (a last block of trits or quints that is not full) are 0 (section 23.12).</summary>
    private static int Read(UInt128 bits, ref int position, int end, int count)
    {
        var available = Math.Min(Math.Min(count, end - position), 128 - position);
        var value = available <= 0 ? 0 : (int)Bits(bits, position, available);
        position += count;
        return value;
    }

    internal static void DecodeTrits(int packed, Span<int> t)
    {
        int c;
        if (((packed >> 2) & 7) == 7)
        {
            c = (((packed >> 5) & 7) << 2) | (packed & 3);
            t[4] = 2;
            t[3] = 2;
        }
        else
        {
            c = packed & 0x1F;
            if (((packed >> 5) & 3) == 3)
            {
                t[4] = 2;
                t[3] = (packed >> 7) & 1;
            }
            else
            {
                t[4] = (packed >> 7) & 1;
                t[3] = (packed >> 5) & 3;
            }
        }

        if ((c & 3) == 3)
        {
            t[2] = 2;
            t[1] = (c >> 4) & 1;
            t[0] = (((c >> 3) & 1) << 1) | (((c >> 2) & 1) & ~((c >> 3) & 1) & 1);
        }
        else if (((c >> 2) & 3) == 3)
        {
            t[2] = 2;
            t[1] = 2;
            t[0] = c & 3;
        }
        else
        {
            t[2] = (c >> 4) & 1;
            t[1] = (c >> 2) & 3;
            t[0] = (((c >> 1) & 1) << 1) | ((c & 1) & ~((c >> 1) & 1) & 1);
        }
    }

    internal static void DecodeQuints(int packed, Span<int> q)
    {
        if (((packed >> 1) & 3) == 3 && ((packed >> 5) & 3) == 0)
        {
            var q0bit = packed & 1;
            q[2] = (q0bit << 2) | ((((packed >> 4) & 1) & ~q0bit & 1) << 1) | (((packed >> 3) & 1) & ~q0bit & 1);
            q[1] = 4;
            q[0] = 4;
            return;
        }

        int c;
        if (((packed >> 1) & 3) == 3)
        {
            q[2] = 4;
            c = (((packed >> 3) & 3) << 3) | ((~(packed >> 5) & 3) << 1) | (packed & 1);
        }
        else
        {
            q[2] = (packed >> 5) & 3;
            c = packed & 0x1F;
        }

        if ((c & 7) == 5)
        {
            q[1] = 4;
            q[0] = (c >> 3) & 3;
        }
        else
        {
            q[1] = (c >> 3) & 3;
            q[0] = c & 7;
        }
    }

    /// <summary>Unquantizes a color endpoint integer to 0..255 (section 23.13); UASTC uses it too (section 25.7.2.4).</summary>
    public static int UnquantizeColor(int value, int trits, int quints, int bitCount)
    {
        if (trits == 0 && quints == 0)
            return Replicate(value, bitCount, 8);

        // Table 172: A, B and C from the low bits, D the trit or quint
        var d = value >> bitCount;
        var bitsValue = value & ((1 << bitCount) - 1);
        var a = (bitsValue & 1) != 0 ? 0x1FF : 0;
        var (b, c) = (trits, bitCount) switch
        {
            (1, 1) => (0, 204),
            (1, 2) => ((Bit(bitsValue, 1) << 8) | (Bit(bitsValue, 1) << 4) | (Bit(bitsValue, 1) << 2) | (Bit(bitsValue, 1) << 1), 93),
            (1, 3) => ((Bit(bitsValue, 2) << 8) | (Bit(bitsValue, 1) << 7) | (Bit(bitsValue, 2) << 3) | (Bit(bitsValue, 1) << 2) | (Bit(bitsValue, 2) << 1) | Bit(bitsValue, 1), 44),
            (1, 4) => ((Bit(bitsValue, 3) << 8) | (Bit(bitsValue, 2) << 7) | (Bit(bitsValue, 1) << 6) | (Bit(bitsValue, 3) << 2) | (Bit(bitsValue, 2) << 1) | Bit(bitsValue, 1), 22),
            (1, 5) => ((Bit(bitsValue, 4) << 8) | (Bit(bitsValue, 3) << 7) | (Bit(bitsValue, 2) << 6) | (Bit(bitsValue, 1) << 5) | (Bit(bitsValue, 4) << 1) | Bit(bitsValue, 3), 11),
            (1, 6) => ((Bit(bitsValue, 5) << 8) | (Bit(bitsValue, 4) << 7) | (Bit(bitsValue, 3) << 6) | (Bit(bitsValue, 2) << 5) | (Bit(bitsValue, 1) << 4) | Bit(bitsValue, 5), 5),
            (0, 1) => (0, 113),
            (0, 2) => ((Bit(bitsValue, 1) << 8) | (Bit(bitsValue, 1) << 3) | (Bit(bitsValue, 1) << 2), 54),
            (0, 3) => ((Bit(bitsValue, 2) << 8) | (Bit(bitsValue, 1) << 7) | (Bit(bitsValue, 2) << 2) | (Bit(bitsValue, 1) << 1) | Bit(bitsValue, 2), 26),
            (0, 4) => ((Bit(bitsValue, 3) << 8) | (Bit(bitsValue, 2) << 7) | (Bit(bitsValue, 1) << 6) | (Bit(bitsValue, 3) << 1) | Bit(bitsValue, 2), 13),
            _ => ((Bit(bitsValue, 4) << 8) | (Bit(bitsValue, 3) << 7) | (Bit(bitsValue, 2) << 6) | (Bit(bitsValue, 1) << 5) | Bit(bitsValue, 4), 6),
        };
        var unquantized = (d * c) + b;
        unquantized ^= a;
        return (a & 0x80) | (unquantized >> 2);
    }

    internal static int UnquantizeWeight(int value, int trits, int quints, int bitCount)
    {
        int unquantized;
        if (trits == 0 && quints == 0)
        {
            unquantized = Replicate(value, bitCount, 6);
        }
        else if (bitCount == 0)
        {
            // Table 181
            unquantized = trits != 0 ? value switch { 0 => 0, 1 => 32, _ => 63 } : value switch { 0 => 0, 1 => 16, 2 => 32, 3 => 47, _ => 63 };
        }
        else
        {
            // Table 182
            var d = value >> bitCount;
            var bitsValue = value & ((1 << bitCount) - 1);
            var a = (bitsValue & 1) != 0 ? 0x7F : 0;
                var (b, c) = (trits, bitCount) switch
            {
                (1, 1) => (0, 50),
                (1, 2) => ((Bit(bitsValue, 1) << 6) | (Bit(bitsValue, 1) << 2) | Bit(bitsValue, 1), 23),
                (1, 3) => ((Bit(bitsValue, 2) << 6) | (Bit(bitsValue, 1) << 5) | (Bit(bitsValue, 2) << 1) | Bit(bitsValue, 1), 11),
                (0, 1) => (0, 28),
                _ => ((Bit(bitsValue, 1) << 6) | (Bit(bitsValue, 1) << 1), 13),
            };
            unquantized = (d * c) + b;
            unquantized ^= a;
            unquantized = (a & 0x20) | (unquantized >> 2);
        }

        return unquantized > 32 ? unquantized + 1 : unquantized;
    }

    private static int Bit(int value, int index) => (value >> index) & 1;

    private static int Replicate(int value, int bitCount, int targetBits)
    {
        var result = 0;
        var filled = 0;
        while (filled < targetBits)
        {
            var shift = targetBits - filled - bitCount;
            result |= shift >= 0 ? value << shift : value >> -shift;
            filled += bitCount;
        }

        return result & ((1 << targetBits) - 1);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Endpoint interpretation (section 23.14)
    // -----------------------------------------------------------------------------------------------------------------

    internal static void DecodeEndpoints(int mode, Span<int> v, Span<int> e, out bool hdrColor, out bool hdrAlpha)
    {
        hdrColor = false;
        hdrAlpha = false;
        switch (mode)
        {
            case 0:
                Set(e, 0, v[0], v[0], v[0], 0xFF);
                Set(e, 1, v[1], v[1], v[1], 0xFF);
                break;
            case 1:
            {
                var l0 = (v[0] >> 2) | (v[1] & 0xC0);
                var l1 = Math.Min(l0 + (v[1] & 0x3F), 0xFF);
                Set(e, 0, l0, l0, l0, 0xFF);
                Set(e, 1, l1, l1, l1, 0xFF);
                break;
            }

            case 2:
            {
                hdrColor = true;
                hdrAlpha = true;
                int y0, y1;
                if (v[1] >= v[0])
                {
                    (y0, y1) = (v[0] << 4, v[1] << 4);
                }
                else
                {
                    (y0, y1) = ((v[1] << 4) + 8, (v[0] << 4) - 8);
                }

                Set(e, 0, y0, y0, y0, 0x780);
                Set(e, 1, y1, y1, y1, 0x780);
                break;
            }

            case 3:
            {
                hdrColor = true;
                hdrAlpha = true;
                int y0, d;
                if ((v[0] & 0x80) != 0)
                {
                    y0 = ((v[1] & 0xE0) << 4) | ((v[0] & 0x7F) << 2);
                    d = (v[1] & 0x1F) << 2;
                }
                else
                {
                    y0 = ((v[1] & 0xF0) << 4) | ((v[0] & 0x7F) << 1);
                    d = (v[1] & 0x0F) << 1;
                }

                var y1 = Math.Min(y0 + d, 0xFFF);
                Set(e, 0, y0, y0, y0, 0x780);
                Set(e, 1, y1, y1, y1, 0x780);
                break;
            }

            case 4:
                Set(e, 0, v[0], v[0], v[0], v[2]);
                Set(e, 1, v[1], v[1], v[1], v[3]);
                break;
            case 5:
            {
                var (v0, v1) = BitTransferSigned(v[1], v[0]);
                var (v2, v3) = BitTransferSigned(v[3], v[2]);
                Set(e, 0, v0, v0, v0, v2);
                Set(e, 1, v0 + v1, v0 + v1, v0 + v1, v2 + v3);
                ClampUnorm8(e);
                break;
            }

            case 6:
                Set(e, 0, (v[0] * v[3]) >> 8, (v[1] * v[3]) >> 8, (v[2] * v[3]) >> 8, 0xFF);
                Set(e, 1, v[0], v[1], v[2], 0xFF);
                break;
            case 7:
                hdrColor = true;
                hdrAlpha = true;
                DecodeHdrMode7(v, e);
                break;
            case 8:
            case 12:
            {
                var a0 = mode == 12 ? v[6] : 0xFF;
                var a1 = mode == 12 ? v[7] : 0xFF;
                if (v[1] + v[3] + v[5] >= v[0] + v[2] + v[4])
                {
                    Set(e, 0, v[0], v[2], v[4], a0);
                    Set(e, 1, v[1], v[3], v[5], a1);
                }
                else
                {
                    BlueContract(e, 0, v[1], v[3], v[5], a1);
                    BlueContract(e, 1, v[0], v[2], v[4], a0);
                }

                break;
            }

            case 9:
            case 13:
            {
                var (v0, v1) = BitTransferSigned(v[1], v[0]);
                var (v2, v3) = BitTransferSigned(v[3], v[2]);
                var (v4, v5) = BitTransferSigned(v[5], v[4]);
                int a0 = 0xFF, a1 = 0xFF;
                if (mode == 13)
                {
                    (a0, var a1Offset) = BitTransferSigned(v[7], v[6]);
                    a1 = a0 + a1Offset;
                }

                if (v1 + v3 + v5 >= 0)
                {
                    Set(e, 0, v0, v2, v4, a0);
                    Set(e, 1, v0 + v1, v2 + v3, v4 + v5, a1);
                }
                else
                {
                    BlueContract(e, 0, v0 + v1, v2 + v3, v4 + v5, a1);
                    BlueContract(e, 1, v0, v2, v4, a0);
                }

                ClampUnorm8(e);
                break;
            }

            case 10:
                Set(e, 0, (v[0] * v[3]) >> 8, (v[1] * v[3]) >> 8, (v[2] * v[3]) >> 8, v[4]);
                Set(e, 1, v[0], v[1], v[2], v[5]);
                break;
            case 11:
                hdrColor = true;
                hdrAlpha = true;
                DecodeHdrMode11(v, e);
                break;
            case 14:
                hdrColor = true;
                DecodeHdrMode11(v, e);
                e[3] = v[6];
                e[7] = v[7];
                break;
            default:
                hdrColor = true;
                hdrAlpha = true;
                DecodeHdrMode11(v, e);
                DecodeHdrAlpha(v[6], v[7], e);
                break;
        }
    }

    private static void Set(Span<int> e, int endpoint, int r, int g, int b, int a)
    {
        e[endpoint * 4] = r;
        e[(endpoint * 4) + 1] = g;
        e[(endpoint * 4) + 2] = b;
        e[(endpoint * 4) + 3] = a;
    }

    private static void BlueContract(Span<int> e, int endpoint, int r, int g, int b, int a) => Set(e, endpoint, (r + b) >> 1, (g + b) >> 1, b, a);

    private static void ClampUnorm8(Span<int> e)
    {
        for (var i = 0; i < 8; i++)
        {
            e[i] = Math.Clamp(e[i], 0, 255);
        }
    }

    /// <summary>bit_transfer_signed(a, b): returns (b, a) once the top bit of a moved to b and a became signed.</summary>
    private static (int B, int A) BitTransferSigned(int a, int b)
    {
        b >>= 1;
        b |= a & 0x80;
        a >>= 1;
        a &= 0x3F;
        if ((a & 0x20) != 0)
        {
            a -= 0x40;
        }

        return (b, a);
    }

    private static void DecodeHdrMode7(ReadOnlySpan<int> v, Span<int> e)
    {
        int majcomp;
        int mode;
        var modeval = ((v[0] & 0xC0) >> 6) | ((v[1] & 0x80) >> 5) | ((v[2] & 0x80) >> 4);
        if ((modeval & 0xC) != 0xC)
        {
            majcomp = modeval >> 2;
            mode = modeval & 3;
        }
        else if (modeval != 0xF)
        {
            majcomp = modeval & 3;
            mode = 4;
        }
        else
        {
            majcomp = 0;
            mode = 5;
        }

        var red = v[0] & 0x3F;
        var green = v[1] & 0x1F;
        var blue = v[2] & 0x1F;
        var scale = v[3] & 0x1F;
        var x0 = (v[1] >> 6) & 1;
        var x1 = (v[1] >> 5) & 1;
        var x2 = (v[2] >> 6) & 1;
        var x3 = (v[2] >> 5) & 1;
        var x4 = (v[3] >> 7) & 1;
        var x5 = (v[3] >> 6) & 1;
        var x6 = (v[3] >> 5) & 1;
        var ohm = 1 << mode;
        if ((ohm & 0x30) != 0) green |= x0 << 6;
        if ((ohm & 0x3A) != 0) green |= x1 << 5;
        if ((ohm & 0x30) != 0) blue |= x2 << 6;
        if ((ohm & 0x3A) != 0) blue |= x3 << 5;
        if ((ohm & 0x3D) != 0) scale |= x6 << 5;
        if ((ohm & 0x2D) != 0) scale |= x5 << 6;
        if ((ohm & 0x04) != 0) scale |= x4 << 7;
        if ((ohm & 0x3B) != 0) red |= x4 << 6;
        if ((ohm & 0x04) != 0) red |= x3 << 6;
        if ((ohm & 0x10) != 0) red |= x5 << 7;
        if ((ohm & 0x0F) != 0) red |= x2 << 7;
        if ((ohm & 0x05) != 0) red |= x1 << 8;
        if ((ohm & 0x0A) != 0) red |= x0 << 8;
        if ((ohm & 0x05) != 0) red |= x0 << 9;
        if ((ohm & 0x02) != 0) red |= x6 << 9;
        if ((ohm & 0x01) != 0) red |= x3 << 10;
        if ((ohm & 0x02) != 0) red |= x5 << 10;

        ReadOnlySpan<int> shifts = [1, 1, 2, 3, 4, 5];
        var shamt = shifts[mode];
        red <<= shamt;
        green <<= shamt;
        blue <<= shamt;
        scale <<= shamt;
        if (mode != 5)
        {
            green = red - green;
            blue = red - blue;
        }

        if (majcomp == 1)
        {
            (red, green) = (green, red);
        }
        else if (majcomp == 2)
        {
            (red, blue) = (blue, red);
        }

        Set(e, 1, Math.Clamp(red, 0, 0xFFF), Math.Clamp(green, 0, 0xFFF), Math.Clamp(blue, 0, 0xFFF), 0x780);
        Set(e, 0, Math.Clamp(red - scale, 0, 0xFFF), Math.Clamp(green - scale, 0, 0xFFF), Math.Clamp(blue - scale, 0, 0xFFF), 0x780);
    }

    private static void DecodeHdrMode11(ReadOnlySpan<int> v, Span<int> e)
    {
        var majcomp = ((v[4] & 0x80) >> 7) | ((v[5] & 0x80) >> 6);
        if (majcomp == 3)
        {
            Set(e, 0, v[0] << 4, v[2] << 4, (v[4] & 0x7F) << 5, 0x780);
            Set(e, 1, v[1] << 4, v[3] << 4, (v[5] & 0x7F) << 5, 0x780);
            return;
        }

        var mode = ((v[1] & 0x80) >> 7) | ((v[2] & 0x80) >> 6) | ((v[3] & 0x80) >> 5);
        var va = v[0] | ((v[1] & 0x40) << 2);
        var vb0 = v[2] & 0x3F;
        var vb1 = v[3] & 0x3F;
        var vc = v[1] & 0x3F;
        ReadOnlySpan<int> dbits = [7, 6, 7, 6, 5, 6, 5, 6];
        var vd0 = SignExtend(v[4] & 0x7F, dbits[mode]);
        var vd1 = SignExtend(v[5] & 0x7F, dbits[mode]);
        var x0 = (v[2] >> 6) & 1;
        var x1 = (v[3] >> 6) & 1;
        var x2 = (v[4] >> 6) & 1;
        var x3 = (v[5] >> 6) & 1;
        var x4 = (v[4] >> 5) & 1;
        var x5 = (v[5] >> 5) & 1;
        var ohm = 1 << mode;
        if ((ohm & 0xA4) != 0) va |= x0 << 9;
        if ((ohm & 0x08) != 0) va |= x2 << 9;
        if ((ohm & 0x50) != 0) va |= x4 << 9;
        if ((ohm & 0x50) != 0) va |= x5 << 10;
        if ((ohm & 0xA0) != 0) va |= x1 << 10;
        if ((ohm & 0xC0) != 0) va |= x2 << 11;
        if ((ohm & 0x04) != 0) vc |= x1 << 6;
        if ((ohm & 0xE8) != 0) vc |= x3 << 6;
        if ((ohm & 0x20) != 0) vc |= x2 << 7;
        if ((ohm & 0x5B) != 0) vb0 |= x0 << 6;
        if ((ohm & 0x5B) != 0) vb1 |= x1 << 6;
        if ((ohm & 0x12) != 0) vb0 |= x2 << 7;
        if ((ohm & 0x12) != 0) vb1 |= x3 << 7;

        var shamt = (mode >> 1) ^ 3;
        va <<= shamt;
        vb0 <<= shamt;
        vb1 <<= shamt;
        vc <<= shamt;
        vd0 <<= shamt;
        vd1 <<= shamt;
        int r1 = Math.Clamp(va, 0, 0xFFF), g1 = Math.Clamp(va - vb0, 0, 0xFFF), b1 = Math.Clamp(va - vb1, 0, 0xFFF);
        int r0 = Math.Clamp(va - vc, 0, 0xFFF), g0 = Math.Clamp(va - vb0 - vc - vd0, 0, 0xFFF), b0 = Math.Clamp(va - vb1 - vc - vd1, 0, 0xFFF);
        if (majcomp == 1)
        {
            (r0, g0) = (g0, r0);
            (r1, g1) = (g1, r1);
        }
        else if (majcomp == 2)
        {
            (r0, b0) = (b0, r0);
            (r1, b1) = (b1, r1);
        }

        Set(e, 0, r0, g0, b0, 0x780);
        Set(e, 1, r1, g1, b1, 0x780);
    }

    private static void DecodeHdrAlpha(int v6, int v7, Span<int> e)
    {
        var mode = ((v6 >> 7) & 1) | ((v7 >> 6) & 2);
        v6 &= 0x7F;
        v7 &= 0x7F;
        if (mode == 3)
        {
            e[3] = v6 << 5;
            e[7] = v7 << 5;
            return;
        }

        v6 |= (v7 << (mode + 1)) & 0x780;
        v7 &= 0x3F >> mode;
        v7 ^= 0x20 >> mode;
        v7 -= 0x20 >> mode;
        v6 <<= 4 - mode;
        v7 <<= 4 - mode;
        v7 += v6;
        e[3] = v6;
        e[7] = Math.Clamp(v7, 0, 0xFFF);
    }

    private static int SignExtend(int value, int bitCount)
    {
        value &= (1 << bitCount) - 1;
        return (value & (1 << (bitCount - 1))) != 0 ? value - (1 << bitCount) : value;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Partitions (section 23.20) and void extents (section 23.22)
    // -----------------------------------------------------------------------------------------------------------------

    internal static int SelectPartition(int seed, int x, int y, int z, int partitionCount, bool smallBlock)
    {
        if (smallBlock)
        {
            x <<= 1;
            y <<= 1;
            z <<= 1;
        }

        seed += (partitionCount - 1) * 1024;
        var rnum = Hash52((uint)seed);
        var seed1 = (int)(rnum & 0xF);
        var seed2 = (int)((rnum >> 4) & 0xF);
        var seed3 = (int)((rnum >> 8) & 0xF);
        var seed4 = (int)((rnum >> 12) & 0xF);
        var seed5 = (int)((rnum >> 16) & 0xF);
        var seed6 = (int)((rnum >> 20) & 0xF);
        var seed7 = (int)((rnum >> 24) & 0xF);
        var seed8 = (int)((rnum >> 28) & 0xF);
        var seed9 = (int)((rnum >> 18) & 0xF);
        var seed10 = (int)((rnum >> 22) & 0xF);
        var seed11 = (int)((rnum >> 26) & 0xF);
        var seed12 = (int)(((rnum >> 30) | (rnum << 2)) & 0xF);
        seed1 *= seed1;
        seed2 *= seed2;
        seed3 *= seed3;
        seed4 *= seed4;
        seed5 *= seed5;
        seed6 *= seed6;
        seed7 *= seed7;
        seed8 *= seed8;
        seed9 *= seed9;
        seed10 *= seed10;
        seed11 *= seed11;
        seed12 *= seed12;
        int sh1, sh2;
        if ((seed & 1) != 0)
        {
            sh1 = (seed & 2) != 0 ? 4 : 5;
            sh2 = partitionCount == 3 ? 6 : 5;
        }
        else
        {
            sh1 = partitionCount == 3 ? 6 : 5;
            sh2 = (seed & 2) != 0 ? 4 : 5;
        }

        var sh3 = (seed & 0x10) != 0 ? sh1 : sh2;
        seed1 >>= sh1;
        seed2 >>= sh2;
        seed3 >>= sh1;
        seed4 >>= sh2;
        seed5 >>= sh1;
        seed6 >>= sh2;
        seed7 >>= sh1;
        seed8 >>= sh2;
        seed9 >>= sh3;
        seed10 >>= sh3;
        seed11 >>= sh3;
        seed12 >>= sh3;
        var a = (int)(((seed1 * x) + (seed2 * y) + (seed11 * z) + (rnum >> 14)) & 0x3F);
        var b = (int)(((seed3 * x) + (seed4 * y) + (seed12 * z) + (rnum >> 10)) & 0x3F);
        var c = (int)(((seed5 * x) + (seed6 * y) + (seed9 * z) + (rnum >> 6)) & 0x3F);
        var d = (int)(((seed7 * x) + (seed8 * y) + (seed10 * z) + (rnum >> 2)) & 0x3F);
        if (partitionCount < 4)
        {
            d = 0;
        }

        if (partitionCount < 3)
        {
            c = 0;
        }

        if (a >= b && a >= c && a >= d)
            return 0;

        if (b >= c && b >= d)
            return 1;

        return c >= d ? 2 : 3;
    }

    private static uint Hash52(uint p)
    {
        p ^= p >> 15;
        p -= p << 17;
        p += p << 7;
        p += p << 4;
        p ^= p >> 5;
        p += p << 16;
        p ^= p >> 7;
        p ^= p >> 3;
        p ^= p << 6;
        p ^= p >> 17;
        return p;
    }

    private static void DecodeVoidExtent(UInt128 bits, int blockWidth, int blockHeight, AstcProfile profile, Span<byte> destination, int stride)
    {
        // Table 185: bits 10 and 11 are reserved (1), bit 9 the dynamic range; the extent is valid or all ones
        if (Bits(bits, 10, 2) != 3)
            throw Illegal("its void extent has reserved bits that are not 1");

        var minS = (int)Bits(bits, 12, 13);
        var maxS = (int)Bits(bits, 25, 13);
        var minT = (int)Bits(bits, 38, 13);
        var maxT = (int)Bits(bits, 51, 13);
        var allOnes = minS == 0x1FFF && maxS == 0x1FFF && minT == 0x1FFF && maxT == 0x1FFF;
        if (!allOnes && (minS >= maxS || minT >= maxT))
            throw Illegal("its void extent has a low coordinate that is not below the high one");

        var hdr = Bits(bits, 9, 1) != 0;
        if (hdr && profile != AstcProfile.Hdr)
            throw Illegal("it is an HDR void extent in an LDR format");

        Span<int> texel = stackalloc int[4];
        for (var c = 0; c < 4; c++)
        {
            var value = (int)Bits(bits, 64 + (16 * c), 16);
            texel[c] = profile switch
            {
                AstcProfile.Srgb or AstcProfile.LinearUnorm8 => value >> 8,
                _ => hdr ? value : UnormToHalf(value),
            };
        }

        for (var y = 0; y < blockHeight; y++)
        {
            for (var x = 0; x < blockWidth; x++)
            {
                WriteTexel(texel, profile, destination, (y * stride) + (x * (IsByte(profile) ? 4 : 8)));
            }
        }
    }

    private static ulong Bits(UInt128 bits, int start, int count) => (ulong)((bits >> start) & ((UInt128.One << count) - 1));

    internal static UInt128 ReverseBits(UInt128 value)
    {
        var low = (ulong)value;
        var high = (ulong)(value >> 64);
        return new UInt128(ReverseBits(low), ReverseBits(high));
    }

    private static ulong ReverseBits(ulong value)
    {
        value = ((value >> 1) & 0x5555555555555555) | ((value & 0x5555555555555555) << 1);
        value = ((value >> 2) & 0x3333333333333333) | ((value & 0x3333333333333333) << 2);
        value = ((value >> 4) & 0x0F0F0F0F0F0F0F0F) | ((value & 0x0F0F0F0F0F0F0F0F) << 4);
        return BinaryPrimitives.ReverseEndianness(value);
    }

    private static InvalidImageContentException Illegal(string reason) => Ktx2Format.Invalid("an ASTC block is illegal: " + reason + " (section 23.23 of the Khronos Data Format Specification).");
}
