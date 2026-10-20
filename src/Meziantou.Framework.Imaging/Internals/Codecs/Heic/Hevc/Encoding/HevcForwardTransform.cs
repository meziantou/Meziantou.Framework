using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The forward transforms of the encoder: the inverses of the scaling and transformation process of the decoding process
/// (Rec. ITU-T H.265, clauses 8.6.2, 8.6.4 and 8.6.5), which is the only direction that the specification describes.
/// Every operation is an integer operation.
/// </summary>
/// <remarks>
/// <para>
/// The transformation process of the decoder is <c>r = ( M' * d * M ) / 2^( 27 - bitDepth )</c>, <c>M</c> being the matrix
/// of the basis functions of the transform (<c>transMatrix</c>) and <c>M'</c> its transposed matrix: a first stage on the
/// columns, a division by 2^7, a second stage on the rows and a division by 2^( 20 - bitDepth ). The forward transform is
/// therefore <c>d = ( G' * r * G ) * 2^( 27 - bitDepth )</c>, <c>G</c> being the inverse of <c>M</c>.
/// </para>
/// <para>
/// <c>M</c> is close to an orthogonal matrix, not one: its inverse is close to its transposed matrix divided by
/// <c>64 * 64 * nTbS</c>, and is not exactly that. The inverses are not written out: they are computed, exactly, from the
/// responses of the kernel of the decoder to each coefficient (<see cref="HevcInverseTransform.Transform1D"/>), so that
/// the two directions cannot disagree, then rounded to the bases of the transform.
/// </para>
/// <para>
/// The forward transform is not normative: only the reconstruction is, and the encoder reconstructs with the inverse
/// process of the decoder. It is computed in two stages of 32-bit sums of products (<see cref="Forward"/>), each rounded,
/// which differ from the exact product by the inverse (<see cref="ForwardExact"/>) by a few units of the last place of
/// the coefficients, far below the step of the quantizer.
/// </para>
/// </remarks>
internal static class HevcForwardTransform
{
    // The fractional bits of the bases of the exact product (ForwardExact), which are close to the basis functions of the
    // specification: their largest values are near 90 * 2^ExactBasisBits
    private const int ExactBasisBits = 12;

    // The second stage of Forward divides its sums by 2^ColumnShift: see the remarks of GetBasisBits
    private const int ColumnShift = 15;

    // The bases of Forward, by the base 2 logarithm of nTbS minus 2: those of the rows (the first stage), then those of the
    // columns (the second stage). RowBases[ log2TrafoSize - 2 ][ ( k * nTbS ) + i ] is the value of the coefficient k at the
    // sample i
    private static readonly int[][] RowBases = new int[4][];
    private static readonly int[][] ColumnBases = new int[4][];
    private static readonly int[] DstRowBasis;
    private static readonly int[] DstColumnBasis;

    [SuppressMessage("Performance", "CA1810:Initialize reference type static fields inline", Justification = "Both bases of a size are rounded from the same exact inverse, which is computed once.")]
    static HevcForwardTransform()
    {
        for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
        {
            var (rowBits, columnBits, _) = GetBasisBits(log2TrafoSize);
            var bases = CreateBases(log2TrafoSize, trType: 0, rowBits, columnBits);
            RowBases[log2TrafoSize - 2] = bases[0];
            ColumnBases[log2TrafoSize - 2] = bases[1];
        }

        var (dstRowBits, dstColumnBits, _) = GetBasisBits(2);
        var dstBases = CreateBases(2, trType: 1, dstRowBits, dstColumnBits);
        DstRowBasis = dstBases[0];
        DstColumnBasis = dstBases[1];
    }

    /// <summary>
    /// Returns the base 2 logarithm of the squared gain of the transforms of a size: the ratio between the sum of the
    /// squares of the scaled transform coefficients of a residual and the sum of the squares of its samples, up to what
    /// separates the matrices from orthogonal ones. A squared error of coefficients divided by the gain is the squared
    /// error of the samples.
    /// </summary>
    public static int GetLog2SquaredGain(int log2TrafoSize, int bitDepth) => 30 - (2 * bitDepth) - (2 * log2TrafoSize);

