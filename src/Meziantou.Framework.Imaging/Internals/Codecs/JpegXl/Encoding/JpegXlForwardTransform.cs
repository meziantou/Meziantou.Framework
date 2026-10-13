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
