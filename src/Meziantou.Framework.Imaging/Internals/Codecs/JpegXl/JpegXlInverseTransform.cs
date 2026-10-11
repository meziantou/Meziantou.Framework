namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The inverse transforms of the VarDCT mode (ISO/IEC 18181-1, section I.2.3): from the dequantized coefficients of a
/// transform to its samples. The DCT of the format maps a constant block to its value: the forward transform divides by
/// the number of samples, the inverse one is <c>x[n] = X[0] + sqrt(2) * sum(X[k] * cos(pi * k * (n + 1/2) / N))</c>.
/// </summary>
/// <remarks>
/// <para>
/// Layout of the coefficients: a block has the smaller side of its transform as rows. When the transform is wider than it
/// is high, a row holds the horizontal frequencies of one vertical frequency; otherwise (square transforms included) the
/// block is transposed, and a row holds the vertical frequencies of one horizontal frequency.
/// </para>
/// <para>
/// The one-dimensional inverse DCT is computed by halving: the even coefficients are the inverse DCT of half the length,
/// and the odd ones become one after replacing each by its sum with the previous odd one, divided by twice the cosine of
/// the sample position. Every operation is a float32 one in a fixed order, so that the result does not depend on the
/// machine.
/// </para>
/// </remarks>
internal static class JpegXlInverseTransform
{
    /// <summary>The largest side of a transform, in samples.</summary>
    public const int MaxSize = 256;

    /// <summary>The number of float values of scratch memory the transforms of at most <see cref="MaxSize"/> samples per side need.</summary>
    public const int ScratchLength = 4 * MaxSize;

    // For a transform of N samples, at N / 2 + i: 1 / (2 * cos((i + 1/2) * pi / N))
    private static readonly float[] HalfSecants = CreateHalfSecants();

    // For each number of blocks N (1 to 32), at N + k: 1 / (cos(k * pi / 16N) * cos(k * pi / 8N) * cos(k * pi / 4N)), the
    // factor that turns the coefficient k of a DCT of N values into the coefficient k of a DCT of 8N samples
    private static readonly float[] LowFrequencyScales = CreateLowFrequencyScales();

    // For each number of values N (1 to 32), at N + (k * N) + n: cos(pi * k * (n + 1/2) / N) * (k == 0 ? 1 : sqrt(2)) / N
    private static readonly float[][] ForwardBasis = CreateForwardBasis();

    /// <summary>Computes the samples of a block from its coefficients, for every transform type.</summary>
    /// <param name="transform">The transform type.</param>
    /// <param name="coefficients">The coefficients, in the layout of the format; they are overwritten.</param>
    /// <param name="output">Receives the samples, in rows of <paramref name="stride"/> values.</param>
    /// <param name="stride">The distance between two rows of <paramref name="output"/>.</param>
    /// <param name="scratch">Scratch memory of <see cref="ScratchLength"/> values.</param>
    public static void Apply(int transform, Span<float> coefficients, Span<float> output, int stride, Span<float> scratch)
    {
        switch (transform)
        {
            case JpegXlVarDctTables.TransformIdentity:
                ApplyIdentity(coefficients, output, stride);
                break;

            case JpegXlVarDctTables.TransformDct2:
                ApplyDct2(coefficients, output, stride);
                break;

            case JpegXlVarDctTables.TransformDct4:
                ApplyDct4(coefficients, output, stride, scratch);
                break;

            case JpegXlVarDctTables.TransformDct4x8:
            case JpegXlVarDctTables.TransformDct8x4:
                ApplyHalves(coefficients, output, stride, scratch, isWide: transform == JpegXlVarDctTables.TransformDct4x8);
                break;

            case >= JpegXlVarDctTables.TransformAfv0 and <= JpegXlVarDctTables.TransformAfv3:
                ApplyAfv(transform - JpegXlVarDctTables.TransformAfv0, coefficients, output, stride, scratch);
                break;

            default:
                InverseDct(coefficients, JpegXlVarDctTables.BlocksHigh[transform] * 8, JpegXlVarDctTables.BlocksWide[transform] * 8, output, stride, scratch);
                break;
        }
    }