    /// <summary>
    /// Transforms the residual samples of a transform block into scaled transform coefficients <c>d</c>, the coefficients
    /// that the transformation process (clause 8.6.4) and Equation 8-299 turn back into the residual, up to their rounding.
    /// </summary>
    /// <param name="residual">The residual <c>r</c>: <c>nTbS</c> rows of <c>nTbS</c> samples, each smaller than 2^12 in magnitude.</param>
    /// <param name="coefficients">The scaled transform coefficients, in the layout of the decoder: the coefficient <c>( x, y )</c> is the value <c>x</c> of the row <c>y</c>.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="trType">The transform type: 1 for the discrete sine transform of a 4x4 block, otherwise 0.</param>
    /// <param name="bitDepth">The bit depth of the samples: 8 to 12.</param>
    /// <remarks>
    /// The rows of the residual are transformed into intermediate values, rounded to <c>IntermediateBits</c> bits
    /// (<see cref="GetBasisBits"/>), whose columns are transformed into the coefficients. The discrete cosine transforms
    /// use the even and odd decomposition of their bases: the basis of an even coefficient <c>k</c> is symmetric and the
    /// one of an odd coefficient antisymmetric, and the even coefficients of <c>nTbS</c> samples are the coefficients of
    /// the transform of <c>nTbS / 2</c> samples of the sums of the pairs of samples <c>i</c> and <c>nTbS - 1 - i</c>,
    /// exactly (the bases of the even coefficients are those of the smaller transform). The sums are those of the direct
    /// products (<see cref="ForwardScalar"/>), in another order.
    /// </remarks>
    public static void Forward(ReadOnlySpan<int> residual, Span<int> coefficients, int log2TrafoSize, int trType, int bitDepth)
    {
        Debug.Assert(log2TrafoSize is >= 2 and <= 5);
        Debug.Assert(trType is 0 || log2TrafoSize is 2);
        Debug.Assert(bitDepth is >= 8 and <= 12);
        if (HevcSimd.IsSupported)
        {
            ForwardVector(residual, coefficients, log2TrafoSize, trType, bitDepth);
            return;
        }

        ForwardScalar(residual, coefficients, log2TrafoSize, trType, bitDepth);
    }

    /// <summary>
    /// The transform of <see cref="Forward"/> as two direct products by the bases, one value at a time: the reference of the
    /// vector path, which gives the same coefficients.
    /// </summary>
    [SkipLocalsInit]
    internal static void ForwardScalar(ReadOnlySpan<int> residual, Span<int> coefficients, int log2TrafoSize, int trType, int bitDepth)
    {
        var nTbS = 1 << log2TrafoSize;
        var rowBasis = GetRowBasis(log2TrafoSize, trType);
        var columnBasis = GetColumnBasis(log2TrafoSize, trType);
        var rowShift = GetRowShift(log2TrafoSize, bitDepth);

        // The rows: the horizontal coefficient k of each row of the residual. Every value is written before it is read
        Span<int> intermediate = unsafe(stackalloc int[nTbS * nTbS]);
        for (var y = 0; y < nTbS; y++)
        {
            var row = residual.Slice(y * nTbS, nTbS);
            for (var k = 0; k < nTbS; k++)
            {
                var sum = 0;
                var weights = rowBasis.Slice(k * nTbS, nTbS);
                for (var i = 0; i < nTbS; i++)
                {
                    Debug.Assert(Math.Abs(row[i]) < 1 << 12);
                    sum += row[i] * weights[i];
                }

                intermediate[(y * nTbS) + k] = Round(sum, rowShift);
            }
        }

        // The columns, then the clipping to the range of the coefficients
        for (var k = 0; k < nTbS; k++)
        {
            for (var j = 0; j < nTbS; j++)
            {
                var sum = 0;
                var weights = columnBasis.Slice(j * nTbS, nTbS);
                for (var y = 0; y < nTbS; y++)
                {
                    sum += intermediate[(y * nTbS) + k] * weights[y];
                }

                coefficients[(j * nTbS) + k] = Math.Clamp(Round(sum, ColumnShift), HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax);
            }
        }
    }

