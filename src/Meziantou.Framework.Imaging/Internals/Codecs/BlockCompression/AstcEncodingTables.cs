namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The tables of the ASTC encoder (Khronos Data Format Specification 1.4, section 23), all derived from the decoding
/// procedures of <see cref="AstcBlockDecoder"/> so that the encoder writes exactly what the decoder reads: the block mode
/// of each weight grid and weight range (23.10), the quantization of the color endpoints (23.13) and of the weights
/// (23.16), and the integer sequence encoding (23.12).
/// </summary>
/// <remarks>The tables are computed once, in the static constructor, and never change afterward.</remarks>
internal static class AstcEncodingTables
{
    /// <summary>The number of weight ranges (table 160).</summary>
    public const int WeightRangeCount = 12;

    /// <summary>The weight ranges (table 160), the fewest levels first: (trits, quints, bits).</summary>
    public static readonly (int Trits, int Quints, int Bits)[] WeightRanges =
    [
        (0, 0, 1), (1, 0, 0), (0, 0, 2), (0, 1, 0), (1, 0, 1), (0, 0, 3), (0, 1, 1), (1, 0, 2), (0, 0, 4), (0, 1, 2), (1, 0, 3), (0, 0, 5),
    ];

    private const int MaxGrid = 12;
    private const int MaxLevels = 32;

    // The block mode of each weight grid (2..12 x 2..12), plane count and weight range, or -1 when none encodes them
    private static readonly short[] BlockModes = new short[(MaxGrid - 1) * (MaxGrid - 1) * 2 * WeightRangeCount];

    // The color endpoint integer nearest to each 8-bit value, per color range (AstcBlockDecoder.ColorRanges), and back
    private static readonly byte[] ColorQuantization = new byte[17 * 256];
    private static readonly byte[] ColorUnquantization = new byte[17 * 256];

    // The levels of each weight range sorted by their unquantized weight (0..64): the integer and the weight of each rank,
    // and the rank nearest to each weight
    private static readonly byte[] WeightRankValues = new byte[WeightRangeCount * MaxLevels];
    private static readonly byte[] WeightRankWeights = new byte[WeightRangeCount * MaxLevels];
    private static readonly byte[] WeightNearestRank = new byte[WeightRangeCount * 65];

    // The packed bits of each group of five trits (t0 + 3 t1 + 9 t2 + 27 t3 + 81 t4) and of three quints (q0 + 5 q1 + 25 q2)
    private static readonly byte[] TritPacking = new byte[243];
    private static readonly byte[] QuintPacking = new byte[125];

    static AstcEncodingTables()
    {
        BlockModes.AsSpan().Fill(-1);
        for (var mode = 0; mode < 2048; mode++)
        {
            // The void-extent pattern is not a block mode (section 23.9.3)
            if ((mode & 0x1FF) == 0x1FC)
                continue;

            if (!AstcBlockDecoder.TryDecodeBlockMode(mode, out var width, out var height, out var dualPlane, out var range))
                continue;

            var index = BlockModeIndex(width, height, dualPlane, Array.IndexOf(WeightRanges, range));
            if (BlockModes[index] < 0)
            {
                BlockModes[index] = (short)mode;
            }
        }

        for (var range = 0; range < AstcBlockDecoder.ColorRanges.Length; range++)
        {
            var (trits, quints, bits) = AstcBlockDecoder.ColorRanges[range];
            var levels = GetLevelCount(trits, quints, bits);
            for (var value = 0; value < levels; value++)
            {
                ColorUnquantization[(range * 256) + value] = (byte)AstcBlockDecoder.UnquantizeColor(value, trits, quints, bits);
            }

            for (var target = 0; target < 256; target++)
            {
                var best = 0;
                var bestDistance = int.MaxValue;
                for (var value = 0; value < levels; value++)
                {
                    var distance = Math.Abs(ColorUnquantization[(range * 256) + value] - target);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = value;
                    }
                }

                ColorQuantization[(range * 256) + target] = (byte)best;
            }
        }