    /// <summary>The inverse DCT of a block of <paramref name="rows"/> by <paramref name="columns"/> samples.</summary>
    /// <param name="coefficients">The coefficients, in the layout of the format; they are overwritten.</param>
    /// <param name="rows">The number of rows of samples.</param>
    /// <param name="columns">The number of columns of samples.</param>
    /// <param name="output">Receives the samples.</param>
    /// <param name="stride">The distance between two rows of <paramref name="output"/>.</param>
    /// <param name="scratch">Scratch memory of at least four times the larger side.</param>
    public static void InverseDct(Span<float> coefficients, int rows, int columns, Span<float> output, int stride, Span<float> scratch)
    {
        var line = scratch[..Math.Max(rows, columns)];
        var work = scratch.Slice(Math.Max(rows, columns), Math.Max(rows, columns));
        if (rows < columns)
        {
            // Rows of horizontal frequencies: each row becomes samples, then each column of vertical frequencies
            for (var v = 0; v < rows; v++)
            {
                InverseDct(coefficients.Slice(v * columns, columns), work);
            }

            var column = line[..rows];
            for (var x = 0; x < columns; x++)
            {
                for (var v = 0; v < rows; v++)
                {
                    column[v] = coefficients[(v * columns) + x];
                }

                InverseDct(column, work);
                for (var y = 0; y < rows; y++)
                {
                    output[(y * stride) + x] = column[y];
                }
            }
        }
        else
        {
            // Transposed: rows of vertical frequencies, which become the samples of a column for one horizontal frequency
            for (var u = 0; u < columns; u++)
            {
                InverseDct(coefficients.Slice(u * rows, rows), work);
            }

            var row = line[..columns];
            for (var y = 0; y < rows; y++)
            {
                for (var u = 0; u < columns; u++)
                {
                    row[u] = coefficients[(u * rows) + y];
                }

                InverseDct(row, work);
                row.CopyTo(output.Slice(y * stride, columns));
            }
        }
    }

    /// <summary>The one-dimensional inverse DCT of a power-of-two number of values, in place.</summary>
    /// <param name="values">The coefficients, replaced with the samples.</param>
    /// <param name="scratch">Scratch memory of the same length.</param>
    public static void InverseDct(Span<float> values, Span<float> scratch)
    {
        var count = values.Length;
        if (count == 1)
            return;

        if (count == 2)
        {
            var sum = values[0] + values[1];
            values[1] = values[0] - values[1];
            values[0] = sum;
            return;
        }

        var half = count / 2;
        var even = scratch[..half];
        var odd = scratch.Slice(half, half);
        for (var i = 0; i < half; i++)
        {
            even[i] = values[2 * i];
        }

        odd[0] = MathF.Sqrt(2) * values[1];
        for (var i = 1; i < half; i++)
        {
            odd[i] = values[(2 * i) - 1] + values[(2 * i) + 1];
        }

        InverseDct(even, values[..half]);
        InverseDct(odd, values.Slice(half, half));
        var secants = HalfSecants.AsSpan(half, half);
        for (var i = 0; i < half; i++)
        {
            var scaled = odd[i] * secants[i];
            values[i] = even[i] + scaled;
            values[count - 1 - i] = even[i] - scaled;
        }
    }