    // The vector path: a 4x4 block in registers (Forward4x4); otherwise 4 rows of the residual at a time, whose blocks of
    // 4x4 samples are transposed so that the lanes of a vector are rows, then 4 columns of the intermediate values at a time
    [SkipLocalsInit]
    private static void ForwardVector(ReadOnlySpan<int> residual, Span<int> coefficients, int log2TrafoSize, int trType, int bitDepth)
    {
        if (log2TrafoSize is 2)
        {
            Forward4x4(residual, coefficients, trType, bitDepth);
            return;
        }

        var nTbS = 1 << log2TrafoSize;
        var size = nTbS * nTbS;
        residual = residual[..size];
        coefficients = coefficients[..size];
        var rowBasis = GetRowBasis(log2TrafoSize, trType: 0);
        var columnBasis = GetColumnBasis(log2TrafoSize, trType: 0);
        var rowShift = GetRowShift(log2TrafoSize, bitDepth);
        Span<int> intermediate = unsafe(stackalloc int[32 * 32])[..size];
        Span<Vector128<int>> samples = unsafe(stackalloc Vector128<int>[32])[..nTbS];
        Span<Vector128<int>> outputs = unsafe(stackalloc Vector128<int>[32])[..nTbS];
        Span<Vector128<int>> odd = unsafe(stackalloc Vector128<int>[16]);

        // The rows: samples[ i ] has the sample i of the rows y to y + 3, and outputs[ k ] their coefficient k
        for (var y = 0; y < nTbS; y += 4)
        {
            for (var x = 0; x < nTbS; x += 4)
            {
                var r0 = Vector128.Create(residual[((y * nTbS) + x)..]);
                var r1 = Vector128.Create(residual[(((y + 1) * nTbS) + x)..]);
                var r2 = Vector128.Create(residual[(((y + 2) * nTbS) + x)..]);
                var r3 = Vector128.Create(residual[(((y + 3) * nTbS) + x)..]);
                HevcSimd.Transpose4x4(ref r0, ref r1, ref r2, ref r3);
                samples[x] = r0;
                samples[x + 1] = r1;
                samples[x + 2] = r2;
                samples[x + 3] = r3;
            }

            Transform1D(samples, outputs, odd, log2TrafoSize, rowBasis);
            for (var k = 0; k < nTbS; k += 4)
            {
                var o0 = Round(outputs[k], rowShift);
                var o1 = Round(outputs[k + 1], rowShift);
                var o2 = Round(outputs[k + 2], rowShift);
                var o3 = Round(outputs[k + 3], rowShift);
                HevcSimd.Transpose4x4(ref o0, ref o1, ref o2, ref o3);
                o0.CopyTo(intermediate[((y * nTbS) + k)..]);
                o1.CopyTo(intermediate[(((y + 1) * nTbS) + k)..]);
                o2.CopyTo(intermediate[(((y + 2) * nTbS) + k)..]);
                o3.CopyTo(intermediate[(((y + 3) * nTbS) + k)..]);
            }
        }

        // The columns: samples[ y ] has the intermediate values k to k + 3 of the row y, and outputs[ j ] their coefficients
        // of the row j, clipped to the range of the coefficients
        var min = Vector128.Create(HevcInverseTransform.CoeffMin);
        var max = Vector128.Create(HevcInverseTransform.CoeffMax);
        for (var k = 0; k < nTbS; k += 4)
        {
            for (var y = 0; y < nTbS; y++)
            {
                samples[y] = Vector128.Create(intermediate[((y * nTbS) + k)..]);
            }

            Transform1D(samples, outputs, odd, log2TrafoSize, columnBasis);
            for (var j = 0; j < nTbS; j++)
            {
                Vector128.Min(Vector128.Max(Round(outputs[j], ColumnShift), min), max).CopyTo(coefficients[((j * nTbS) + k)..]);
            }
        }
    }