        Span<int> order = stackalloc int[MaxLevels];
        for (var range = 0; range < WeightRangeCount; range++)
        {
            var (trits, quints, bits) = WeightRanges[range];
            var levels = GetLevelCount(trits, quints, bits);
            for (var value = 0; value < levels; value++)
            {
                order[value] = value;
            }

            // Insertion sort by unquantized weight: the ranks go from weight 0 to weight 64
            for (var i = 1; i < levels; i++)
            {
                var value = order[i];
                var weight = AstcBlockDecoder.UnquantizeWeight(value, trits, quints, bits);
                var j = i - 1;
                while (j >= 0 && AstcBlockDecoder.UnquantizeWeight(order[j], trits, quints, bits) > weight)
                {
                    order[j + 1] = order[j];
                    j--;
                }

                order[j + 1] = value;
            }

            for (var rank = 0; rank < levels; rank++)
            {
                WeightRankValues[(range * MaxLevels) + rank] = (byte)order[rank];
                WeightRankWeights[(range * MaxLevels) + rank] = (byte)AstcBlockDecoder.UnquantizeWeight(order[rank], trits, quints, bits);
            }

            for (var target = 0; target <= 64; target++)
            {
                var best = 0;
                var bestDistance = int.MaxValue;
                for (var rank = 0; rank < levels; rank++)
                {
                    var distance = Math.Abs(WeightRankWeights[(range * MaxLevels) + rank] - target);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = rank;
                    }
                }

                WeightNearestRank[(range * 65) + target] = (byte)best;
            }
        }

        // The inverse of the decoder's tables 171 and 173: the smallest packed value that decodes to each group. The smallest
        // one has its high bits clear when the last values of the group are 0, so that a sequence can end in a partial group
        // (section 23.12: the bits past the end of the sequence read as 0); a test checks it for every length
        TritPacking.AsSpan().Fill(0xFF);
        Span<int> digits = stackalloc int[5];
        for (var packed = 255; packed >= 0; packed--)
        {
            AstcBlockDecoder.DecodeTrits(packed, digits);
            TritPacking[digits[0] + (3 * digits[1]) + (9 * digits[2]) + (27 * digits[3]) + (81 * digits[4])] = (byte)packed;
        }

        for (var packed = 127; packed >= 0; packed--)
        {
            AstcBlockDecoder.DecodeQuints(packed, digits);
            QuintPacking[digits[0] + (5 * digits[1]) + (25 * digits[2])] = (byte)packed;
        }
    }

    /// <summary>The number of levels of a range: 2^bits, times 3 with a trit or 5 with a quint.</summary>
    public static int GetLevelCount(int trits, int quints, int bits) => (trits != 0 ? 3 : quints != 0 ? 5 : 1) << bits;

    /// <summary>Gets the number of levels of a weight range.</summary>
    public static int GetWeightLevelCount(int range)
    {
        var (trits, quints, bits) = WeightRanges[range];
        return GetLevelCount(trits, quints, bits);
    }

    /// <summary>Gets the number of levels of a color range (an index of <see cref="AstcBlockDecoder.ColorRanges"/>).</summary>
    public static int GetColorLevelCount(int range)
    {
        var (trits, quints, bits) = AstcBlockDecoder.ColorRanges[range];
        return GetLevelCount(trits, quints, bits);
    }

    /// <summary>Gets the 11-bit block mode of a weight grid, or -1 when the block modes cannot encode it (section 23.10).</summary>
    public static int GetBlockMode(int gridWidth, int gridHeight, bool dualPlane, int weightRange) => BlockModes[BlockModeIndex(gridWidth, gridHeight, dualPlane, weightRange)];

    private static int BlockModeIndex(int width, int height, bool dualPlane, int weightRange)
        => ((((((width - 2) * (MaxGrid - 1)) + (height - 2)) * 2) + (dualPlane ? 1 : 0)) * WeightRangeCount) + weightRange;

    /// <summary>Gets the color endpoint integer whose unquantized value is the nearest to an 8-bit value (section 23.13).</summary>
    public static int QuantizeColor(int range, int value) => ColorQuantization[(range * 256) + value];

    /// <summary>Gets the unquantized 8-bit value of a color endpoint integer (section 23.13).</summary>
    public static int UnquantizeColor(int range, int value) => ColorUnquantization[(range * 256) + value];

    /// <summary>Gets the rank (the levels sorted by weight) of the level nearest to an unquantized weight 0..64.</summary>
    public static int GetNearestWeightRank(int range, int weight) => WeightNearestRank[(range * 65) + weight];

    /// <summary>Gets the unquantized weight (0..64, section 23.16) of a rank.</summary>
    public static int GetRankWeight(int range, int rank) => WeightRankWeights[(range * MaxLevels) + rank];

    /// <summary>Gets the weight integer of a rank.</summary>
    public static int GetRankValue(int range, int rank) => WeightRankValues[(range * MaxLevels) + rank];

    /// <summary>
    /// Writes an integer sequence (section 23.12) at a bit position: the values in bits, or in groups of five trits or three
    /// quints interleaved with their low bits (tables 169 and 170). The bits past the end of the sequence are not written.
    /// </summary>
    public static void WriteSequence(ref UInt128 block, int start, ReadOnlySpan<int> values, int trits, int quints, int bitCount)
    {
        var position = start;
        var end = start + AstcBlockDecoder.GetSequenceBits(values.Length, trits, quints, bitCount);
        var mask = (1 << bitCount) - 1;
        if (trits != 0)
        {
            for (var i = 0; i < values.Length; i += 5)
            {
                var m0 = Get(values, i);
                var m1 = Get(values, i + 1);
                var m2 = Get(values, i + 2);
                var m3 = Get(values, i + 3);
                var m4 = Get(values, i + 4);
                int packed = TritPacking[(m0 >> bitCount) + (3 * (m1 >> bitCount)) + (9 * (m2 >> bitCount)) + (27 * (m3 >> bitCount)) + (81 * (m4 >> bitCount))];
                Write(ref block, ref position, end, m0 & mask, bitCount);
                Write(ref block, ref position, end, packed & 3, 2);
                Write(ref block, ref position, end, m1 & mask, bitCount);
                Write(ref block, ref position, end, (packed >> 2) & 3, 2);
                Write(ref block, ref position, end, m2 & mask, bitCount);
                Write(ref block, ref position, end, (packed >> 4) & 1, 1);
                Write(ref block, ref position, end, m3 & mask, bitCount);
                Write(ref block, ref position, end, (packed >> 5) & 3, 2);
                Write(ref block, ref position, end, m4 & mask, bitCount);
                Write(ref block, ref position, end, packed >> 7, 1);
            }

            return;
        }

        if (quints != 0)
        {
            for (var i = 0; i < values.Length; i += 3)
            {
                var m0 = Get(values, i);
                var m1 = Get(values, i + 1);
                var m2 = Get(values, i + 2);
                int packed = QuintPacking[(m0 >> bitCount) + (5 * (m1 >> bitCount)) + (25 * (m2 >> bitCount))];
                Write(ref block, ref position, end, m0 & mask, bitCount);
                Write(ref block, ref position, end, packed & 7, 3);
                Write(ref block, ref position, end, m1 & mask, bitCount);
                Write(ref block, ref position, end, (packed >> 3) & 3, 2);
                Write(ref block, ref position, end, m2 & mask, bitCount);
                Write(ref block, ref position, end, packed >> 5, 2);
            }

            return;
        }

        foreach (var value in values)
        {
            Write(ref block, ref position, end, value, bitCount);
        }
    }

    // A partial last group is completed with zeros (section 23.12)
    private static int Get(ReadOnlySpan<int> values, int index) => index < values.Length ? values[index] : 0;

    /// <summary>Writes the low bits of a value at a bit position, without the bits at or past <paramref name="end"/>.</summary>
    public static void Write(ref UInt128 block, ref int position, int end, int value, int count)
    {
        var available = Math.Min(count, end - position);
        if (available > 0)
        {
            block |= (UInt128)(uint)(value & ((1 << available) - 1)) << position;
        }

        position += count;
    }
}
