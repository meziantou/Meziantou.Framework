using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The forward transforms of the VarDCT mode: from the samples of a block to the coefficients that
/// <see cref="JpegXlInverseTransform"/> turns back into these samples, in the layout of the format.
/// </summary>
/// <remarks>
/// The bases are not written out: they are measured on the inverse transforms of the decoder (the samples each coefficient
/// produces, divided by their energy, since the basis functions of a DCT are orthogonal), so that the two directions
/// cannot disagree. Every operation is a float32 one in a fixed order.
/// </remarks>
internal static class JpegXlForwardTransform
{
    /// <summary>The largest side of a transform the encoder uses, in samples.</summary>
    public const int MaxSize = 64;

    // For a transform of N samples, at log2(N): for each coefficient k, at k * N + n, the weight of the sample n
    private static readonly float[]?[] Bases = new float[]?[7];

    // For a transform of H by W blocks, at log2(H) * 4 + log2(W): the matrix that gives the LF samples of its blocks from
    // its lowest frequencies, in rows of H * W values
    private static readonly float[]?[] LowFrequencyMatrices = new float[]?[16];

    /// <summary>The DCT of a block of <paramref name="rows"/> by <paramref name="columns"/> samples.</summary>
    /// <param name="samples">The samples, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows of <paramref name="samples"/>.</param>
    /// <param name="rows">The number of rows of samples: a power of two, at most <see cref="MaxSize"/>.</param>
    /// <param name="columns">The number of columns of samples: a power of two, at most <see cref="MaxSize"/>.</param>
    /// <param name="coefficients">Receives the coefficients, in the layout of the format: the smaller side as rows, transposed when the block is not wider than it is high.</param>
    /// <param name="scratch">Scratch memory of <paramref name="rows"/> times <paramref name="columns"/> values.</param>
    public static void ForwardDct(ReadOnlySpan<float> samples, int stride, int rows, int columns, Span<float> coefficients, Span<float> scratch)
    {
        // Four lines at once, one per lane, each with the operations of the scalar code in the same order
        if (Vector128.IsHardwareAccelerated && rows >= 4 && columns >= 4)
        {
            ForwardDctVector(samples, stride, rows, columns, coefficients, scratch);
            return;
        }

        ForwardDctScalar(samples, stride, rows, columns, coefficients, scratch);
    }