    // The transform of a 4x4 block in 4 vectors: its columns (the transposed rows), then the transposed intermediate values
    private static void Forward4x4(ReadOnlySpan<int> residual, Span<int> coefficients, int trType, int bitDepth)
    {
        var x0 = Vector128.Create(residual);
        var x1 = Vector128.Create(residual[4..]);
        var x2 = Vector128.Create(residual[8..]);
        var x3 = Vector128.Create(residual[12..]);
        HevcSimd.Transpose4x4(ref x0, ref x1, ref x2, ref x3);
        Transform4(ref x0, ref x1, ref x2, ref x3, GetRowBasis(2, trType), trType);
        var rowShift = GetRowShift(2, bitDepth);
        x0 = Round(x0, rowShift);
        x1 = Round(x1, rowShift);
        x2 = Round(x2, rowShift);
        x3 = Round(x3, rowShift);
        HevcSimd.Transpose4x4(ref x0, ref x1, ref x2, ref x3);
        Transform4(ref x0, ref x1, ref x2, ref x3, GetColumnBasis(2, trType), trType);
        var min = Vector128.Create(HevcInverseTransform.CoeffMin);
        var max = Vector128.Create(HevcInverseTransform.CoeffMax);
        Vector128.Min(Vector128.Max(Round(x0, ColumnShift), min), max).CopyTo(coefficients);
        Vector128.Min(Vector128.Max(Round(x1, ColumnShift), min), max).CopyTo(coefficients[4..]);
        Vector128.Min(Vector128.Max(Round(x2, ColumnShift), min), max).CopyTo(coefficients[8..]);
        Vector128.Min(Vector128.Max(Round(x3, ColumnShift), min), max).CopyTo(coefficients[12..]);
    }

    // The transforms of 4 samples of the lanes of 4 vectors, which are replaced by their coefficients
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Transform4(ref Vector128<int> x0, ref Vector128<int> x1, ref Vector128<int> x2, ref Vector128<int> x3, ReadOnlySpan<int> basis, int trType)
    {
        basis = basis[..16];
        if (trType is 1)
        {
            var w0 = Vector128.Create(basis);
            var w1 = Vector128.Create(basis[4..]);
            var w2 = Vector128.Create(basis[8..]);
            var w3 = Vector128.Create(basis[12..]);
            (x0, x1, x2, x3) = (Dot4(x0, x1, x2, x3, w0), Dot4(x0, x1, x2, x3, w1), Dot4(x0, x1, x2, x3, w2), Dot4(x0, x1, x2, x3, w3));
            return;
        }

        // The bases of the coefficients 0 and 2 are ( a, a, a, a ) and ( a, -a, -a, a )
        var sum0 = x0 + x3;
        var sum1 = x1 + x2;
        var difference0 = x0 - x3;
        var difference1 = x1 - x2;
        var scale = basis[0];
        x0 = (sum0 + sum1) * scale;
        x2 = (sum0 - sum1) * scale;
        x1 = MultiplyAdd(difference0 * basis[4], difference1, basis[5]);
        x3 = MultiplyAdd(difference0 * basis[12], difference1, basis[13]);
    }

    // The sum of the products of 4 vectors by the 4 lanes of weights
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Dot4(Vector128<int> x0, Vector128<int> x1, Vector128<int> x2, Vector128<int> x3, Vector128<int> weights)
    {
        if (AdvSimd.IsSupported)
        {
            var sum = AdvSimd.MultiplyBySelectedScalar(x0, weights, 0);
            sum = AdvSimd.MultiplyAddBySelectedScalar(sum, x1, weights, 1);
            sum = AdvSimd.MultiplyAddBySelectedScalar(sum, x2, weights, 2);
            return AdvSimd.MultiplyAddBySelectedScalar(sum, x3, weights, 3);
        }

        return (x0 * weights.GetElement(0)) + (x1 * weights.GetElement(1)) + (x2 * weights.GetElement(2)) + (x3 * weights.GetElement(3));
    }

