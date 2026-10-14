using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

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
/// the two directions cannot disagree. The transform is then a product by the inverse and one rounding.
/// </para>
/// </remarks>
internal static class HevcForwardTransform
{
    // The fractional bits of the forward bases, which are close to the basis functions of the specification: their largest
    // values are near 90 * 2^BasisBits
    private const int BasisBits = 12;

    // DctBases[ log2TrafoSize - 2 ][ ( k * nTbS ) + i ]: the forward basis of the coefficient k at the sample i
    private static readonly int[][] DctBases = [CreateBasis(2, trType: 0), CreateBasis(3, trType: 0), CreateBasis(4, trType: 0), CreateBasis(5, trType: 0)];
    private static readonly int[] DstBasis = CreateBasis(2, trType: 1);

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
    /// <param name="residual">The residual <c>r</c>: <c>nTbS</c> rows of <c>nTbS</c> samples.</param>
    /// <param name="coefficients">The scaled transform coefficients, in the layout of the decoder: the coefficient <c>( x, y )</c> is the value <c>x</c> of the row <c>y</c>.</param>
    /// <param name="log2TrafoSize">The base 2 logarithm of <c>nTbS</c>: 2 to 5.</param>
    /// <param name="trType">The transform type: 1 for the discrete sine transform of a 4x4 block, otherwise 0.</param>
    /// <param name="bitDepth">The bit depth of the samples.</param>
    [SkipLocalsInit]
    public static void Forward(ReadOnlySpan<int> residual, Span<int> coefficients, int log2TrafoSize, int trType, int bitDepth)
    {
        Debug.Assert(log2TrafoSize is >= 2 and <= 5);
        Debug.Assert(trType is 0 || log2TrafoSize is 2);
        if (trType is 1)
        {
            ForwardFullMatrix(residual, coefficients, log2TrafoSize, DstBasis, bitDepth);
            return;
        }

        // The basis of an even coefficient k is symmetric and the one of an odd coefficient antisymmetric, exactly (the
        // tests check it): the products of the samples i and nTbS - 1 - i share their basis value, so that each half of
        // the coefficients is the product of half of the bases by the sums or by the differences of the pairs of samples.
        // The sums of products are the same integers as those of the full matrix products (ForwardFullMatrix).
        var nTbS = 1 << log2TrafoSize;
        var half = nTbS >> 1;
        ReadOnlySpan<int> basis = DctBases[log2TrafoSize - 2];

        // The rows: the horizontal coefficient k of each row of the residual. The sums are exact: a residual sample is the
        // difference of two samples of at most 16 bits, a value of a basis has 20 bits, and a row at most 32 samples. Every
        // value is written before it is read
        Span<long> intermediate = unsafe(stackalloc long[nTbS * nTbS]);
        Span<int> sums = unsafe(stackalloc int[half]);
        Span<int> differences = unsafe(stackalloc int[half]);
        for (var y = 0; y < nTbS; y++)
        {
            var row = residual.Slice(y * nTbS, nTbS);
            for (var i = 0; i < half; i++)
            {
                Debug.Assert(Math.Abs(row[i]) <= ushort.MaxValue && Math.Abs(row[nTbS - 1 - i]) <= ushort.MaxValue);
                sums[i] = row[i] + row[nTbS - 1 - i];
                differences[i] = row[i] - row[nTbS - 1 - i];
            }

            var output = intermediate.Slice(y * nTbS, nTbS);
            for (var k = 0; k < nTbS; k += 2)
            {
                output[k] = Dot(sums, basis.Slice(k * nTbS, half));
                output[k + 1] = Dot(differences, basis.Slice((k + 1) * nTbS, half));
            }
        }

        // The columns, then the scale: the bases have BasisBits fractional bits and a factor 64 * 64 * nTbS each, and the
        // inverse process divides by 2^( 27 - bitDepth )
        var shift = (2 * BasisBits) + (2 * log2TrafoSize) + bitDepth - 3;
        Span<long> columnSums = unsafe(stackalloc long[half]);
        Span<long> columnDifferences = unsafe(stackalloc long[half]);
        for (var k = 0; k < nTbS; k++)
        {
            for (var y = 0; y < half; y++)
            {
                var top = intermediate[(y * nTbS) + k];
                var bottom = intermediate[((nTbS - 1 - y) * nTbS) + k];
                columnSums[y] = top + bottom;
                columnDifferences[y] = top - bottom;
            }

            for (var j = 0; j < nTbS; j += 2)
            {
                coefficients[(j * nTbS) + k] = Scale(Dot(columnSums, basis.Slice(j * nTbS, half)), shift);
                coefficients[((j + 1) * nTbS) + k] = Scale(Dot(columnDifferences, basis.Slice((j + 1) * nTbS, half)), shift);
            }
        }
    }