    /// <summary>
    /// Computes the lowest frequencies of a transform of several blocks from the LF samples of its blocks, one per block
    /// of 8x8 samples: the DCT of these samples, each coefficient scaled to the coefficient of the same frequency of the
    /// DCT of all the samples.
    /// </summary>
    /// <param name="lowFrequency">The LF samples, starting at the first block of the transform.</param>
    /// <param name="lowFrequencyStride">The distance between two rows of <paramref name="lowFrequency"/>.</param>
    /// <param name="blocksHigh">The height of the transform, in blocks.</param>
    /// <param name="blocksWide">The width of the transform, in blocks.</param>
    /// <param name="coefficients">The coefficients of the transform, in the layout of the format; the lowest frequencies are written.</param>
    /// <param name="scratch">Scratch memory of <see cref="ScratchLength"/> values.</param>
    public static void SetLowestFrequencies(ReadOnlySpan<float> lowFrequency, int lowFrequencyStride, int blocksHigh, int blocksWide, Span<float> coefficients, Span<float> scratch)
    {
        var stride = Math.Max(blocksHigh, blocksWide) * 8;
        if (blocksHigh == 1 && blocksWide == 1)
        {
            coefficients[0] = lowFrequency[0];
            return;
        }

        // The DCT of each row, then of each column
        var rowBasis = ForwardBasis[blocksWide];
        var columnBasis = ForwardBasis[blocksHigh];
        var rowScales = LowFrequencyScales.AsSpan(blocksWide, blocksWide);
        var columnScales = LowFrequencyScales.AsSpan(blocksHigh, blocksHigh);
        var horizontal = scratch[..(blocksHigh * blocksWide)];
        for (var y = 0; y < blocksHigh; y++)
        {
            var source = lowFrequency.Slice(y * lowFrequencyStride, blocksWide);
            for (var u = 0; u < blocksWide; u++)
            {
                var basis = rowBasis.AsSpan(u * blocksWide, blocksWide);
                var sum = 0f;
                for (var x = 0; x < blocksWide; x++)
                {
                    sum += source[x] * basis[x];
                }

                horizontal[(y * blocksWide) + u] = sum;
            }
        }

        var isTransposed = blocksHigh >= blocksWide;
        for (var v = 0; v < blocksHigh; v++)
        {
            var basis = columnBasis.AsSpan(v * blocksHigh, blocksHigh);
            for (var u = 0; u < blocksWide; u++)
            {
                var sum = 0f;
                for (var y = 0; y < blocksHigh; y++)
                {
                    sum += horizontal[(y * blocksWide) + u] * basis[y];
                }

                var value = sum * columnScales[v] * rowScales[u];
                coefficients[isTransposed ? (u * stride) + v : (v * stride) + u] = value;
            }
        }
    }