    // The transforms of nTbS samples of 4 lanes: the products of the samples by the bases, with the even and odd
    // decomposition of the discrete cosine transform. samples is overwritten
    private static void Transform1D(Span<Vector128<int>> samples, Span<Vector128<int>> outputs, Span<Vector128<int>> odd, int log2TrafoSize, ReadOnlySpan<int> basis)
    {
        // The transform of n samples, from nTbS down to 4: the sums of the pairs of samples replace the first half of the
        // samples, and their differences give the odd coefficients of the transform of n samples, which are the
        // coefficients ( 2 * m + 1 ) * nTbS / n of the block
        var nTbS = 1 << log2TrafoSize;
        var level = 0;
        for (var n = nTbS; n > 2; n >>= 1, level++)
        {
            var half = n >> 1;
            var differences = odd[..half];
            for (var i = 0; i < differences.Length; i++)
            {
                var first = samples[i];
                var last = samples[n - 1 - i];
                samples[i] = first + last;
                differences[i] = first - last;
            }

            // 4 products at a time, whose weights are a vector, except for the transform of 4 samples
            ref var difference = ref unsafe(MemoryMarshal.GetReference(differences));
            for (var m = 0; m < half; m++)
            {
                var k = ((2 * m) + 1) << level;
                var weights = basis.Slice(k * nTbS, half);
                Vector128<int> sum;
                if (half is 2)
                {
                    sum = MultiplyAdd(differences[0] * weights[0], differences[1], weights[1]);
                }
                else
                {
                    sum = MultiplyAdd4(Vector128<int>.Zero, ref difference, Vector128.Create(weights));
                    for (var i = 4; i < half; i += 4)
                    {
                        sum = MultiplyAdd4(sum, ref unsafe(Unsafe.Add(ref difference, i)), Vector128.Create(weights[i..]));
                    }
                }

                outputs[k] = sum;
            }
        }

        // The transform of 2 samples: the coefficients 0 and nTbS / 2, whose bases are ( a, a ) and ( a, -a )
        var scale = basis[0];
        Debug.Assert(basis[nTbS / 2 * nTbS] == scale);
        outputs[0] = (samples[0] + samples[1]) * scale;
        outputs[nTbS / 2] = (samples[0] - samples[1]) * scale;
    }

    // sum + ( value * weight ): the multiply-accumulate instruction of Arm64 (MLA), a multiplication and an addition elsewhere
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> MultiplyAdd(Vector128<int> sum, Vector128<int> value, int weight)
    {
        if (AdvSimd.IsSupported)
            return AdvSimd.MultiplyAdd(sum, value, Vector128.Create(weight));

        return sum + (value * weight);
    }

    // sum + the products of 4 consecutive vectors of values by the 4 lanes of weights: the multiply-accumulate instruction
    // by a lane of a vector on Arm64
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> MultiplyAdd4(Vector128<int> sum, ref Vector128<int> values, Vector128<int> weights)
    {
        var values1 = unsafe(Unsafe.Add(ref values, 1));
        var values2 = unsafe(Unsafe.Add(ref values, 2));
        var values3 = unsafe(Unsafe.Add(ref values, 3));
        if (AdvSimd.IsSupported)
        {
            sum = AdvSimd.MultiplyAddBySelectedScalar(sum, values, weights, 0);
            sum = AdvSimd.MultiplyAddBySelectedScalar(sum, values1, weights, 1);
            sum = AdvSimd.MultiplyAddBySelectedScalar(sum, values2, weights, 2);
            return AdvSimd.MultiplyAddBySelectedScalar(sum, values3, weights, 3);
        }

        return sum + (values * weights.GetElement(0)) + (values1 * weights.GetElement(1)) + (values2 * weights.GetElement(2)) + (values3 * weights.GetElement(3));
    }