    /// <summary>
    /// The transform of <see cref="Forward"/> as two full matrix products: the transform of the discrete sine transform,
    /// and the reference of the even and odd decomposition of the other ones.
    /// </summary>
    internal static void ForwardFullMatrix(ReadOnlySpan<int> residual, Span<int> coefficients, int log2TrafoSize, ReadOnlySpan<int> basis, int bitDepth)
    {
        var nTbS = 1 << log2TrafoSize;

        // The rows: the horizontal coefficient k of each row of the residual
        Span<long> intermediate = stackalloc long[nTbS * nTbS];
        for (var y = 0; y < nTbS; y++)
        {
            var row = residual.Slice(y * nTbS, nTbS);
            for (var k = 0; k < nTbS; k++)
            {
                intermediate[(y * nTbS) + k] = Dot(row, basis.Slice(k * nTbS, nTbS));
            }
        }

        // The columns, then the scale
        var shift = (2 * BasisBits) + (2 * log2TrafoSize) + bitDepth - 3;
        Span<long> column = stackalloc long[nTbS];
        for (var k = 0; k < nTbS; k++)
        {
            for (var y = 0; y < nTbS; y++)
            {
                column[y] = intermediate[(y * nTbS) + k];
            }

            for (var j = 0; j < nTbS; j++)
            {
                coefficients[(j * nTbS) + k] = Scale(Dot(column, basis.Slice(j * nTbS, nTbS)), shift);
            }
        }
    }

    /// <summary>Returns the forward basis of the discrete cosine transform of a size: the value of the coefficient <c>k</c> at the sample <c>i</c> is at <c>( k * nTbS ) + i</c>.</summary>
    internal static ReadOnlySpan<int> GetDctBasis(int log2TrafoSize) => DctBases[log2TrafoSize - 2];

    /// <summary>Returns the forward basis of the discrete sine transform of a 4x4 block.</summary>
    internal static ReadOnlySpan<int> GetDstBasis() => DstBasis;

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

    // The sums of products are exact: the order of the terms does not change them
    private static long Dot(ReadOnlySpan<int> samples, ReadOnlySpan<int> basis)
    {
        basis = basis[..samples.Length];
        long sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += (long)samples[i] * basis[i];
        }

        return sum;
    }

    private static long Dot(ReadOnlySpan<long> samples, ReadOnlySpan<int> basis)
    {
        basis = basis[..samples.Length];
        long sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * basis[i];
        }

        return sum;
    }

    private static int Scale(long sum, int shift) => (int)Math.Clamp(Round(sum, shift), HevcInverseTransform.CoeffMin, HevcInverseTransform.CoeffMax);

    // To the nearest integer, halves away from zero: the transform of the opposite of a residual is the opposite of its
    // transform
    private static long Round(long value, int bits)
    {
        var half = 1L << (bits - 1);
        return value >= 0 ? (value + half) >> bits : -((-value + half) >> bits);
    }

    // The forward basis of a transform: the inverse of the matrix of the kernel of the decoder, scaled by 64 * 64 * nTbS,
    // the squared norm of a basis function of an orthogonal kernel, with BasisBits fractional bits
    private static int[] CreateBasis(int log2TrafoSize, int trType)
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
        // basis of the coefficient j is the column j of G
        var basis = new int[nTbS * nTbS];
        for (var y = 0; y < nTbS; y++)
        {
            var denominator = matrix[(y * width) + y];
            var magnitude = BigInteger.Abs(denominator);
            var half = magnitude >> 1;
            for (var j = 0; j < nTbS; j++)
            {
                var numerator = matrix[(y * width) + nTbS + j] << (12 + log2TrafoSize + BasisBits);
                if (denominator.Sign < 0)
                {
                    numerator = -numerator;
                }

                basis[(j * nTbS) + y] = (int)((numerator + (numerator.Sign < 0 ? -half : half)) / magnitude);
            }
        }

        return basis;
    }
}
