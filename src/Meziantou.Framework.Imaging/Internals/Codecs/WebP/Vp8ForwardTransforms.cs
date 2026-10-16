using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The forward transforms of the VP8 encoder, matched to the scaling of the decoder's inverse transforms
/// (<see cref="Vp8Transforms"/>): VP8 coefficients are twice the orthonormal 4x4 DCT-II coefficients (a DC-only block decodes
/// to <c>dc / 8</c> per pixel), and the Y2 block is <c>M D M / 2</c> where <c>M</c> is the 4x4 Walsh-Hadamard matrix of the
/// inverse transform (<c>M M = 4 I</c>, the inverse computes <c>M Y2 M / 8</c>). Computed in fixed point (12-bit basis) and
/// rounded to nearest.
/// </summary>
internal static class Vp8ForwardTransforms
{
    // The DCT basis in fixed point with 12 fractional bits
    private const int BasisBits = 12;
    private static readonly int[] DctBasis = CreateDctBasis();

    // Whether the fixed-point basis has the symmetries of the DCT: (a, a, a, a), (b, c, -c, -b), (a, -a, -a, a), (c, -b, b, -c)
    private static readonly bool IsBasisSymmetric = DctBasis.AsSpan().SequenceEqual(
    [
        DctBasis[0], DctBasis[0], DctBasis[0], DctBasis[0],
        DctBasis[4], DctBasis[5], -DctBasis[5], -DctBasis[4],
        DctBasis[0], -DctBasis[0], -DctBasis[0], DctBasis[0],
        DctBasis[5], -DctBasis[4], DctBasis[4], -DctBasis[5],
    ]);

    private static ReadOnlySpan<sbyte> Hadamard => [1, 1, 1, 1, 1, 1, -1, -1, 1, -1, -1, 1, 1, -1, 1, -1];

    // Whether ForwardDctColumns can use its vector path: a symmetric basis whose first coefficient is 2^11
    private static readonly bool CanVectorize = IsBasisSymmetric && DctBasis[0] == 1 << (BasisBits - 1);