    /// <summary>
    /// The transform of <see cref="Forward"/> as the exact product by the inverse of the matrix of the decoder, with one
    /// rounding (sums of 64-bit products of bases with <c>ExactBasisBits</c> fractional bits): the reference that the
    /// two stages of <see cref="Forward"/> approach.
    /// </summary>
    internal static void ForwardExact(ReadOnlySpan<int> residual, Span<int> coefficients, int log2TrafoSize, int trType, int bitDepth)
    {
        var nTbS = 1 << log2TrafoSize;
        var basis = GetExactBasis(log2TrafoSize, trType);

        // The rows: the horizontal coefficient k of each row of the residual. The sums are exact: a residual sample has at
        // most 16 bits, a value of a basis 20 bits, and a row at most 32 samples
        var intermediate = new long[nTbS * nTbS];
        for (var y = 0; y < nTbS; y++)
        {
            var row = residual.Slice(y * nTbS, nTbS);
            for (var k = 0; k < nTbS; k++)
            {
                long sum = 0;
                for (var i = 0; i < nTbS; i++)
                {
                    sum += (long)row[i] * basis[(k * nTbS) + i];
                }

                intermediate[(y * nTbS) + k] = sum;
            }
        }

        // The columns, then the scale: the bases have ExactBasisBits fractional bits and a factor 64 * 64 * nTbS each, and
        // the inverse process divides by 2^( 27 - bitDepth )
        var shift = (2 * ExactBasisBits) + (2 * log2TrafoSize) + bitDepth - 3;
        for (var k = 0; k < nTbS; k++)
        {
            for (var j = 0; j < nTbS; j++)
            {
                long sum = 0;
                for (var y = 0; y < nTbS; y++)
                {
                    sum += intermediate[(y * nTbS) + k] * basis[(j * nTbS) + y];
                }

                coefficients[(j * nTbS) + k] = (int)Math.Clamp(Round(sum, shift), HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax);
            }
        }
    }

    /// <summary>
    /// Returns the fractional bits of the bases of the rows and of the columns of a transform size, and the bits of the
    /// magnitude of the intermediate values.
    /// </summary>
    /// <remarks>
    /// A residual sample is smaller than 2^12 in magnitude, and the sum of the magnitudes of the values of a basis is close
    /// to <c>64 * nTbS</c> times 2 to the power of its fractional bits (the matrices are nearly orthogonal): with
    /// <c>12 - Log2( nTbS )</c> fractional bits, a sum of the first stage is smaller than 2^30. Its rounding by
    /// <c>2^( bitDepth + 18 - IntermediateBits )</c> (<see cref="GetRowShift"/>) leaves an intermediate value smaller than
    /// 2^IntermediateBits, and the bases of the columns have <c>24 - Log2( nTbS ) - IntermediateBits</c> fractional bits, so
    /// that a sum of the second stage is also smaller than 2^30; it is then divided by 2^15. The tests check both bounds on
    /// the bases. The intermediate bits are those that make the coefficients closest to the exact product (measured on
    /// random and extreme residuals): the error of the rounding of the intermediate values against the one of the bases of
    /// the columns.
    /// </remarks>
    internal static (int RowBasisBits, int ColumnBasisBits, int IntermediateBits) GetBasisBits(int log2TrafoSize)
    {
        var intermediateBits = GetIntermediateBits(log2TrafoSize);
        return (12 - log2TrafoSize, 24 - log2TrafoSize - intermediateBits, intermediateBits);
    }

    /// <summary>Returns the shift of the sums of the first stage of <see cref="Forward"/>.</summary>
    internal static int GetRowShift(int log2TrafoSize, int bitDepth) => bitDepth + 18 - GetIntermediateBits(log2TrafoSize);

    private static int GetIntermediateBits(int log2TrafoSize) => log2TrafoSize <= 3 ? 15 : 14;

    /// <summary>Returns the bases of the first stage of <see cref="Forward"/>: the value of the coefficient <c>k</c> at the sample <c>i</c> is at <c>( k * nTbS ) + i</c>.</summary>
    internal static ReadOnlySpan<int> GetRowBasis(int log2TrafoSize, int trType) => trType is 1 ? DstRowBasis : RowBases[log2TrafoSize - 2];

    /// <summary>Returns the bases of the second stage of <see cref="Forward"/>.</summary>
    internal static ReadOnlySpan<int> GetColumnBasis(int log2TrafoSize, int trType) => trType is 1 ? DstColumnBasis : ColumnBases[log2TrafoSize - 2];

    /// <summary>Returns the bases of <see cref="ForwardExact"/>.</summary>
    internal static ReadOnlySpan<int> GetExactBasis(int log2TrafoSize, int trType) => trType is 1 ? ExactBases.Dst : ExactBases.Dct[log2TrafoSize - 2];

