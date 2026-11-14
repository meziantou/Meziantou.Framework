using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The quantization of the scaled transform coefficients of a transform block into transform coefficient levels: the
/// inverse of the scaling process for transform coefficients of the decoding process (Rec. ITU-T H.265, clause 8.6.3),
/// which multiplies a level by <c>m * levelScale[ qP % 6 ] &lt;&lt; ( qP / 6 )</c> and divides the product by
/// <c>2^bdShift</c>. What a decoder reconstructs from the levels is always obtained from the scaling process of the
/// decoder, never from this type.
/// </summary>
internal static class HevcQuantizer
{
    /// <summary>The scale of a rounding offset: an offset of 128 rounds to nearest.</summary>
    public const int RoundingScale = 256;

    /// <summary>The largest magnitude of a transform coefficient level (clause 7.4.9.11): <c>CoeffMaxY</c> and <c>CoeffMaxC</c>.</summary>
    public const int MaxLevel = HevcInverseTransform.CoeffMax;

    // m[ x ][ y ] when no scaling list applies
    private const int FlatScalingFactor = 16;

    /// <summary>
    /// Quantizes <paramref name="coefficients"/>, the scaled transform coefficients <c>d</c>, to
    /// <paramref name="levels"/>. A level is rounded down when the fractional part of the quotient is below
    /// 1 - <paramref name="rounding"/> / 256: a dead zone that trades a little distortion for fewer and smaller levels.
    /// </summary>
    /// <param name="coefficients">The coefficients: <c>nTbS</c> rows of <c>nTbS</c> values.</param>
    /// <param name="levels">The transform coefficient levels: as many as coefficients.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="qP">The quantization parameter of the block (clause 8.6.2): <c>Qp'Y</c>, <c>Qp'Cb</c> or <c>Qp'Cr</c>, which include <c>QpBdOffset</c>.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="scalingFactors">
    /// <c>m</c>, the rows of the matrix <c>ScalingFactor</c> of the block (<see cref="HevcPictureLayout.ScalingFactors"/>);
    /// or an empty span when <c>m</c> is 16: the sequence has no scaling list, or the block skips the transform and has
    /// more than 4x4 samples.
    /// </param>
    /// <param name="rounding">The rounding offset, in units of 1/256 of a quantization step: 0 to 255.</param>
    /// <returns>The number of levels that are not zero.</returns>
    public static int Quantize(ReadOnlySpan<int> coefficients, Span<int> levels, int log2TrafoSize, int qP, int bitDepth, ReadOnlySpan<byte> scalingFactors, int rounding)
    {
        Debug.Assert(log2TrafoSize is >= 2 and <= 5 && qP >= 0 && rounding is >= 0 and < RoundingScale);
        Debug.Assert(coefficients.Length == 1 << (log2TrafoSize << 1) && levels.Length >= coefficients.Length);
        Debug.Assert(scalingFactors.IsEmpty || scalingFactors.Length >= coefficients.Length);
        var bdShift = GetBdShift(log2TrafoSize, bitDepth);
        var levelScale = GetLevelScale(qP);
        var count = 0;
        if (scalingFactors.IsEmpty)
        {
            // Most coefficients are quantized to zero: the ones whose magnitude plus the offset is below a step, that is whose
            // magnitude is below ceil( ( factor - offset ) / 2^bdShift )
            var factor = FlatScalingFactor * levelScale;
            var offset = (factor * rounding) / RoundingScale;
            var zeroLimit = (uint)Math.Min((factor - offset + (1L << bdShift) - 1) >> bdShift, uint.MaxValue);
            var i = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                // 8 coefficients at a time are skipped when they are all quantized to zero. The magnitudes are compared as
                // unsigned values, which is exact for int.MinValue too
                var limit = Vector128.Create(zeroLimit);

                // The other ones are divided as single-precision values. The factor is levelScale[ qP % 6 ] * 2^shift, and
                // ( n / 2^shift ) / levelScale rounded down twice is n / factor rounded down: n / 2^shift is below 2^24, so
                // that it is exact in single precision and its quotient by levelScale (at most 72) is never rounded up to
                // the next integer. magnitudeLimit keeps n below 2^31 and n / 2^shift below 2^24, or the scalar path quantizes
                // the 8 coefficients
                var shift = 4 + (qP / 6);
                var divisor = Vector128.Create((float)HevcQuantizationTables.LevelScale[qP % 6]);
                var magnitudeLimit = Vector128.Create((uint)Math.Min((Math.Min(int.MaxValue, (1L << (24 + shift)) - 1) - offset) >> bdShift, uint.MaxValue));
                var offsets = Vector128.Create((int)offset);
                var maxLevel = Vector128.Create(MaxLevel);
                for (; i <= coefficients.Length - 8; i += 8)
                {
                    var c0 = Vector128.Create(coefficients.Slice(i, 4));
                    var c1 = Vector128.Create(coefficients.Slice(i + 4, 4));
                    var m0 = Vector128.Abs(c0).AsUInt32();
                    var m1 = Vector128.Abs(c1).AsUInt32();
                    if ((Vector128.LessThan(m0, limit) & Vector128.LessThan(m1, limit)) == Vector128<uint>.AllBitsSet)
                    {
                        Vector128<int>.Zero.CopyTo(levels.Slice(i, 4));
                        Vector128<int>.Zero.CopyTo(levels.Slice(i + 4, 4));
                        continue;
                    }

                    if (Vector128.LessThanOrEqualAll(Vector128.Max(m0, m1), magnitudeLimit))
                    {
                        var l0 = QuantizeFlat(c0, m0.AsInt32(), bdShift, offsets, shift, divisor, maxLevel);
                        var l1 = QuantizeFlat(c1, m1.AsInt32(), bdShift, offsets, shift, divisor, maxLevel);
                        l0.CopyTo(levels.Slice(i, 4));
                        l1.CopyTo(levels.Slice(i + 4, 4));
                        count += BitOperations.PopCount(Vector128.ExtractMostSignificantBits(~Vector128.Equals(l0, Vector128<int>.Zero)) | (Vector128.ExtractMostSignificantBits(~Vector128.Equals(l1, Vector128<int>.Zero)) << 4));
                        continue;
                    }

                    for (var k = i; k < i + 8; k++)
                    {
                        count += QuantizeFlat(coefficients[k], ref levels[k], bdShift, factor, offset);
                    }
                }
            }

            for (; i < coefficients.Length; i++)
            {
                count += QuantizeFlat(coefficients[i], ref levels[i], bdShift, factor, offset);
            }

            return count;
        }