    /// <summary>Computes the VP8 DCT coefficients (raster order) of a 4x4 residual block given in column-major order.</summary>
    /// <param name="residualColumns">The 16 residuals, column by column: <c>residualColumns[(x * 4) + y]</c> is the residual of row <c>y</c>, column <c>x</c>.</param>
    /// <param name="coefficients">The 16 coefficients, raster order.</param>
    /// <remarks>
    /// The coefficients are the ones of <see cref="ForwardDct"/> (integer sums are exact, whatever their order). The vector
    /// path computes the transform of the rows on the columns (4 rows per vector), transposes once, then the transform of the
    /// columns 4 coefficients at a time. Its 64-bit products are avoided: <c>a = 2^11</c>, and <c>b x + c y</c> is split into
    /// <c>(b x_h + c y_h) 2^12 + (b x_l + c y_l)</c> with <c>x_h = x &gt;&gt; 12</c>, <c>x_l = x &amp; 4095</c>, whose rounding
    /// is computed with floor shifts (<c>floor((h 2^12 + m) / 2^23) = (h + (m &gt;&gt; 12)) &gt;&gt; 11</c> for any integers).
    /// </remarks>
    public static void ForwardDctColumns(ReadOnlySpan<int> residualColumns, Span<int> coefficients)
    {
        residualColumns = residualColumns[..16];
        coefficients = coefficients[..16];
        if (!CanVectorize || !Vector128.IsHardwareAccelerated)
        {
            Span<int> residual = stackalloc int[16];
            for (var i = 0; i < 16; i++)
            {
                residual[((i & 3) * 4) + (i >> 2)] = residualColumns[i];
            }

            ForwardDct(residual, coefficients);
            return;
        }

        var a = DctBasis[0];
        var b = DctBasis[4];
        var c = DctBasis[5];

        // Rows: the transform of every row at once, column vectors in, one vector per output index u (over the rows)
        var column0 = Vector128.Create(residualColumns[..4]);
        var column1 = Vector128.Create(residualColumns.Slice(4, 4));
        var column2 = Vector128.Create(residualColumns.Slice(8, 4));
        var column3 = Vector128.Create(residualColumns.Slice(12, 4));
        var outerSum = column0 + column3;
        var innerSum = column1 + column2;
        var outerDifference = column0 - column3;
        var innerDifference = column1 - column2;
        var t0 = (outerSum + innerSum) * a;
        var t1 = (outerDifference * b) + (innerDifference * c);
        var t2 = (outerSum - innerSum) * a;
        var t3 = (outerDifference * c) - (innerDifference * b);

        // Transposed: one vector per row y (over u), the temp rows of ForwardDct
        Vp8Transforms.Transpose4x4(ref t0, ref t1, ref t2, ref t3);

        // Columns: one vector per coefficient row v
        outerSum = t0 + t3;
        innerSum = t1 + t2;
        outerDifference = t0 - t3;
        innerDifference = t1 - t2;
        RoundTimesA(outerSum + innerSum).CopyTo(coefficients[..4]);
        RoundSum(outerDifference, innerDifference, b, c).CopyTo(coefficients.Slice(4, 4));
        RoundTimesA(outerSum - innerSum).CopyTo(coefficients.Slice(8, 4));
        RoundSum(outerDifference, innerDifference, c, -b).CopyTo(coefficients.Slice(12, 4));

        // Round(2^11 z): (|z| 2^11 + 2^22) >> 23 = (|z| + 2^11) >> 12, with the sign of z
        static Vector128<int> RoundTimesA(Vector128<int> z)
        {
            var magnitude = Vector128.ShiftRightLogical(Vector128.Abs(z) + Vector128.Create(1 << 11), 12);
            return Vector128.ConditionalSelect(Vector128.LessThan(z, Vector128<int>.Zero), -magnitude, magnitude);
        }

        // Round(p x + q y) for |x|, |y| < 2^22 and |p|, |q| < 2^12 (see the remarks)
        static Vector128<int> RoundSum(Vector128<int> x, Vector128<int> y, int p, int q)
        {
            var mask = Vector128.Create(4095);
            var high = (Vector128.ShiftRightArithmetic(x, 12) * p) + (Vector128.ShiftRightArithmetic(y, 12) * q);
            var low = ((x & mask) * p) + ((y & mask) * q);
            var half = Vector128.Create(1 << 22);
            var negative = Vector128.LessThan(high + Vector128.ShiftRightArithmetic(low, 12), Vector128<int>.Zero);
            var positive = Vector128.ShiftRightArithmetic(high + Vector128.ShiftRightArithmetic(low + half, 12), 11);
            var negated = -Vector128.ShiftRightArithmetic(-high + Vector128.ShiftRightArithmetic(half - low, 12), 11);
            return Vector128.ConditionalSelect(negative, negated, positive);
        }
    }

    /// <summary>Computes the VP8 DCT coefficients (raster order) of a 4x4 residual block.</summary>
    /// <param name="residual">The 16 residuals (source minus prediction), raster order.</param>
    /// <param name="coefficients">The 16 coefficients, raster order.</param>
    /// <remarks>
    /// The rows of the basis are <c>(a, a, a, a)</c>, <c>(b, c, -c, -b)</c>, <c>(a, -a, -a, a)</c> and <c>(c, -b, b, -c)</c>:
    /// the four products of a row or column are computed from the sums and differences of its outer and inner samples.
    /// Integer arithmetic is exact, so the coefficients are the ones of the plain matrix products
    /// (<see cref="ForwardDctReference"/>).
    /// </remarks>
    public static void ForwardDct(ReadOnlySpan<int> residual, Span<int> coefficients)
    {
        if (!IsBasisSymmetric)
        {
            ForwardDctReference(residual, coefficients);
            return;
        }

        residual = residual[..16];
        coefficients = coefficients[..16];
        var a = DctBasis[0];
        var b = DctBasis[4];
        var c = DctBasis[5];
        Span<int> temp = stackalloc int[16];

        // Rows, then columns: C R C^T
        for (var y = 0; y < 16; y += 4)
        {
            var outerSum = residual[y] + residual[y + 3];
            var innerSum = residual[y + 1] + residual[y + 2];
            var outerDifference = residual[y] - residual[y + 3];
            var innerDifference = residual[y + 1] - residual[y + 2];
            temp[y] = a * (outerSum + innerSum);
            temp[y + 1] = (b * outerDifference) + (c * innerDifference);
            temp[y + 2] = a * (outerSum - innerSum);
            temp[y + 3] = (c * outerDifference) - (b * innerDifference);
        }

        for (var u = 0; u < 4; u++)
        {
            var outerSum = (long)temp[u] + temp[12 + u];
            var innerSum = (long)temp[4 + u] + temp[8 + u];
            var outerDifference = (long)temp[u] - temp[12 + u];
            var innerDifference = (long)temp[4 + u] - temp[8 + u];
            coefficients[u] = Round(a * (outerSum + innerSum));
            coefficients[4 + u] = Round((b * outerDifference) + (c * innerDifference));
            coefficients[8 + u] = Round(a * (outerSum - innerSum));
            coefficients[12 + u] = Round((c * outerDifference) - (b * innerDifference));
        }
    }

