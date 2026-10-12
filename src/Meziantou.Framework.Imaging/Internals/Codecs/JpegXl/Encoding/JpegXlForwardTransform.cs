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

        // The horizontal frequencies of each row of samples
        var horizontal = scratch[..(rows * columns)];
        for (var y = 0; y < rows; y++)
        {
            var source = samples.Slice(y * stride, columns);
            var target = horizontal.Slice(y * columns, columns);
            for (var u = 0; u < columns; u++)
            {
                var basis = columnBasis.AsSpan(u * columns, columns);
                var sum = 0f;
                for (var x = 0; x < columns; x++)
                {
                    sum += source[x] * basis[x];
                }

                target[u] = sum;
            }
        }

        // Then the vertical frequencies of each column of horizontal frequencies
        var isWide = rows < columns;
        for (var v = 0; v < rows; v++)
        {
            var basis = rowBasis.AsSpan(v * rows, rows);
            for (var u = 0; u < columns; u++)
            {
                var sum = 0f;
                for (var y = 0; y < rows; y++)
                {
                    sum += horizontal[(y * columns) + u] * basis[y];
                }

                coefficients[isWide ? (v * columns) + u : (u * rows) + v] = sum;
            }
        }
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