    /// <summary>
    /// Scales the residual samples of a transform block that skips the transform into scaled transform coefficients
    /// <c>d</c> (the inverse of Equations 8-298 and 8-299): the decoder multiplies a coefficient by
    /// <c>2^( 5 + Log2( nTbS ) )</c> and divides it by <c>2^( 20 - bitDepth )</c>.
    /// </summary>
    /// <param name="residual">The residual: <c>nTbS</c> rows of <c>nTbS</c> samples.</param>
    /// <param name="coefficients">The scaled transform coefficients.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    /// <param name="rotate"><c>rotateCoeffs</c>: the block is a 4x4 block of an intra coding unit and <c>transform_skip_rotation_enabled_flag</c> is 1.</param>
    public static void ForwardTransformSkip(ReadOnlySpan<int> residual, Span<int> coefficients, int log2TrafoSize, int bitDepth, bool rotate)
    {
        Debug.Assert(log2TrafoSize is >= 2 and <= 5);
        Debug.Assert(!rotate || log2TrafoSize is 2);
        var count = 1 << (log2TrafoSize << 1);
        var shift = 15 - bitDepth - log2TrafoSize;
        for (var i = 0; i < count; i++)
        {
            var value = shift >= 0 ? (long)residual[i] << shift : Round(residual[i], -shift);
            coefficients[i] = (int)Math.Clamp(value, HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax);
        }

        if (rotate)
        {
            Rotate(coefficients[..count]);
        }
    }

    /// <summary>
    /// Copies the residual samples of a transform block of a coding unit that bypasses the scaling and the transform
    /// (<c>cu_transquant_bypass_flag</c>) into transform coefficient levels: the residual itself (clause 8.6.2), rotated
    /// when <paramref name="rotate"/> is set (Equation 8-297).
    /// </summary>
    public static void ForwardBypass(ReadOnlySpan<int> residual, Span<int> levels, bool rotate)
    {
        residual.CopyTo(levels);
        if (rotate)
        {
            Rotate(levels[..residual.Length]);
        }
    }

    /// <summary>
    /// Rotates a block by half a turn: the value <c>( x, y )</c> becomes the value <c>( nTbS - x - 1, nTbS - y - 1 )</c>
    /// (Equations 8-297 and 8-298, <c>rotateCoeffs</c>). The rotation is its own inverse.
    /// </summary>
    public static void Rotate(Span<int> block) => block.Reverse();

    /// <summary>
    /// Replaces the residual samples of a block by their differences with the sample before them in the direction of the
    /// prediction: the inverse of the residual modification process for blocks using a transform bypass (clause 8.6.5),
    /// which accumulates them. It is exact, which is what a block that bypasses the scaling and the transform needs; the
    /// differences of a block that skips the transform are those of its reconstructed residual, which the quantization
    /// decides.
    /// </summary>
    /// <param name="residual">The residual: <c>nTbS</c> rows of <c>nTbS</c> samples.</param>
    /// <param name="nTbS">The size of the block.</param>
    /// <param name="vertical">Whether the samples are accumulated from the top (the vertical direction); otherwise from the left.</param>
    public static void DifferenceResidual(Span<int> residual, int nTbS, bool vertical)
    {
        if (vertical)
        {
            for (var i = residual.Length - 1; i >= nTbS; i--)
            {
                residual[i] -= residual[i - nTbS];
            }
        }
        else
        {
            for (var y = 0; y < nTbS; y++)
            {
                var row = residual.Slice(y * nTbS, nTbS);
                for (var x = nTbS - 1; x > 0; x--)
                {
                    row[x] -= row[x - 1];
                }
            }
        }
    }


    // To the nearest integer, halves away from zero: the transform of the opposite of a residual is the opposite of its
    // transform. For a negative value, ( value + half - 1 ) >> bits is the opposite of ( -value + half ) >> bits
    private static int Round(int value, int bits) => (value + (1 << (bits - 1)) + (value >> 31)) >> bits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> Round(Vector128<int> value, int bits) => (value + Vector128.Create(1 << (bits - 1)) + (value >> 31)) >> bits;

    private static long Round(long value, int bits)
    {
        var half = 1L << (bits - 1);
        return value >= 0 ? (value + half) >> bits : -((-value + half) >> bits);
    }