        for (var i = 0; i < coefficients.Length; i++)
        {
            var coefficient = coefficients[i];
            var factor = scalingFactors[i] * levelScale;
            var offset = (factor * rounding) / RoundingScale;
            var level = (int)Math.Min(((Math.Abs((long)coefficient) << bdShift) + offset) / factor, MaxLevel);
            levels[i] = coefficient < 0 ? -level : level;
            if (level is not 0)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The levels of <see cref="Quantize"/> without scaling list, one coefficient at a time: the reference of the vector
    /// path.
    /// </summary>
    internal static int QuantizeFlatScalar(ReadOnlySpan<int> coefficients, Span<int> levels, int log2TrafoSize, int qP, int bitDepth, int rounding)
    {
        var bdShift = GetBdShift(log2TrafoSize, bitDepth);
        var factor = FlatScalingFactor * GetLevelScale(qP);
        var offset = (factor * rounding) / RoundingScale;
        var count = 0;
        for (var i = 0; i < coefficients.Length; i++)
        {
            count += QuantizeFlat(coefficients[i], ref levels[i], bdShift, factor, offset);
        }

        return count;
    }

    // The levels of 4 coefficients whose magnitudes are below the limit of the vector path of Quantize
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> QuantizeFlat(Vector128<int> coefficients, Vector128<int> magnitudes, int bdShift, Vector128<int> offset, int shift, Vector128<float> divisor, Vector128<int> maxLevel)
    {
        var sign = coefficients >> 31;
        var reduced = ((magnitudes << bdShift) + offset) >>> shift;
        var level = Vector128.Min(Vector128.ConvertToInt32(Vector128.ConvertToSingle(reduced) / divisor), maxLevel);
        return (level ^ sign) - sign;
    }

    // The level of a coefficient without scaling list, without branches on its sign or its magnitude, which are hard to
    // predict: the quotient is 0 for a magnitude below the step. Returns 1 when the level is not zero
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int QuantizeFlat(int coefficient, ref int level, int bdShift, long factor, long offset)
    {
        var sign = coefficient >> 31;
        var magnitude = (long)(uint)((coefficient ^ sign) - sign) << bdShift;
        var value = (int)Math.Min((magnitude + offset) / factor, MaxLevel);
        level = (value ^ sign) - sign;
        return value is 0 ? 0 : 1;
    }

    /// <summary>
    /// Makes the levels of a 4x4 sub-block of a transform block tell the sign of their first coefficient by the parity of
    /// the sum of their magnitudes, when the residual coding syntax hides this sign (clause 7.3.8.11, <c>signHidden</c>;
    /// <c>sign_data_hiding_enabled_flag</c>): the sign is hidden when more than 3 scan positions separate the first and the
    /// last significant coefficients of the sub-block, and the coefficient is then negative when the sum is odd. When the
    /// levels do not tell the sign, one magnitude is changed by one: the change that adds the least distortion among those
    /// that leave the sub-block consistent.
    /// </summary>
    /// <remarks>
    /// The caller does not call the method for the blocks whose signs are never hidden: those of a coding unit that
    /// bypasses the scaling and the transform, and those that use the residual differential pulse code modulation.
    /// </remarks>
    /// <param name="coefficients">The scaled transform coefficients that the levels were quantized from.</param>
    /// <param name="levels">The levels of the transform block.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="scanIdx">The scan order of the block: <see cref="HevcScanOrder.Diagonal"/>, <see cref="HevcScanOrder.Horizontal"/> or <see cref="HevcScanOrder.Vertical"/>.</param>
    /// <param name="xS">The horizontal position of the sub-block, in sub-blocks.</param>
    /// <param name="yS">The vertical position of the sub-block, in sub-blocks.</param>
    /// <param name="qP">The quantization parameter of the block.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="scalingFactors"><c>m</c>, as for <see cref="Quantize"/>.</param>
    /// <returns><see langword="true"/> when a level was changed.</returns>
    public static bool HideSign(ReadOnlySpan<int> coefficients, Span<int> levels, int log2TrafoSize, int scanIdx, int xS, int yS, int qP, int bitDepth, ReadOnlySpan<byte> scalingFactors)
    {
        // A sign is only hidden in a sub-block that has two significant coefficients at least: most sub-blocks have fewer
        var nTbS = 1 << log2TrafoSize;
        var origin = (yS << (log2TrafoSize + 2)) + (xS << 2);
        var significantCount = 0;
        for (var y = 0; y < 4; y++)
        {
            var row = Vector128.Create(levels.Slice(origin + (y << log2TrafoSize), 4));
            significantCount += BitOperations.PopCount(Vector128.ExtractMostSignificantBits(~Vector128.Equals(row, Vector128<int>.Zero)));
        }

        if (significantCount < 2)
            return false;

        // The position in the block of the coefficient of each scan position of the sub-block, and the magnitude of its level
        var scan = HevcScanOrder.Get(2, scanIdx);
        Span<int> positions = stackalloc int[16];
        Span<long> magnitudes = stackalloc long[16];
        var significant = 0u;
        var sumOfMagnitudes = 0L;
        for (var n = 0; n < 16; n++)
        {
            positions[n] = (((yS << 2) + HevcScanOrder.Y(scan[n])) * nTbS) + (xS << 2) + HevcScanOrder.X(scan[n]);
            var magnitude = Math.Abs((long)levels[positions[n]]);
            magnitudes[n] = magnitude;
            sumOfMagnitudes += magnitude;
            significant |= (magnitude is 0 ? 0u : 1u) << n;
        }

        var summary = new SubBlockSummary(significant, sumOfMagnitudes);
        if (!summary.TryGetHiddenSign(magnitudes, changed: -1, changedMagnitude: 0, out var firstSigScanPos, out var sumAbsLevel))
            return false;

        // The parity already gives the sign of the level
        if (((sumAbsLevel & 1) is 1) == (levels[positions[firstSigScanPos]] < 0))
            return false;

        var bdShift = GetBdShift(log2TrafoSize, bitDepth);
        var levelScale = GetLevelScale(qP);
        var bestPosition = -1;
        var bestMagnitude = 0;
        var bestNegative = false;
        Int128 bestCost = 0;
        var bestKey = 0L;
        var flat = scalingFactors.IsEmpty;
        for (var n = 0; n < 16; n++)
        {
            var position = positions[n];
            var coefficient = coefficients[position];
            var magnitude = Math.Abs(levels[position]);
            long factor = (flat ? FlatScalingFactor : scalingFactors[position]) * levelScale;

            // The error of the level, in units of 2^-bdShift of a coefficient
            var error = (Math.Abs((long)coefficient) << bdShift) - (magnitude * factor);
            for (var delta = -1; delta <= 1; delta += 2)
            {
                var candidate = magnitude + delta;
                if (candidate is < 0 or > MaxLevel)
                    continue;

                // The squared error of the candidate minus the one of the level:
                // ( error - delta * factor )^2 - error^2 = factor * ( factor - 2 * delta * error ), since delta^2 is 1.
                // Without scaling list, every candidate has the same positive factor: the second term orders the
                // candidates as their costs do, and it fits 64 bits (the error has less than 49 bits). Equal costs keep
                // the first candidate, which is the smaller magnitude of the lowest scan position: a candidate that does
                // not cost less than the best one is not checked
                var key = 0L;
                Int128 cost = 0;
                if (flat)
                {
                    key = factor - (2 * delta * error);
                    if (bestPosition >= 0 && key >= bestKey)
                        continue;
                }
                else
                {
                    cost = (Int128)factor * ((Int128)factor - (2 * (Int128)(delta * error)));
                    if (bestPosition >= 0 && cost >= bestCost)
                        continue;
                }

                // A level that becomes significant has the sign of its coefficient; any sign when the coefficient is zero
                var negative = magnitude is 0 ? coefficient < 0 : levels[position] < 0;
                var anySign = magnitude is 0 && coefficient is 0;
                if (summary.TryGetHiddenSign(magnitudes, n, candidate, out var first, out var sum))
                {
                    var parityNegative = (sum & 1) is 1;
                    if (first == n && anySign)
                    {
                        negative = parityNegative;
                    }
                    else
                    {
                        var firstNegative = first == n ? negative : levels[positions[first]] < 0;
                        if (firstNegative != parityNegative)
                            continue;
                    }
                }

                bestPosition = position;
                bestMagnitude = candidate;
                bestNegative = negative;
                bestKey = key;
                bestCost = cost;
            }
        }

        // Adding one to the last significant coefficient, or subtracting one from it, is always a candidate
        Debug.Assert(bestPosition >= 0);
        levels[bestPosition] = bestNegative ? -bestMagnitude : bestMagnitude;
        return true;
    }

    /// <summary>Returns <c>bdShift</c> of the scaling process (Equations 8-301 and 8-305, without extended precision processing).</summary>
    public static int GetBdShift(int log2TrafoSize, int bitDepth) => bitDepth + log2TrafoSize - 5;

    /// <summary>Returns <c>levelScale[ qP % 6 ] &lt;&lt; ( qP / 6 )</c> (Equation 8-309).</summary>
    public static long GetLevelScale(int qP) => (long)HevcQuantizationTables.LevelScale[qP % 6] << (qP / 6);

    // The residual coding syntax of a sub-block (clause 7.3.8.11): firstSigScanPos, lastSigScanPos and sumAbsLevel of its
    // levels, and the scan positions of the second significant coefficient from each end, so that the values of a sub-block
    // whose level at one scan position changes are known without going through its 16 levels again
    [StructLayout(LayoutKind.Auto)]
    private readonly ref struct SubBlockSummary
    {
        private readonly int _first;
        private readonly int _second;
        private readonly int _last;
        private readonly int _secondToLast;
        private readonly long _sum;

        // significant has the bit n set when the level of the scan position n is not zero, and sum is the sum of the magnitudes
        public SubBlockSummary(uint significant, long sum)
        {
            // 16 and -1 when there is no such position
            _first = BitOperations.TrailingZeroCount(significant | 0x10000);
            _second = BitOperations.TrailingZeroCount((significant & (significant - 1)) | 0x10000);
            _last = 31 - BitOperations.LeadingZeroCount(significant);
            _secondToLast = 31 - BitOperations.LeadingZeroCount(_last < 0 ? 0 : significant & ~(1u << _last));
            _sum = sum;
        }

        // firstSigScanPos and sumAbsLevel of the levels, the level of the scan position "changed" having the magnitude
        // "changedMagnitude" (no change when "changed" is -1). Returns signHidden
        public bool TryGetHiddenSign(ReadOnlySpan<long> magnitudes, int changed, long changedMagnitude, out int firstSigScanPos, out long sumAbsLevel)
        {
            var first = _first;
            var last = _last;
            sumAbsLevel = _sum;
            if (changed >= 0)
            {
                var magnitude = magnitudes[changed];
                sumAbsLevel += changedMagnitude - magnitude;
                if (magnitude is 0 && changedMagnitude is not 0)
                {
                    // The scan position becomes significant
                    first = Math.Min(first, changed);
                    last = Math.Max(last, changed);
                }
                else if (magnitude is not 0 && changedMagnitude is 0)
                {
                    // The scan position is no longer significant: the next significant one becomes the first or the last
                    if (changed == first)
                    {
                        first = _second;
                    }

                    if (changed == last)
                    {
                        last = _secondToLast;
                    }
                }
            }

            firstSigScanPos = first;
            return last - first > 3;
        }
    }
}