    /// <summary>
    /// The transform that stores samples: the block is four blocks of 4x4 samples; each stores the differences of its
    /// samples with the sample of its second row and second column, which is its mean minus the mean of the differences.
    /// </summary>
    private static void ApplyIdentity(ReadOnlySpan<float> coefficients, Span<float> output, int stride)
    {
        Span<float> means = stackalloc float[4];
        SplitLowestFrequency(coefficients, means);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                var sum = 0f;
                for (var iy = 0; iy < 4; iy++)
                {
                    for (var ix = 0; ix < 4; ix++)
                    {
                        if (ix != 0 || iy != 0)
                        {
                            sum += coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)];
                        }
                    }
                }

                var reference = means[(y * 2) + x] - (sum * (1f / 16));
                for (var iy = 0; iy < 4; iy++)
                {
                    for (var ix = 0; ix < 4; ix++)
                    {
                        output[(((y * 4) + iy) * stride) + (x * 4) + ix] = coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)] + reference;
                    }
                }

                // The first position holds the mean: the difference of the first sample is stored where the reference would be
                output[(((y * 4) + 1) * stride) + (x * 4) + 1] = reference;
                output[(y * 4 * stride) + (x * 4)] = coefficients[((y + 2) * 8) + x + 2] + reference;
            }
        }
    }

    /// <summary>Three nested transforms of 2x2 values: of the first 2x2 coefficients, then of the first 4x4, then of all of them.</summary>
    private static void ApplyDct2(Span<float> coefficients, Span<float> output, int stride)
    {
        Span<float> work = stackalloc float[64];
        for (var size = 2; size <= 8; size *= 2)
        {
            var half = size / 2;
            for (var y = 0; y < half; y++)
            {
                for (var x = 0; x < half; x++)
                {
                    var c00 = coefficients[(y * 8) + x];
                    var c01 = coefficients[(y * 8) + half + x];
                    var c10 = coefficients[((y + half) * 8) + x];
                    var c11 = coefficients[((y + half) * 8) + half + x];
                    work[(y * 2 * 8) + (x * 2)] = c00 + c01 + c10 + c11;
                    work[(y * 2 * 8) + (x * 2) + 1] = c00 + c01 - c10 - c11;
                    work[(((y * 2) + 1) * 8) + (x * 2)] = c00 - c01 + c10 - c11;
                    work[(((y * 2) + 1) * 8) + (x * 2) + 1] = c00 - c01 - c10 + c11;
                }
            }

            for (var y = 0; y < size; y++)
            {
                work.Slice(y * 8, size).CopyTo(coefficients.Slice(y * 8, size));
            }
        }

        for (var y = 0; y < 8; y++)
        {
            coefficients.Slice(y * 8, 8).CopyTo(output.Slice(y * stride, 8));
        }
    }

    /// <summary>Four DCTs of 4x4 samples, whose coefficients are interleaved; their lowest frequencies come from the first 2x2 coefficients.</summary>
    private static void ApplyDct4(ReadOnlySpan<float> coefficients, Span<float> output, int stride, Span<float> scratch)
    {
        Span<float> means = stackalloc float[4];
        Span<float> block = stackalloc float[16];
        SplitLowestFrequency(coefficients, means);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 2; x++)
            {
                for (var iy = 0; iy < 4; iy++)
                {
                    for (var ix = 0; ix < 4; ix++)
                    {
                        block[(iy * 4) + ix] = coefficients[((y + (iy * 2)) * 8) + x + (ix * 2)];
                    }
                }

                block[0] = means[(y * 2) + x];
                InverseDct(block, 4, 4, output[((y * 4 * stride) + (x * 4))..], stride, scratch);
            }
        }
    }

    /// <summary>Two DCTs of 4x8 samples, one above the other, or of 8x4 samples, side by side; their coefficients are in alternate rows.</summary>
    private static void ApplyHalves(ReadOnlySpan<float> coefficients, Span<float> output, int stride, Span<float> scratch, bool isWide)
    {
        Span<float> block = stackalloc float[32];
        var first = coefficients[0];
        var second = coefficients[8];
        for (var half = 0; half < 2; half++)
        {
            for (var iy = 0; iy < 4; iy++)
            {
                coefficients.Slice((half + (iy * 2)) * 8, 8).CopyTo(block.Slice(iy * 8, 8));
            }

            block[0] = half == 0 ? first + second : first - second;
            if (isWide)
            {
                InverseDct(block, 4, 8, output[(half * 4 * stride)..], stride, scratch);
            }
            else
            {
                InverseDct(block, 8, 4, output[(half * 4)..], stride, scratch);
            }
        }
    }

    /// <summary>
    /// The AFV transform: a corner of 4x4 samples has its own basis, the 4x4 samples beside it a DCT, and the other half
    /// of the block a DCT of 4x8 samples. The kind selects the corner.
    /// </summary>
    private static void ApplyAfv(int kind, ReadOnlySpan<float> coefficients, Span<float> output, int stride, Span<float> scratch)
    {
        var isRight = (kind & 1) != 0;
        var isBottom = (kind & 2) != 0;
        var c00 = coefficients[0];
        var c01 = coefficients[1];
        var c10 = coefficients[8];
        Span<float> block = stackalloc float[32];
        Span<float> samples = stackalloc float[16];

        // The corner: the coefficients at even rows and even columns
        var corner = block[..16];
        for (var iy = 0; iy < 4; iy++)
        {
            for (var ix = 0; ix < 4; ix++)
            {
                corner[(iy * 4) + ix] = coefficients[(iy * 2 * 8) + (ix * 2)];
            }
        }

        corner[0] = (c00 + c10 + c01) * 4;
        samples.Clear();
        var basis = JpegXlVarDctTables.AfvBasis;
        for (var j = 0; j < 16; j++)
        {
            var coefficient = corner[j];
            var row = basis.Slice(j * 16, 16);
            for (var i = 0; i < 16; i++)
            {
                samples[i] += coefficient * row[i];
            }
        }

        var cornerX = isRight ? 4 : 0;
        var cornerY = isBottom ? 4 : 0;
        for (var iy = 0; iy < 4; iy++)
        {
            for (var ix = 0; ix < 4; ix++)
            {
                output[((cornerY + iy) * stride) + cornerX + ix] = samples[((isBottom ? 3 - iy : iy) * 4) + (isRight ? 3 - ix : ix)];
            }
        }

        // The 4x4 samples beside the corner: the coefficients at even rows and odd columns
        for (var iy = 0; iy < 4; iy++)
        {
            for (var ix = 0; ix < 4; ix++)
            {
                corner[(iy * 4) + ix] = coefficients[(iy * 2 * 8) + (ix * 2) + 1];
            }
        }

        corner[0] = c00 + c10 - c01;
        InverseDct(corner, 4, 4, output[((cornerY * stride) + (isRight ? 0 : 4))..], stride, scratch);

        // The other half of the block: the coefficients of the odd rows
        for (var iy = 0; iy < 4; iy++)
        {
            coefficients.Slice((1 + (iy * 2)) * 8, 8).CopyTo(block.Slice(iy * 8, 8));
        }

        block[0] = c00 - c10;
        InverseDct(block, 4, 8, output[((isBottom ? 0 : 4) * stride)..], stride, scratch);
    }

    /// <summary>Gets the lowest frequency of each quarter of a block from its first 2x2 coefficients.</summary>
    private static void SplitLowestFrequency(ReadOnlySpan<float> coefficients, Span<float> means)
    {
        var c00 = coefficients[0];
        var c01 = coefficients[1];
        var c10 = coefficients[8];
        var c11 = coefficients[9];
        means[0] = c00 + c01 + c10 + c11;
        means[1] = c00 + c01 - c10 - c11;
        means[2] = c00 - c01 + c10 - c11;
        means[3] = c00 - c01 - c10 + c11;
    }

    private static float[] CreateHalfSecants()
    {
        var result = new float[MaxSize];
        for (var half = 1; half < MaxSize; half *= 2)
        {
            for (var i = 0; i < half; i++)
            {
                result[half + i] = (float)(1 / (2 * Math.Cos((i + 0.5) * Math.PI / (2 * half))));
            }
        }

        return result;
    }

    private static float[] CreateLowFrequencyScales()
    {
        var result = new float[64];
        for (var count = 1; count <= 32; count *= 2)
        {
            for (var k = 0; k < count; k++)
            {
                result[count + k] = (float)(1 / (Math.Cos(k * Math.PI / (16 * count)) * Math.Cos(k * Math.PI / (8 * count)) * Math.Cos(k * Math.PI / (4 * count))));
            }
        }

        return result;
    }

    private static float[][] CreateForwardBasis()
    {
        var result = new float[33][];
        for (var count = 1; count <= 32; count *= 2)
        {
            var basis = result[count] = new float[count * count];
            for (var k = 0; k < count; k++)
            {
                for (var n = 0; n < count; n++)
                {
                    basis[(k * count) + n] = (float)(Math.Cos(Math.PI * k * (n + 0.5) / count) * (k == 0 ? 1 : Math.Sqrt(2)) / count);
                }
            }
        }

        return result;
    }
}