    /// <summary>
    /// The DCT of a block whose sides are multiples of 4: the rows of samples are transformed four at a time (transposed
    /// 4x4 values at a time on the way in and out), then the columns of horizontal frequencies four at a time.
    /// </summary>
    /// <remarks>Every element of the stack buffers is written before it is read.</remarks>
    [System.Runtime.CompilerServices.SkipLocalsInit]
    private static void ForwardDctVector(ReadOnlySpan<float> samples, int stride, int rows, int columns, Span<float> coefficients, Span<float> scratch)
    {
        var columnBasis = GetBasis(columns);
        var rowBasis = GetBasis(rows);
        Span<Vector128<float>> lines = unsafe(stackalloc Vector128<float>[MaxSize]);
        Span<Vector128<float>> even = unsafe(stackalloc Vector128<float>[MaxSize / 2]);
        Span<Vector128<float>> odd = unsafe(stackalloc Vector128<float>[MaxSize / 2]);

        // The horizontal frequencies of each row of samples, for four rows at a time
        var horizontal = scratch[..(rows * columns)];
        var halfColumns = columns / 2;
        var line = lines[..columns];
        for (var y = 0; y < rows; y += 4)
        {
            for (var x = 0; x < columns; x += 4)
            {
                JpegXlInverseTransform.Transpose(
                    Vector128.Create(samples.Slice((y * stride) + x, 4)),
                    Vector128.Create(samples.Slice(((y + 1) * stride) + x, 4)),
                    Vector128.Create(samples.Slice(((y + 2) * stride) + x, 4)),
                    Vector128.Create(samples.Slice(((y + 3) * stride) + x, 4)),
                    out var r0,
                    out var r1,
                    out var r2,
                    out var r3);
                line[x] = r0;
                line[x + 1] = r1;
                line[x + 2] = r2;
                line[x + 3] = r3;
            }

            for (var x = 0; x < halfColumns; x++)
            {
                even[x] = line[x] + line[columns - 1 - x];
                odd[x] = line[x] - line[columns - 1 - x];
            }

            for (var u = 0; u < columns; u += 4)
            {
                Sum(even, odd, columnBasis.AsSpan(u * columns, 4 * columns), columns, halfColumns, out var s0, out var s1, out var s2, out var s3);
                JpegXlInverseTransform.Transpose(s0, s1, s2, s3, out var r0, out var r1, out var r2, out var r3);
                r0.CopyTo(horizontal.Slice((y * columns) + u, 4));
                r1.CopyTo(horizontal.Slice(((y + 1) * columns) + u, 4));
                r2.CopyTo(horizontal.Slice(((y + 2) * columns) + u, 4));
                r3.CopyTo(horizontal.Slice(((y + 3) * columns) + u, 4));
            }
        }

        // Then the vertical frequencies of each column of horizontal frequencies, for four columns at a time
        var isWide = rows < columns;
        var halfRows = rows / 2;
        for (var u = 0; u < columns; u += 4)
        {
            for (var y = 0; y < halfRows; y++)
            {
                var first = Vector128.Create(horizontal.Slice((y * columns) + u, 4));
                var last = Vector128.Create(horizontal.Slice(((rows - 1 - y) * columns) + u, 4));
                even[y] = first + last;
                odd[y] = first - last;
            }

            for (var v = 0; v < rows; v += 4)
            {
                Sum(even, odd, rowBasis.AsSpan(v * rows, 4 * rows), rows, halfRows, out var s0, out var s1, out var s2, out var s3);
                if (isWide)
                {
                    s0.CopyTo(coefficients.Slice((v * columns) + u, 4));
                    s1.CopyTo(coefficients.Slice(((v + 1) * columns) + u, 4));
                    s2.CopyTo(coefficients.Slice(((v + 2) * columns) + u, 4));
                    s3.CopyTo(coefficients.Slice(((v + 3) * columns) + u, 4));
                }
                else
                {
                    JpegXlInverseTransform.Transpose(s0, s1, s2, s3, out var r0, out var r1, out var r2, out var r3);
                    r0.CopyTo(coefficients.Slice((u * rows) + v, 4));
                    r1.CopyTo(coefficients.Slice(((u + 1) * rows) + v, 4));
                    r2.CopyTo(coefficients.Slice(((u + 2) * rows) + v, 4));
                    r3.CopyTo(coefficients.Slice(((u + 3) * rows) + v, 4));
                }
            }
        }

        // For four consecutive coefficients (the first one of even rank), the sums of the products of the folded values by
        // the first half of their basis functions, each from the first product: four independent sums
        static void Sum(ReadOnlySpan<Vector128<float>> even, ReadOnlySpan<Vector128<float>> odd, ReadOnlySpan<float> bases, int count, int half, out Vector128<float> s0, out Vector128<float> s1, out Vector128<float> s2, out Vector128<float> s3)
        {
            even = even[..half];
            odd = odd[..half];
            var basis0 = bases.Slice(0, half);
            var basis1 = bases.Slice(count, half);
            var basis2 = bases.Slice(2 * count, half);
            var basis3 = bases.Slice(3 * count, half);
            s0 = Vector128<float>.Zero;
            s1 = Vector128<float>.Zero;
            s2 = Vector128<float>.Zero;
            s3 = Vector128<float>.Zero;
            for (var i = 0; i < half; i++)
            {
                s0 += even[i] * Vector128.Create(basis0[i]);
                s1 += odd[i] * Vector128.Create(basis1[i]);
                s2 += even[i] * Vector128.Create(basis2[i]);
                s3 += odd[i] * Vector128.Create(basis3[i]);
            }
        }
    }

