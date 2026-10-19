using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The transformation process for scaled transform coefficients (Rec. ITU-T H.265, clause 8.6.4): the inverse of the
/// integer approximations of the discrete cosine transform of 4 to 32 samples, and of the discrete sine transform of 4
/// samples. Every operation is an integer operation: the result is the one of the specification, bit for bit.
/// The vector path computes the sums of products of its scalar reference (<see cref="TransformScalar"/>) in another
/// order, with 32-bit arithmetic that never overflows, and equals it bit for bit.
/// </summary>
internal static class HevcInverseTransform
{
    private static readonly Vector128<short>[] OddWeights = CreateOddWeights();

    /// <summary>The smallest value of a transform coefficient and of an intermediate sample: <c>CoeffMinY</c> and <c>CoeffMinC</c> without extended precision.</summary>
    public const int CoeffMin = -32768;

    /// <summary>The largest value of a transform coefficient and of an intermediate sample: <c>CoeffMaxY</c> and <c>CoeffMaxC</c>.</summary>
    public const int CoeffMax = 32767;

    /// <summary>
    /// Transforms the scaled transform coefficients <c>d</c> of a block into the residual samples <c>r</c>, before their
    /// rounding to the bit depth (clause 8.6.4.1 "General").
    /// </summary>
    /// <param name="block">The rows of the block, <c>nTbS</c> values each: the coefficients, replaced by the residual samples.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="trType">The transform type: 1 for the discrete sine transform of a 4x4 block, otherwise 0.</param>
    /// <param name="scratch">A scratch of <c>nTbS * nTbS</c> values.</param>
    public static void Transform(Span<int> block, int log2TrafoSize, int trType, Span<int> scratch)
    {
        var nTbS = 1 << log2TrafoSize;
        Transform(block, log2TrafoSize, trType, scratch, nTbS, nTbS, residualShift: 0);
    }

    /// <summary>
    /// Transforms the scaled transform coefficients <c>d</c> of a block whose coefficients that are not zero are in its
    /// first rows and columns into the residual samples <c>r</c>, rounded by Equation 8-299 when
    /// <paramref name="residualShift"/> is not zero: <c>( r + ( 1 &lt;&lt; ( bdShift - 1 ) ) ) &gt;&gt; bdShift</c>.
    /// </summary>
    /// <param name="block">The rows of the block, <c>nTbS</c> values each: the coefficients, whose values outside the first <paramref name="rows"/> rows and <paramref name="columns"/> columns are not read and count as zeros; replaced by the residual samples.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="trType">The transform type: 1 for the discrete sine transform of a 4x4 block, otherwise 0.</param>
    /// <param name="scratch">A scratch of <c>nTbS * nTbS</c> values.</param>
    /// <param name="rows">The number of rows that can have coefficients that are not zero: 1 to <c>nTbS</c>.</param>
    /// <param name="columns">The number of columns that can have coefficients that are not zero: a multiple of 4 up to <c>nTbS</c>.</param>
    /// <param name="residualShift"><c>bdShift</c> of Equation 8-299, or 0 for the residual samples before that rounding.</param>
    public static void Transform(Span<int> block, int log2TrafoSize, int trType, Span<int> scratch, int rows, int columns, int residualShift)
    {
        if (HevcSimd.IsSupported)
        {
            TransformVector(block, log2TrafoSize, trType, scratch, rows, columns, residualShift);
            return;
        }

        // The coefficients outside the rows and the columns are zeros
        var nTbS = 1 << log2TrafoSize;
        for (var y = 0; y < nTbS; y++)
        {
            block.Slice((y * nTbS) + (y < rows ? columns : 0), y < rows ? nTbS - columns : nTbS).Clear();
        }

        TransformScalar(block, log2TrafoSize, trType, scratch);
        if (residualShift > 0)
        {
            var rounding = 1 << (residualShift - 1);
            for (var i = 0; i < nTbS * nTbS; i++)
            {
                block[i] = (block[i] + rounding) >> residualShift;
            }
        }
    }