    /// <summary>The reference of <see cref="ForwardDct"/>: the two matrix products, term by term.</summary>
    internal static void ForwardDctReference(ReadOnlySpan<int> residual, Span<int> coefficients)
    {
        var basis = DctBasis.AsSpan(0, 16);
        residual = residual[..16];
        coefficients = coefficients[..16];
        Span<int> temp = stackalloc int[16];

        // Rows, then columns: C R C^T
        for (var y = 0; y < 4; y++)
        {
            var r0 = residual[y * 4];
            var r1 = residual[(y * 4) + 1];
            var r2 = residual[(y * 4) + 2];
            var r3 = residual[(y * 4) + 3];
            for (var u = 0; u < 4; u++)
            {
                temp[(y * 4) + u] = (basis[u * 4] * r0) + (basis[(u * 4) + 1] * r1) + (basis[(u * 4) + 2] * r2) + (basis[(u * 4) + 3] * r3);
            }
        }

        for (var v = 0; v < 4; v++)
        {
            for (var u = 0; u < 4; u++)
            {
                var sum = ((long)basis[v * 4] * temp[u]) + ((long)basis[(v * 4) + 1] * temp[4 + u]) + ((long)basis[(v * 4) + 2] * temp[8 + u]) + ((long)basis[(v * 4) + 3] * temp[12 + u]);
                coefficients[(v * 4) + u] = Round(sum);
            }
        }
    }

    // The coefficients are twice the orthonormal ones, rounded to nearest (ties away from zero)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Round(long sum)
    {
        const int Shift = (2 * BasisBits) - 1;
        const long Half = 1L << (Shift - 1);
        return (int)(sum >= 0 ? (sum + Half) >> Shift : -((-sum + Half) >> Shift));
    }

    /// <summary>Computes the Y2 coefficients (raster order) from the 16 DC coefficients of the luma blocks (block raster order).</summary>
    public static void ForwardWalshHadamard(ReadOnlySpan<int> dc, Span<int> y2)
    {
        var m = Hadamard;
        Span<int> temp = stackalloc int[16];
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                var sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += m[(i * 4) + k] * dc[(k * 4) + j];
                }

                temp[(i * 4) + j] = sum;
            }
        }

        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                var sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += temp[(i * 4) + k] * m[(k * 4) + j];
                }

                // Halved with rounding to nearest, ties away from zero
                y2[(i * 4) + j] = sum >= 0 ? (sum + 1) >> 1 : -((-sum + 1) >> 1);
            }
        }
    }

    private static int[] CreateDctBasis()
    {
        var basis = new int[16];
        for (var u = 0; u < 4; u++)
        {
            var scale = u == 0 ? Math.Sqrt(0.25) : Math.Sqrt(0.5);
            for (var x = 0; x < 4; x++)
            {
                basis[(u * 4) + x] = (int)Math.Round(scale * Math.Cos((2 * x + 1) * u * Math.PI / 8) * (1 << BasisBits), MidpointRounding.AwayFromZero);
            }
        }

        return basis;
    }
}