    /// <summary>The scalar reference of <see cref="ForwardDct"/>.</summary>
    public static void ForwardDctScalar(ReadOnlySpan<float> samples, int stride, int rows, int columns, Span<float> coefficients, Span<float> scratch)
    {
        var columnBasis = GetBasis(columns);
        var rowBasis = GetBasis(rows);

        // A basis function of even rank is symmetric about the middle of the block, one of odd rank antisymmetric: the
        // samples are folded once, and each coefficient is a sum over half of them
        Span<float> even = stackalloc float[MaxSize / 2];
        Span<float> odd = stackalloc float[MaxSize / 2];

        // The horizontal frequencies of each row of samples
        var horizontal = scratch[..(rows * columns)];
        var halfColumns = columns / 2;
        for (var y = 0; y < rows; y++)
        {
            var source = samples.Slice(y * stride, columns);
            var target = horizontal.Slice(y * columns, columns);
            for (var x = 0; x < halfColumns; x++)
            {
                even[x] = source[x] + source[columns - 1 - x];
                odd[x] = source[x] - source[columns - 1 - x];
            }

            for (var u = 0; u < columns; u++)
            {
                var basis = columnBasis.AsSpan(u * columns, halfColumns);
                var folded = (u & 1) == 0 ? even : odd;
                var sum = 0f;
                for (var x = 0; x < basis.Length; x++)
                {
                    sum += folded[x] * basis[x];
                }

                target[u] = sum;
            }
        }

        // Then the vertical frequencies of each column of horizontal frequencies
        var isWide = rows < columns;
        var halfRows = rows / 2;
        for (var u = 0; u < columns; u++)
        {
            for (var y = 0; y < halfRows; y++)
            {
                var first = horizontal[(y * columns) + u];
                var last = horizontal[((rows - 1 - y) * columns) + u];
                even[y] = first + last;
                odd[y] = first - last;
            }

            for (var v = 0; v < rows; v++)
            {
                var basis = rowBasis.AsSpan(v * rows, halfRows);
                var folded = (v & 1) == 0 ? even : odd;
                var sum = 0f;
                for (var y = 0; y < basis.Length; y++)
                {
                    sum += folded[y] * basis[y];
                }

                coefficients[isWide ? (v * columns) + u : (u * rows) + v] = sum;
            }
        }
    }

    /// <summary>
    /// Computes the LF samples of the blocks of a transform from its lowest frequencies: the samples from which
    /// <see cref="JpegXlInverseTransform.SetLowestFrequencies"/> computes these coefficients again.
    /// </summary>
    /// <param name="coefficients">The coefficients of the transform, in the layout of the format.</param>
    /// <param name="blocksHigh">The height of the transform, in blocks.</param>
    /// <param name="blocksWide">The width of the transform, in blocks.</param>
    /// <param name="lowFrequency">Receives the LF sample of each block, row by row.</param>
    public static void GetLowFrequencies(ReadOnlySpan<float> coefficients, int blocksHigh, int blocksWide, Span<float> lowFrequency)
    {
        if (blocksHigh == 1 && blocksWide == 1)
        {
            lowFrequency[0] = coefficients[0];
            return;
        }

        var count = blocksHigh * blocksWide;
        var index = (System.Numerics.BitOperations.Log2((uint)blocksHigh) * 4) + System.Numerics.BitOperations.Log2((uint)blocksWide);
        var matrix = LowFrequencyMatrices[index] ?? CreateLowFrequencyMatrix(blocksHigh, blocksWide, index);

        // The lowest frequencies, in the order of the coefficients that SetLowestFrequencies writes
        Span<float> lowest = stackalloc float[count];
        var stride = Math.Max(blocksHigh, blocksWide) * 8;
        var isTransposed = blocksHigh >= blocksWide;
        for (var v = 0; v < blocksHigh; v++)
        {
            for (var u = 0; u < blocksWide; u++)
            {
                lowest[(v * blocksWide) + u] = coefficients[isTransposed ? (u * stride) + v : (v * stride) + u];
            }
        }

        for (var i = 0; i < count; i++)
        {
            var row = matrix.AsSpan(i * count, count);
            var sum = 0f;
            for (var j = 0; j < count; j++)
            {
                sum += row[j] * lowest[j];
            }

            lowFrequency[i] = sum;
        }
    }