    /// <summary>The transformation of <see cref="Transform(Span{int}, int, int, Span{int})"/>, one sample at a time: the reference of the vector path.</summary>
    internal static void TransformScalar(Span<int> block, int log2TrafoSize, int trType, Span<int> scratch)
    {
        var nTbS = 1 << log2TrafoSize;
        Span<int> input = stackalloc int[32];
        Span<int> output = stackalloc int[32];

        // 1. Each column of d is transformed to e; 2. g = Clip3( coeffMin, coeffMax, ( e + 64 ) >> 7 )
        for (var x = 0; x < nTbS; x++)
        {
            var any = false;
            for (var y = 0; y < nTbS; y++)
            {
                input[y] = block[(y * nTbS) + x];
                any |= input[y] is not 0;
            }

            if (!any)
            {
                for (var y = 0; y < nTbS; y++)
                {
                    scratch[(y * nTbS) + x] = 0;
                }

                continue;
            }

            Transform1D(input, output, log2TrafoSize, trType);
            for (var y = 0; y < nTbS; y++)
            {
                scratch[(y * nTbS) + x] = Math.Clamp((output[y] + 64) >> 7, CoeffMin, CoeffMax);
            }
        }

        // 3. Each row of g is transformed to r
        for (var y = 0; y < nTbS; y++)
        {
            Transform1D(scratch.Slice(y * nTbS, nTbS), block.Slice(y * nTbS, nTbS), log2TrafoSize, trType);
        }
    }


