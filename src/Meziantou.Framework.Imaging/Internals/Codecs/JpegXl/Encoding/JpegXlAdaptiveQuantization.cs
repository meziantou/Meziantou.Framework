namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The quantization factor of each block of a lossy JPEG XL frame: finer where an error shows (flat areas, the
/// neighborhood of an edge), coarser where the content hides it (texture). The standard leaves the choice to the
/// encoder. The field is computed from the image once: the encoder does not decode its output to refine it.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// Masking: the squared difference of each luma sample with the mean of its four neighbors, in a perceptual scale, is
/// averaged over cells of 4x4 samples. A block is given the weighted sum of the four smallest cells of the 3x3
/// neighborhood of each of its four cells: a block next to a flat area counts as flat, since an error would be seen there.
/// </description></item>
/// <item><description>
/// Modulations of the exponent of each block: the brightness of its samples, its amount of high frequencies, and its
/// amount of blue.
/// </description></item>
/// <item><description>
/// A transform of several blocks has one factor: the largest value of its blocks at a low distance, their mean at a
/// high one (<see cref="GetTransformValue"/>).
/// </description></item>
/// <item><description>
/// The scale of the frame gives the median of the field, less its median absolute deviation, the factor 5: the factors
/// are small integers, and a field that varies much has more of them (<see cref="GetGlobalScale"/>).
/// </description></item>
/// </list>
/// The arithmetic is float32 in a fixed order, without fused multiply-add.
/// </remarks>
internal static class JpegXlAdaptiveQuantization
{
    /// <summary>The number of samples that <see cref="ComputeField"/> reads around the rectangle of its blocks.</summary>
    public const int Margin = 5;

    /// <summary>The largest quantization factor of a block.</summary>
    public const int MaxQuantFactor = 256;

    /// <summary>The value of the field of a block that no modulation changes, at distance 1.</summary>
    public const float ScaleAtDistance1 = 0.765f;

    private const float InverseLog2E = 0.6931471805599453f;
    private const float GammaMultiplier = 226.77216153508914f;
    private const float GammaMultiplier2 = 1.0f / 73.377132366608819f;
    private const float GammaReturnMultiplier = GammaMultiplier2 * 18.6580932135f * InverseLog2E;
    private const float GammaOffset = 7.7825991679894591f;
    private const float GammaEpsilon = 1e-2f;
    private const float RatioNumeratorMultiplier = GammaReturnMultiplier * 3 * GammaMultiplier;
    private const float RatioOffset = (GammaOffset * InverseLog2E) + GammaEpsilon;
    private const float RatioDenominatorMultiplier = InverseLog2E * GammaMultiplier;

    // The XYB gamma is 3; the one of the eye is around 2.6: one more cubic root in the differences brings the two closer
    private const float MatchGammaOffset = 0.019f;
    private const float DifferenceLimit = 0.2f;

    private static readonly float MaskingMultiplier = MathF.Sqrt(211.66567973503678f * 1e8f);

    /// <summary>Gets the length of the working storage of <see cref="ComputeField"/> for a rectangle of blocks.</summary>
    public static int GetScratchLength(int blocksWide, int blocksHigh) => checked(((2 * blocksWide) + 2) * ((2 * blocksHigh) + 2));

    /// <summary>Computes the field of a rectangle of blocks of a frame.</summary>
    /// <param name="x">The X samples of the rectangle and of <see cref="Margin"/> samples around it: outside the frame, the nearest sample of the frame.</param>
    /// <param name="y">The Y samples, in the same layout.</param>
    /// <param name="b">The B samples, in the same layout.</param>
    /// <param name="stride">The distance between two rows of the samples.</param>
    /// <param name="blockX">The first column of blocks of the rectangle in the frame.</param>
    /// <param name="blockY">The first row of blocks of the rectangle in the frame.</param>
    /// <param name="blocksWide">The number of columns of blocks of the rectangle.</param>
    /// <param name="blocksHigh">The number of rows of blocks of the rectangle.</param>
    /// <param name="frameBlocksWide">The number of columns of blocks of the frame.</param>
    /// <param name="frameBlocksHigh">The number of rows of blocks of the frame.</param>
    /// <param name="distance">The distance of the frame: 1 where a difference is barely visible.</param>
    /// <param name="scale">The value of a block that no modulation changes.</param>
    /// <param name="scratch">Working storage of <see cref="GetScratchLength"/> values.</param>
    /// <param name="field">Receives the value of each block of the rectangle.</param>
    /// <param name="fieldStride">The distance between two rows of <paramref name="field"/>.</param>
    public static void ComputeField(ReadOnlySpan<float> x, ReadOnlySpan<float> y, ReadOnlySpan<float> b, int stride, int blockX, int blockY, int blocksWide, int blocksHigh, int frameBlocksWide, int frameBlocksHigh, float distance, float scale, Span<float> scratch, Span<float> field, int fieldStride)
    {
        // The cells of the rectangle and one more on each side that the frame has: 4x4 samples each
        var leadX = blockX > 0 ? 1 : 0;
        var leadY = blockY > 0 ? 1 : 0;
        var cellsWide = (2 * blocksWide) + leadX + (blockX + blocksWide < frameBlocksWide ? 1 : 0);
        var cellsHigh = (2 * blocksHigh) + leadY + (blockY + blocksHigh < frameBlocksHigh ? 1 : 0);
        var cells = scratch[..(cellsWide * cellsHigh)];
        for (var cellY = 0; cellY < cellsHigh; cellY++)
        {
            for (var cellX = 0; cellX < cellsWide; cellX++)
            {
                var origin = ((Margin + ((cellY - leadY) * 4)) * stride) + Margin + ((cellX - leadX) * 4);
                var column0 = 0f;
                var column1 = 0f;
                var column2 = 0f;
                var column3 = 0f;
                for (var row = 0; row < 4; row++)
                {
                    var position = origin + (row * stride);
                    column0 += GetMaskedDifference(y, position, stride);
                    column1 += GetMaskedDifference(y, position + 1, stride);
                    column2 += GetMaskedDifference(y, position + 2, stride);
                    column3 += GetMaskedDifference(y, position + 3, stride);
                }

                cells[(cellY * cellsWide) + cellX] = (column0 + column1 + column2 + column3) * 0.25f;
            }
        }

        // The weights of the four smallest cells of a neighborhood: the smallest counts more at a low distance
        ReadOnlySpan<float> weightBase = [0.125f, 0.1f, 0.09f, 0.06f];
        ReadOnlySpan<float> weightAdd = [0.0f, -0.1f, -0.09f, -0.06f];
        const float WeightTotal = 0.29959705784054957f;
        var weightMix = distance < 2.0f ? (2.0f - distance) * (1.0f / 2.0f) : 0.0f;
        Span<float> weights = stackalloc float[4];
        var weightSum = 0.0f;
        for (var i = 0; i < 4; i++)
        {
            weights[i] = weightBase[i] + (weightMix * weightAdd[i]);
            weightSum += weights[i];
        }

        for (var i = 0; i < 4; i++)
        {
            weights[i] *= WeightTotal / weightSum;
        }

        // The modulations are damped at high distances
        const float DampenRampStart = 2.0f;
        const float DampenRampEnd = 14.0f;
        var baseLevel = 0.48f * scale;
        var dampen = 1.0f;
        if (distance >= DampenRampStart)
        {
            dampen = Math.Max(0, 1.0f - ((distance - DampenRampStart) / (DampenRampEnd - DampenRampStart)));
        }

        var multiplier = scale * dampen;
        var addend = (1.0f - dampen) * baseLevel;
        Span<float> smallest = stackalloc float[4];
        for (var by = 0; by < blocksHigh; by++)
        {
            for (var bx = 0; bx < blocksWide; bx++)
            {
                var masking = 0f;
                for (var half = 0; half < 4; half++)
                {
                    var cellX = (2 * bx) + (half & 1) + leadX;
                    var cellY = (2 * by) + (half >> 1) + leadY;
                    var left = cellX >= 1 ? cellX - 1 : cellX;
                    var right = cellX + 1 < cellsWide ? cellX + 1 : cellX;
                    var top = (cellY >= 1 ? cellY - 1 : cellY) * cellsWide;
                    var middle = cellY * cellsWide;
                    var bottom = (cellY + 1 < cellsHigh ? cellY + 1 : cellY) * cellsWide;
                    smallest.Fill(float.MaxValue);
                    StoreSmallest(cells[middle + cellX], smallest);
                    StoreSmallest(cells[middle + left], smallest);
                    StoreSmallest(cells[middle + right], smallest);
                    StoreSmallest(cells[top + left], smallest);
                    StoreSmallest(cells[top + cellX], smallest);
                    StoreSmallest(cells[top + right], smallest);
                    StoreSmallest(cells[bottom + left], smallest);
                    StoreSmallest(cells[bottom + cellX], smallest);
                    StoreSmallest(cells[bottom + right], smallest);
                    masking += (weights[0] * smallest[0]) + (weights[1] * smallest[1]) + (weights[2] * smallest[2]) + (weights[3] * smallest[3]);
                }

                var origin = ((Margin + (by * 8)) * stride) + Margin + (bx * 8);
                var exponent = ModulateByGamma(x, y, origin, stride, GetMask(masking));
                exponent = Math.Min(ModulateByHighFrequencies(y, origin, stride, exponent), ModulateByBlue(x, y, b, origin, stride, exponent));

                // The modulations are the ones of an exponent
                field[(by * fieldStride) + bx] = (MathF.Exp(exponent) * multiplier) + addend;
            }
        }
    }

    /// <summary>
    /// Gets the value of the field for a transform: the one of its block, or of its blocks the largest one at a low distance
    /// and the mean at a high one.
    /// </summary>
    /// <param name="field">The field, from the first block of the transform.</param>
    /// <param name="stride">The distance between two rows of <paramref name="field"/>.</param>
    /// <param name="blocksWide">The number of columns of blocks of the transform.</param>
    /// <param name="blocksHigh">The number of rows of blocks of the transform.</param>
    /// <param name="distance">The distance of the frame.</param>
    public static float GetTransformValue(ReadOnlySpan<float> field, int stride, int blocksWide, int blocksHigh, float distance)
    {
        var max = field[0];
        if (blocksWide * blocksHigh == 1)
            return max;

        var mean = 0.0f;
        for (var y = 0; y < blocksHigh; y++)
        {
            for (var x = 0; x < blocksWide; x++)
            {
                var value = field[(y * stride) + x];
                mean += value;
                max = Math.Max(value, max);
            }
        }

        mean /= blocksHigh * blocksWide;
        if (blocksHigh * blocksWide >= 4)
        {
            const float Limit = 1.54138f;
            const float Multiplier = 0.56391f;
            var mixer = distance > Limit ? Math.Max(0.0f, 1.0f - ((distance - Limit) * Multiplier)) : 1.0f;
            max = (max * mixer) + ((1.0f - mixer) * mean);
        }

        return max;
    }

    /// <summary>
    /// Gets the scale of the quantization of a frame, in units of 1/65536: the one that gives the median of the field,
    /// less its median absolute deviation, the factor 5.
    /// </summary>
    /// <param name="field">The value of each block of the frame.</param>
    /// <param name="scratch">Working storage of the length of <paramref name="field"/>.</param>
    /// <param name="maximum">The largest scale that may be returned.</param>
    public static int GetGlobalScale(ReadOnlySpan<float> field, Span<float> scratch, int maximum)
    {
        const float FieldTarget = 5;
        scratch = scratch[..field.Length];
        field.CopyTo(scratch);
        scratch.Sort();
        var median = scratch[scratch.Length / 2];
        for (var i = 0; i < scratch.Length; i++)
        {
            scratch[i] = MathF.Abs(scratch[i] - median);
        }

        scratch.Sort();
        var deviation = scratch[scratch.Length / 2];
        var scale = 65536f * (median - deviation) / FieldTarget;
        if (float.IsNaN(scale) || scale < 1)
        {
            scale = 1;
        }

        return Math.Min((int)Math.Min(scale, 1 << 15), Math.Max(1, maximum));
    }

    /// <summary>Gets the quantization factor of a block from the value of the field and the scale of the frame.</summary>
    /// <param name="value">The value of the field.</param>
    /// <param name="inverseGlobalScale">65536 divided by the scale of the frame.</param>
    public static int GetQuantFactor(float value, float inverseGlobalScale)
        => Math.Clamp((int)((value * inverseGlobalScale) + 0.5f), 1, MaxQuantFactor);

    /// <summary>
    /// The ratio of the derivatives of the cubic root, which the samples went through, and of the logarithmic curve of the
    /// perceptual scale: what moves a difference of samples to that scale.
    /// </summary>
    private static float GetGammaRatio(float value, bool invert)
    {
        value = Math.Max(value, 0);
        var squared = value * value;
        var numerator = (RatioNumeratorMultiplier * squared) + GammaEpsilon;
        var denominator = (RatioDenominatorMultiplier * value * squared) + RatioOffset;
        return invert ? numerator / denominator : denominator / numerator;
    }

    /// <summary>The squared difference of a sample with the mean of its four neighbors, limited, in the scale of the masking.</summary>
    private static float GetMaskedDifference(ReadOnlySpan<float> samples, int position, int stride)
    {
        var value = samples[position];
        var mean = 0.25f * (samples[position + stride] + samples[position - stride] + samples[position - 1] + samples[position + 1]);
        var difference = GetGammaRatio(value + MatchGammaOffset, invert: false) * (value - mean);
        difference *= difference;
        if (difference >= DifferenceLimit)
        {
            difference = DifferenceLimit;
        }

        const float Offset = 27.505837037000106f;
        return 0.25f * MathF.Sqrt((difference * MaskingMultiplier) + Offset);
    }

    /// <summary>Inserts a value in four values sorted from the smallest, when it is smaller than the last one.</summary>
    private static void StoreSmallest(float value, Span<float> smallest)
    {
        if (float.IsNaN(value) || value >= smallest[3])
            return;

        var index = 3;
        while (index > 0 && value < smallest[index - 1])
        {
            smallest[index] = smallest[index - 1];
            index--;
        }

        smallest[index] = value;
    }

    /// <summary>The exponent of a block from its masking.</summary>
    private static float GetMask(float masking)
    {
        const float Base = -0.7647f;
        const float Multiplier4 = 9.4708735624378946f;
        const float Multiplier2 = 17.35036561631863f;
        const float Offset2 = 302.59587815579727f;
        const float Multiplier3 = 6.7943250517376494f;
        const float Offset3 = 3.7179635626140772f;
        const float Offset4 = 0.25f * Offset3;
        const float Multiplier0 = 0.80061762862741759f;
        var v1 = Math.Max(masking * Multiplier0, 1e-3f);
        var v2 = 1.0f / (v1 + Offset2);
        var v3 = 1.0f / ((v1 * v1) + Offset3);
        var v4 = 1.0f / ((v1 * v1) + Offset4);
        return Base + ((Multiplier4 * v4) + ((Multiplier2 * v2) + (Multiplier3 * v3)));
    }

    /// <summary>Changes the exponent of a block by the brightness of its samples: the two cone responses that X and Y are made of.</summary>
    private static float ModulateByGamma(ReadOnlySpan<float> x, ReadOnlySpan<float> y, int origin, int stride, float exponent)
    {
        const float Bias = 0.16f;
        const float Gamma = 0.1005613337192697f;
        var ratio = 0.0f;
        for (var dy = 0; dy < 8; dy++)
        {
            var position = origin + (dy * stride);
            for (var dx = 0; dx < 8; dx++)
            {
                var valueY = y[position + dx] + Bias;
                var valueX = x[position + dx];
                ratio += GetGammaRatio(valueY - valueX, invert: true);
                ratio += GetGammaRatio(valueY + valueX, invert: true);
            }
        }

        ratio *= 0.5f / 64;
        return (Gamma * MathF.Log2(ratio)) + exponent;
    }

    /// <summary>Changes the exponent of a block by its amount of high frequencies: the limited differences of its neighboring samples.</summary>
    private static float ModulateByHighFrequencies(ReadOnlySpan<float> y, int origin, int stride, float exponent)
    {
        const float Limit = 0.0206f;
        const float Multiplier = -0.38f;
        const float Offset = 0.42f;
        var sum = 0.0f;
        for (var dy = 0; dy < 8; dy++)
        {
            var position = origin + (dy * stride);
            var below = dy == 7 ? position : position + stride;
            for (var dx = 0; dx < 8; dx++)
            {
                var value = y[position + dx];
                if (dx < 7)
                {
                    sum += Math.Min(Limit, MathF.Abs(value - y[position + dx + 1]));
                }

                sum += Math.Min(Limit, MathF.Abs(value - y[below + dx]));
            }
        }

        return (sum * Multiplier) + Offset + exponent;
    }

    /// <summary>
    /// Changes the exponent of a block by its amount of blue: blue is seen with more accuracy where the two other
    /// responses are low. A block that is all blue is a flat blue, which does not need it.
    /// </summary>
    private static float ModulateByBlue(ReadOnlySpan<float> x, ReadOnlySpan<float> y, ReadOnlySpan<float> b, int origin, int stride, float exponent)
    {
        const float Limit = 0.010474084867598155f;
        const float Offset = 0.0031994768654636393f;
        const float Multiplier = 0.90590804735610064f;
        const float MaxLimit = 15.463398341612438f;
        var sum = 0.0f;
        for (var dy = 0; dy < 8; dy++)
        {
            var position = origin + (dy * stride);
            for (var dx = 0; dx < 8; dx++)
            {
                var valueB = b[position + dx];
                var effectiveY = y[position + dx] + Offset + MathF.Abs(x[position + dx]);
                if (valueB > effectiveY)
                {
                    sum += Math.Min(valueB - effectiveY, Limit);
                }
            }
        }

        if (sum >= 32 * Limit)
        {
            sum = (64 * Limit) - sum;
        }

        if (sum >= MaxLimit * Limit)
        {
            sum = MaxLimit * Limit;
        }

        return (sum * Multiplier) + exponent;
    }
}