    private static float[] CreateLowFrequencyMatrix(int blocksHigh, int blocksWide, int index)
    {
        // The coefficients that each LF sample alone gives are a column of the matrix of the decoder, which is inverted
        var count = blocksHigh * blocksWide;
        var stride = Math.Max(blocksHigh, blocksWide) * 8;
        var isTransposed = blocksHigh >= blocksWide;
        var forward = new double[count * count];
        var samples = new float[count];
        var coefficients = new float[blocksHigh * blocksWide * 64];
        var scratch = new float[JpegXlInverseTransform.ScratchLength];
        for (var sample = 0; sample < count; sample++)
        {
            Array.Clear(samples);
            samples[sample] = 1;
            JpegXlInverseTransform.SetLowestFrequencies(samples, blocksWide, blocksHigh, blocksWide, coefficients, scratch);
            for (var v = 0; v < blocksHigh; v++)
            {
                for (var u = 0; u < blocksWide; u++)
                {
                    forward[(((v * blocksWide) + u) * count) + sample] = coefficients[isTransposed ? (u * stride) + v : (v * stride) + u];
                }
            }
        }

        // Gauss-Jordan elimination with partial pivoting: the matrix is a scaled DCT, far from singular
        var inverse = new double[count * count];
        for (var i = 0; i < count; i++)
        {
            inverse[(i * count) + i] = 1;
        }

        for (var column = 0; column < count; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < count; row++)
            {
                if (Math.Abs(forward[(row * count) + column]) > Math.Abs(forward[(pivot * count) + column]))
                {
                    pivot = row;
                }
            }

            if (pivot != column)
            {
                for (var j = 0; j < count; j++)
                {
                    (forward[(column * count) + j], forward[(pivot * count) + j]) = (forward[(pivot * count) + j], forward[(column * count) + j]);
                    (inverse[(column * count) + j], inverse[(pivot * count) + j]) = (inverse[(pivot * count) + j], inverse[(column * count) + j]);
                }
            }

            var scale = 1 / forward[(column * count) + column];
            for (var j = 0; j < count; j++)
            {
                forward[(column * count) + j] *= scale;
                inverse[(column * count) + j] *= scale;
            }

            for (var row = 0; row < count; row++)
            {
                if (row == column)
                    continue;

                var factor = forward[(row * count) + column];
                if (factor == 0)
                    continue;

                for (var j = 0; j < count; j++)
                {
                    forward[(row * count) + j] -= factor * forward[(column * count) + j];
                    inverse[(row * count) + j] -= factor * inverse[(column * count) + j];
                }
            }
        }

        var matrix = new float[count * count];
        for (var i = 0; i < matrix.Length; i++)
        {
            matrix[i] = (float)inverse[i];
        }

        // Two threads may compute the same table: either one is kept
        LowFrequencyMatrices[index] = matrix;
        return matrix;
    }

    private static float[] GetBasis(int count)
    {
        var index = System.Numerics.BitOperations.Log2((uint)count);
        return Bases[index] ?? CreateBasis(count, index);
    }

    private static float[] CreateBasis(int count, int index)
    {
        // The samples that the coefficient k alone produces are the k-th basis function of the inverse transform
        var basis = new float[count * count];
        Span<float> values = stackalloc float[count];
        Span<float> scratch = stackalloc float[count];
        for (var k = 0; k < count; k++)
        {
            values.Clear();
            values[k] = 1;
            JpegXlInverseTransform.InverseDct(values, scratch);
            var energy = 0.0;
            foreach (var value in values)
            {
                energy += (double)value * value;
            }

            for (var n = 0; n < count; n++)
            {
                basis[(k * count) + n] = (float)(values[n] / energy);
            }
        }

        // Two threads may compute the same table: either one is kept
        Bases[index] = basis;
        return basis;
    }
}