    // The vector path. A block of the discrete sine transform, or of the discrete cosine transform of 4 samples, is
    // transformed in 4 vectors of 4 32-bit lanes (Transform4x4). The larger blocks are transformed 8 columns at a time
    // (ColumnTransform), with 16-bit coefficients: the columns of d, whose results g are transposed, then the columns of
    // the transposed g, which are its rows, whose results r are transposed back. The coefficients after the last one that
    // is not zero add nothing to the sums, and the columns that have no coefficient give columns of zeros.
    [SkipLocalsInit]
    private static void TransformVector(Span<int> block, int log2TrafoSize, int trType, Span<int> scratch, int rows, int columns, int residualShift)
    {
        var nTbS = 1 << log2TrafoSize;
        var size = nTbS * nTbS;
        block = block[..size];
        int lastRow;
        if (columns == nTbS)
        {
            var lastIndex = block[..(rows * nTbS)].LastIndexOfAnyExcept(0);
            lastRow = lastIndex < 0 ? -1 : lastIndex / nTbS;
        }
        else
        {
            lastRow = rows - 1;
            while (lastRow >= 0 && !block.Slice(lastRow * nTbS, columns).ContainsAnyExcept(0))
            {
                lastRow--;
            }
        }

        if (lastRow < 0)
        {
            // A block of zeros transforms to zeros
            block.Clear();
            return;
        }

        if (trType is 0 && lastRow is 0 && !block.Slice(1, columns - 1).ContainsAnyExcept(0))
        {
            // Only the coefficient d[ 0 ][ 0 ]: the column 0 of g is 64 * d[ 0 ][ 0 ] rounded and clipped, the other columns
            // are zeros, and every residual sample is 64 times that value, rounded
            var g = Math.Clamp(((64 * block[0]) + 64) >> 7, CoeffMin, CoeffMax);
            block.Fill(residualShift > 0 ? ((64 * g) + (1 << (residualShift - 1))) >> residualShift : 64 * g);
            return;
        }

        if (log2TrafoSize is 2)
        {
            Transform4x4(block, trType, lastRow, residualShift);
            return;
        }

        // The coefficients of the rows up to the last one that is not zero and of the columns transformed (a multiple of 8),
        // as 16-bit values, then the transposed g
        var values = unsafe(MemoryMarshal.Cast<int, short>(scratch));
        var coefficients = values[..size];
        var transposed = values.Slice(size, size);
        var width = (columns + 7) & ~7;
        for (var y = 0; y <= lastRow; y++)
        {
            var source = block.Slice(y * nTbS, columns);
            var destination = coefficients.Slice(y * nTbS, width);
            var x = 0;
            for (; x + 8 <= columns; x += 8)
            {
                Vector128.Narrow(Vector128.Create(source[x..]), Vector128.Create(source[(x + 4)..])).CopyTo(destination[x..]);
            }

            if (x < columns)
            {
                Vector128.Narrow(Vector128.Create(source[x..]), Vector128<int>.Zero).CopyTo(destination[x..]);
            }
        }

        // The sums of the outputs, 4 lanes for the first 4 columns and 4 for the last ones, and the coefficients of a stage
        Span<Vector128<int>> sums = unsafe(stackalloc Vector128<int>[2 * 32]);
        Span<Vector128<short>> pairs = unsafe(stackalloc Vector128<short>[2 * 8]);

        // 1. Each column of d is transformed to e; 2. g = Clip3( coeffMin, coeffMax, ( e + 64 ) >> 7 ), which the saturation
        // of the narrowing to 16 bits is
        var round = Vector128.Create(64);
        for (var x = 0; x < width; x += 8)
        {
            ColumnTransform(coefficients, nTbS, x, log2TrafoSize, lastRow, sums, pairs);
            for (var y = 0; y < nTbS; y += 8)
            {
                var g0 = NarrowSaturate((sums[(2 * y) + 0] + round) >> 7, (sums[(2 * y) + 1] + round) >> 7);
                var g1 = NarrowSaturate((sums[(2 * y) + 2] + round) >> 7, (sums[(2 * y) + 3] + round) >> 7);
                var g2 = NarrowSaturate((sums[(2 * y) + 4] + round) >> 7, (sums[(2 * y) + 5] + round) >> 7);
                var g3 = NarrowSaturate((sums[(2 * y) + 6] + round) >> 7, (sums[(2 * y) + 7] + round) >> 7);
                var g4 = NarrowSaturate((sums[(2 * y) + 8] + round) >> 7, (sums[(2 * y) + 9] + round) >> 7);
                var g5 = NarrowSaturate((sums[(2 * y) + 10] + round) >> 7, (sums[(2 * y) + 11] + round) >> 7);
                var g6 = NarrowSaturate((sums[(2 * y) + 12] + round) >> 7, (sums[(2 * y) + 13] + round) >> 7);
                var g7 = NarrowSaturate((sums[(2 * y) + 14] + round) >> 7, (sums[(2 * y) + 15] + round) >> 7);
                HevcSimd.Transpose8x8(ref g0, ref g1, ref g2, ref g3, ref g4, ref g5, ref g6, ref g7);
                g0.CopyTo(transposed[((x * nTbS) + y)..]);
                g1.CopyTo(transposed[(((x + 1) * nTbS) + y)..]);
                g2.CopyTo(transposed[(((x + 2) * nTbS) + y)..]);
                g3.CopyTo(transposed[(((x + 3) * nTbS) + y)..]);
                g4.CopyTo(transposed[(((x + 4) * nTbS) + y)..]);
                g5.CopyTo(transposed[(((x + 5) * nTbS) + y)..]);
                g6.CopyTo(transposed[(((x + 6) * nTbS) + y)..]);
                g7.CopyTo(transposed[(((x + 7) * nTbS) + y)..]);
            }
        }

        // 3. Each row of g is transformed to r, then rounded by Equation 8-299: the lanes of the sums are rows
        var rounding = Vector128.Create(residualShift > 0 ? 1 << (residualShift - 1) : 0);
        for (var y = 0; y < nTbS; y += 8)
        {
            ColumnTransform(transposed, nTbS, y, log2TrafoSize, columns - 1, sums, pairs);
            for (var x = 0; x < nTbS; x += 4)
            {
                var r0 = (sums[(2 * x) + 0] + rounding) >> residualShift;
                var r1 = (sums[(2 * x) + 2] + rounding) >> residualShift;
                var r2 = (sums[(2 * x) + 4] + rounding) >> residualShift;
                var r3 = (sums[(2 * x) + 6] + rounding) >> residualShift;
                HevcSimd.Transpose4x4(ref r0, ref r1, ref r2, ref r3);
                r0.CopyTo(block[((y * nTbS) + x)..]);
                r1.CopyTo(block[(((y + 1) * nTbS) + x)..]);
                r2.CopyTo(block[(((y + 2) * nTbS) + x)..]);
                r3.CopyTo(block[(((y + 3) * nTbS) + x)..]);
                r0 = (sums[(2 * x) + 1] + rounding) >> residualShift;
                r1 = (sums[(2 * x) + 3] + rounding) >> residualShift;
                r2 = (sums[(2 * x) + 5] + rounding) >> residualShift;
                r3 = (sums[(2 * x) + 7] + rounding) >> residualShift;
                HevcSimd.Transpose4x4(ref r0, ref r1, ref r2, ref r3);
                r0.CopyTo(block[(((y + 4) * nTbS) + x)..]);
                r1.CopyTo(block[(((y + 5) * nTbS) + x)..]);
                r2.CopyTo(block[(((y + 6) * nTbS) + x)..]);
                r3.CopyTo(block[(((y + 7) * nTbS) + x)..]);
            }
        }
    }