    // The forward bases of a transform, rounded to each number of fractional bits: the inverse of the matrix of the kernel
    // of the decoder, scaled by 64 * 64 * nTbS, the squared norm of a basis function of an orthogonal kernel
    internal static int[][] CreateBases(int log2TrafoSize, int trType, params ReadOnlySpan<int> fractionalBits)
    {
        var nTbS = 1 << log2TrafoSize;

        // The kernel of the decoder: the response to the coefficient j alone is the row j of the matrix. Each row of the
        // work matrix ( M | I ) has 2 * nTbS values
        var width = 2 * nTbS;
        var matrix = new BigInteger[nTbS * width];
        Span<int> x = stackalloc int[32];
        Span<int> response = stackalloc int[32];
        for (var j = 0; j < nTbS; j++)
        {
            x.Clear();
            x[j] = 1;
            HevcInverseTransform.Transform1D(x[..nTbS], response[..nTbS], log2TrafoSize, trType);
            for (var i = 0; i < nTbS; i++)
            {
                matrix[(j * width) + i] = response[i];
            }

            matrix[(j * width) + nTbS + j] = BigInteger.One;
        }

        // Gauss-Jordan elimination on the rows of ( M | I ), in exact arithmetic: a row is multiplied instead of divided,
        // then divided by the greatest common divisor of its values, which keeps them small
        for (var k = 0; k < nTbS; k++)
        {
            var pivotRow = k;
            while (matrix[(pivotRow * width) + k].IsZero)
            {
                pivotRow++;
            }

            if (pivotRow != k)
            {
                for (var i = 0; i < width; i++)
                {
                    (matrix[(k * width) + i], matrix[(pivotRow * width) + i]) = (matrix[(pivotRow * width) + i], matrix[(k * width) + i]);
                }
            }

            var pivot = matrix[(k * width) + k];
            for (var row = 0; row < nTbS; row++)
            {
                var factor = matrix[(row * width) + k];
                if (row == k || factor.IsZero)
                    continue;

                var divisor = BigInteger.Zero;
                for (var i = 0; i < width; i++)
                {
                    var value = (matrix[(row * width) + i] * pivot) - (matrix[(k * width) + i] * factor);
                    matrix[(row * width) + i] = value;
                    divisor = BigInteger.GreatestCommonDivisor(divisor, value);
                }

                if (!divisor.IsOne && !divisor.IsZero)
                {
                    for (var i = 0; i < width; i++)
                    {
                        matrix[(row * width) + i] /= divisor;
                    }
                }
            }
        }

        // The row y of the right half divided by the value y of the diagonal is the row y of the inverse G; the forward
        // basis of the coefficient j is the column j of G, rounded to the nearest integer, halves away from zero, which keeps
        // the symmetry of the bases
        var bases = new int[fractionalBits.Length][];
        for (var b = 0; b < fractionalBits.Length; b++)
        {
            var basis = new int[nTbS * nTbS];
            for (var y = 0; y < nTbS; y++)
            {
                var denominator = matrix[(y * width) + y];
                var magnitude = BigInteger.Abs(denominator);
                var half = magnitude >> 1;
                for (var j = 0; j < nTbS; j++)
                {
                    var numerator = matrix[(y * width) + nTbS + j] << (12 + log2TrafoSize + fractionalBits[b]);
                    if (denominator.Sign < 0)
                    {
                        numerator = -numerator;
                    }

                    basis[(j * nTbS) + y] = (int)((numerator + (numerator.Sign < 0 ? -half : half)) / magnitude);
                }
            }

            bases[b] = basis;
        }

        return bases;
    }

    // The bases of ForwardExact, which only the tests use: they are created on their first use
    private static class ExactBases
    {
        public static readonly int[][] Dct = [CreateBases(2, 0, ExactBasisBits)[0], CreateBases(3, 0, ExactBasisBits)[0], CreateBases(4, 0, ExactBasisBits)[0], CreateBases(5, 0, ExactBasisBits)[0]];
        public static readonly int[] Dst = CreateBases(2, 1, ExactBasisBits)[0];
    }
}