    /// <summary>
    /// Transforms 8 columns of a block of 16-bit values, one per lane, by the even/odd decomposition of the matrix of
    /// Equation 8-317: the rows of the matrix whose index is even are symmetric and the rows whose index is odd
    /// antisymmetric, so that the outputs <c>k</c> and <c>nTbS - 1 - k</c> of the transform of <c>nTbS</c> samples are the
    /// sum and the difference of the output <c>k</c> of the transform of <c>nTbS / 2</c> samples of the coefficients whose
    /// index is even, and of the sum of the products of the coefficients whose index is odd. The sums are those of the
    /// products of the scalar reference, in another order: every intermediate value fits in 32 bits.
    /// </summary>
    /// <param name="source">The rows of the block: the coefficients <c>j</c> of a column are its rows <c>j</c>.</param>
    /// <param name="stride">The number of values of a row of <paramref name="source"/>.</param>
    /// <param name="column">The first of the 8 columns.</param>
    /// <param name="log2Size">The base 2 logarithm of the number of coefficients of a column: 3 to 5.</param>
    /// <param name="last">The last row that can have values that are not zero: the others are not read.</param>
    /// <param name="sums">The outputs <c>k</c>: the 4 first lanes in the vector <c>2 * k</c>, the 4 last ones in the vector <c>2 * k + 1</c>.</param>
    /// <param name="pairs">A scratch of 16 vectors.</param>
    private static void ColumnTransform(ReadOnlySpan<short> source, int stride, int column, int log2Size, int last, Span<Vector128<int>> sums, Span<Vector128<short>> pairs)
    {
        // The transform of 2 samples: the coefficients 0 and nTbS / 2 of the column (the rows 0 and 16 of transMatrix)
        var half = 1 << (log2Size - 1);
        var (first0, first1) = Vector128.Widen(Vector128.Create(source[column..]));
        var (second0, second1) = half <= last ? Vector128.Widen(Vector128.Create(source[((half * stride) + column)..])) : default;
        sums[0] = (first0 + second0) << 6;
        sums[1] = (first1 + second1) << 6;
        sums[2] = (first0 - second0) << 6;
        sums[3] = (first1 - second1) << 6;

        // The transforms of 4 to nTbS samples: the odd part of the transform of n samples has the coefficients
        // ( 2 * m + 1 ) * nTbS / n of the column, whose products are summed by pairs
        ref var weights = ref unsafe(MemoryMarshal.GetArrayDataReference(OddWeights));
        for (var log2n = 2; log2n <= log2Size; log2n++)
        {
            var n = 1 << log2n;
            var shift = log2Size - log2n;
            var count = Math.Min(n / 2, ((last >> shift) + 1) >> 1);
            var pairCount = (count + 1) >> 1;
            for (var p = 0; p < pairCount; p++)
            {
                var first = Vector128.Create(source[(((((4 * p) + 1) << shift) * stride) + column)..]);
                var row = ((4 * p) + 3) << shift;
                var second = row <= last ? Vector128.Create(source[((row * stride) + column)..]) : default;
                Interleave(first, second, out pairs[2 * p], out pairs[(2 * p) + 1]);
            }

            // Two outputs at a time, k and k + 1, whose weights are n / 4 pairs apart
            ref var sum = ref unsafe(MemoryMarshal.GetReference(sums));
            for (var k = 0; k < n / 2; k += 2)
            {
                var low0 = Vector128<int>.Zero;
                var high0 = Vector128<int>.Zero;
                var low1 = Vector128<int>.Zero;
                var high1 = Vector128<int>.Zero;
                ref var pair = ref unsafe(MemoryMarshal.GetReference(pairs));
                ref var weight = ref weights;
                for (var p = 0; p < pairCount; p++)
                {
                    var first = pair;
                    var second = unsafe(Unsafe.Add(ref pair, 1));
                    MultiplyAddPair(ref low0, ref high0, first, second, weight);
                    MultiplyAddPair(ref low1, ref high1, first, second, unsafe(Unsafe.Add(ref weight, n / 4)));
                    pair = ref unsafe(Unsafe.Add(ref pair, 2));
                    weight = ref unsafe(Unsafe.Add(ref weight, 1));
                }

                weights = ref unsafe(Unsafe.Add(ref weights, n / 2));
                Combine(ref unsafe(Unsafe.Add(ref sum, 2 * k)), ref unsafe(Unsafe.Add(ref sum, 2 * (n - 1 - k))), low0, high0);
                Combine(ref unsafe(Unsafe.Add(ref sum, 2 * (k + 1))), ref unsafe(Unsafe.Add(ref sum, 2 * (n - 2 - k))), low1, high1);
            }
        }
    }

    // The outputs k and n - 1 - k of the transform of n samples: the sum and the difference of the output k of the transform
    // of n / 2 samples, in the place of the first one, and of the sum of the odd part
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Combine(ref Vector128<int> first, ref Vector128<int> last, Vector128<int> low, Vector128<int> high)
    {
        var evenLow = first;
        var evenHigh = unsafe(Unsafe.Add(ref first, 1));
        first = evenLow + low;
        unsafe { Unsafe.Add(ref first, 1) = evenHigh + high; }
        last = evenLow - low;
        unsafe { Unsafe.Add(ref last, 1) = evenHigh - high; }
    }

    // The two vectors of the pair of rows of the coefficients that MultiplyAddPair takes: the rows on Arm64, their lanes
    // interleaved on x64 (the lanes 0 to 3 of both rows, then the lanes 4 to 7)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Interleave(Vector128<short> first, Vector128<short> second, out Vector128<short> low, out Vector128<short> high)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            low = first;
            high = second;
            return;
        }

        low = Sse2.UnpackLow(first, second);
        high = Sse2.UnpackHigh(first, second);
    }

    // Adds the products of a pair of rows of coefficients (Interleave) by their weights: the lanes 0 and 1 of weight, which
    // every pair of lanes repeats
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MultiplyAddPair(ref Vector128<int> low, ref Vector128<int> high, Vector128<short> first, Vector128<short> second, Vector128<short> weight)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            low = AdvSimd.MultiplyBySelectedScalarWideningLowerAndAdd(low, first.GetLower(), weight, 0);
            high = AdvSimd.MultiplyBySelectedScalarWideningUpperAndAdd(high, first, weight, 0);
            low = AdvSimd.MultiplyBySelectedScalarWideningLowerAndAdd(low, second.GetLower(), weight, 1);
            high = AdvSimd.MultiplyBySelectedScalarWideningUpperAndAdd(high, second, weight, 1);
            return;
        }

        low += Sse2.MultiplyAddAdjacent(first, weight);
        high += Sse2.MultiplyAddAdjacent(second, weight);
    }

    // The 16-bit values of 8 lanes, saturated
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<short> NarrowSaturate(Vector128<int> low, Vector128<int> high)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.ExtractNarrowingSaturateUpper(AdvSimd.ExtractNarrowingSaturateLower(low), high);

        return Sse2.PackSignedSaturate(low, high);
    }

    // The transform of a 4x4 block in 4 vectors: its rows, then its transposed columns, then its rows again
    private static void Transform4x4(Span<int> block, int trType, int lastRow, int residualShift)
    {
        var r0 = Vector128.Create(block);
        var r1 = lastRow >= 1 ? Vector128.Create(block[4..]) : default;
        var r2 = lastRow >= 2 ? Vector128.Create(block[8..]) : default;
        var r3 = lastRow >= 3 ? Vector128.Create(block[12..]) : default;
        Transform4(trType, ref r0, ref r1, ref r2, ref r3);
        var round = Vector128.Create(64);
        var min = Vector128.Create(CoeffMin);
        var max = Vector128.Create(CoeffMax);
        r0 = Vector128.Min(Vector128.Max((r0 + round) >> 7, min), max);
        r1 = Vector128.Min(Vector128.Max((r1 + round) >> 7, min), max);
        r2 = Vector128.Min(Vector128.Max((r2 + round) >> 7, min), max);
        r3 = Vector128.Min(Vector128.Max((r3 + round) >> 7, min), max);
        HevcSimd.Transpose4x4(ref r0, ref r1, ref r2, ref r3);
        Transform4(trType, ref r0, ref r1, ref r2, ref r3);
        var rounding = Vector128.Create(residualShift > 0 ? 1 << (residualShift - 1) : 0);
        r0 = (r0 + rounding) >> residualShift;
        r1 = (r1 + rounding) >> residualShift;
        r2 = (r2 + rounding) >> residualShift;
        r3 = (r3 + rounding) >> residualShift;
        HevcSimd.Transpose4x4(ref r0, ref r1, ref r2, ref r3);
        r0.CopyTo(block);
        r1.CopyTo(block[4..]);
        r2.CopyTo(block[8..]);
        r3.CopyTo(block[12..]);
    }

    // The transforms of 4 samples of the lanes of 4 vectors (Transform1D)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform4(int trType, ref Vector128<int> x0, ref Vector128<int> x1, ref Vector128<int> x2, ref Vector128<int> x3)
    {
        if (trType is 1)
        {
            // Equation 8-315
            var matrix = HevcTransformTables.DstMatrix;
            var y0 = (x0 * matrix[0]) + (x1 * matrix[4]) + (x2 * matrix[8]) + (x3 * matrix[12]);
            var y1 = (x0 * matrix[1]) + (x1 * matrix[5]) + (x2 * matrix[9]) + (x3 * matrix[13]);
            var y2 = (x0 * matrix[2]) + (x1 * matrix[6]) + (x2 * matrix[10]) + (x3 * matrix[14]);
            var y3 = (x0 * matrix[3]) + (x1 * matrix[7]) + (x2 * matrix[11]) + (x3 * matrix[15]);
            (x0, x1, x2, x3) = (y0, y1, y2, y3);
            return;
        }

        // Equation 8-317 with the rows 0, 8, 16 and 24 of transMatrix: ( 64, 64, 64, 64 ), ( 83, 36, -36, -83 ),
        // ( 64, -64, -64, 64 ) and ( 36, -83, 83, -36 )
        var even0 = (x0 + x2) << 6;
        var even1 = (x0 - x2) << 6;
        var odd0 = (x1 * 83) + (x3 * 36);
        var odd1 = (x1 * 36) - (x3 * 83);
        x0 = even0 + odd0;
        x1 = even1 + odd1;
        x2 = even1 - odd1;
        x3 = even0 - odd0;
    }

    // The weights of the odd parts of ColumnTransform, by pairs: for the transform of n samples (4 to 32), its output k and
    // its pair p, the values of the rows ( 4 * p + 1 ) * 32 / n and ( 4 * p + 3 ) * 32 / n of transMatrix at the
    // column k, in the even and the odd lanes
    private static Vector128<short>[] CreateOddWeights()
    {
        var matrix = HevcTransformTables.TransMatrix;
        var weights = new Vector128<short>[2 + 8 + 32 + 128];
        var index = 0;
        for (var n = 4; n <= 32; n *= 2)
        {
            var step = 32 / n;
            for (var k = 0; k < n / 2; k++)
            {
                for (var p = 0; p < n / 4; p++)
                {
                    var first = matrix[(((4 * p) + 1) * step * 32) + k];
                    var second = matrix[(((4 * p) + 3) * step * 32) + k];
                    weights[index++] = Vector128.Create((second * 65536) + (first & 0xFFFF)).AsInt16();
                }
            }
        }

        return weights;
    }


    /// <summary>
    /// Transforms a list of scaled transform coefficients <c>x</c> into the list of transformed samples <c>y</c> (clause
    /// 8.6.4.2 "Transformation process").
    /// </summary>
    public static void Transform1D(ReadOnlySpan<int> x, Span<int> y, int log2TrafoSize, int trType)
    {
        var nTbS = 1 << log2TrafoSize;
        if (trType is 1)
        {
            // Equation 8-315
            var transMatrix = HevcTransformTables.DstMatrix;
            for (var i = 0; i < 4; i++)
            {
                var sum = 0;
                for (var j = 0; j < 4; j++)
                {
                    sum += transMatrix[(j * 4) + i] * x[j];
                }

                y[i] = sum;
            }

            return;
        }

        // Equation 8-317: the coefficient j is the one of the row j * 2^( 5 - Log2( nTbS ) ) of the matrix
        var matrix = HevcTransformTables.TransMatrix;
        var rowStep = 32 << (5 - log2TrafoSize);
        var last = nTbS - 1;
        while (last > 0 && x[last] is 0)
        {
            last--;
        }

        for (var i = 0; i < nTbS; i++)
        {
            var sum = 0;
            for (var j = 0; j <= last; j++)
            {
                sum += matrix[(j * rowStep) + i] * x[j];
            }

            y[i] = sum;
        }
    }
}
