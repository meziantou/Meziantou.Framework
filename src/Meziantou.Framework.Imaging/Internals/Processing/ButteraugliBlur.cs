namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The blurs of <see cref="ButteraugliCalculator"/> (<c>Blur</c> in <c>lib/jxl/butteraugli/butteraugli.cc</c> of libjxl
/// 0.12.0): a Gaussian kernel truncated at 2.25 sigma, renormalized near the borders, or, for the 5-tap kernel, a separable
/// convolution that mirrors the plane at its borders. Large planes are split into row bands between up to
/// <see cref="ImageConfiguration.MaxDegreeOfParallelism"/> workers; every value is computed as by one worker.
/// </summary>
internal static class ButteraugliBlur
{
    private static readonly Dictionary<float, float[]> Kernels = [];
    private static readonly Lock KernelsLock = new();

    /// <summary>Blurs <paramref name="input"/> into <paramref name="output"/>, which can be the same plane.</summary>
    /// <exception cref="ImageResourceLimitException">The temporary plane exceeds the allocation limit.</exception>
    public static void Blur(FloatPlaneArena arena, FloatPlane input, float sigma, FloatPlane output, int maxDegreeOfParallelism)
    {
        var kernel = GetKernel(sigma);
        if (kernel.Length is 5 && input != output)
        {
            Separable5(arena, input, kernel, output, maxDegreeOfParallelism);
            return;
        }

        var transposed = arena.Rent(input.Height, input.Width);
        ConvolveAndTranspose(input, kernel, transposed, maxDegreeOfParallelism);
        ConvolveAndTranspose(transposed, kernel, output, maxDegreeOfParallelism);
        arena.Release(transposed);
    }

    private static float[] GetKernel(float sigma)
    {
        lock (KernelsLock)
        {
            if (!Kernels.TryGetValue(sigma, out var kernel))
            {
                const float M = 2.25f;
                var scaler = -1.0 / (2.0 * sigma * sigma);
                var diff = Math.Max(1, (int)(M * Math.Abs(sigma)));
                kernel = new float[2 * diff + 1];
                for (var i = -diff; i <= diff; i++)
                {
                    kernel[i + diff] = (float)Math.Exp(scaler * i * i);
                }

                Kernels.Add(sigma, kernel);
            }

            return kernel;
        }
    }

    /// <summary>Convolves the rows and writes the result transposed: <c>output[x, y]</c> is the convolution at <c>(x, y)</c>.</summary>
    private static void ConvolveAndTranspose(FloatPlane input, float[] kernel, FloatPlane output, int maxDegreeOfParallelism)
    {
        var width = input.Width;
        var height = input.Height;
        var offset = kernel.Length / 2;

        var weightNoBorder = 0f;
        foreach (var weight in kernel)
        {
            weightNoBorder += weight;
        }

        var scaleNoBorder = 1.0f / weightNoBorder;
        var scaledKernel = new float[offset + 1];
        for (var i = 0; i <= offset; i++)
        {
            scaledKernel[i] = kernel[i] * scaleNoBorder;
        }

        var border1 = Math.Min(width, offset);
        var border2 = width > offset ? width - offset : 0;
        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            ConvolveRows(input.Span, output.Span, width, height, scaledKernel, border1, border2, start, end);
            for (var x = 0; x < border1; x++)
            {
                ConvolveBorderColumn(input.Span, width, height, kernel, x, output.Span, start, end);
            }

            for (var x = border2; x < width; x++)
            {
                ConvolveBorderColumn(input.Span, width, height, kernel, x, output.Span, start, end);
            }
        });
    }

    /// <summary>Convolves the rows <c>[start, end)</c> between the border columns.</summary>
    private static void ConvolveRows(ReadOnlySpan<float> source, Span<float> destination, int width, int height, float[] scaledKernel, int border1, int border2, int start, int end)
    {
        var offset = scaledKernel.Length - 1;
        for (var y = start; y < end; y++)
        {
            var row = source.Slice(y * width, width);
            for (var x = border1; x < border2; x++)
            {
                // Four partial sums in the order of the unrolled loops of libjxl, then the center
                float sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
                for (var j = 0; j < offset; j++)
                {
                    var term = (row[x - offset + j] + row[x + offset - j]) * scaledKernel[j];
                    switch (j % 4)
                    {
                        case 0: sum0 += term; break;
                        case 1: sum1 += term; break;
                        case 2: sum2 += term; break;
                        default: sum3 += term; break;
                    }
                }

                destination[x * height + y] = row[x] * scaledKernel[offset] + sum0 + sum1 + sum2 + sum3;
            }
        }
    }

    /// <summary>Convolves column <paramref name="x"/> of the rows <c>[start, end)</c>, with the weights inside the plane.</summary>
    private static void ConvolveBorderColumn(ReadOnlySpan<float> source, int width, int height, float[] kernel, int x, Span<float> transposed, int start, int end)
    {
        var offset = kernel.Length / 2;
        var minX = Math.Max(0, x - offset);
        var maxX = Math.Min(width - 1, x + offset);
        var weight = 0f;
        for (var j = minX; j <= maxX; j++)
        {
            weight += kernel[j - x + offset];
        }

        var scale = 1.0f / weight;
        var destination = transposed.Slice(x * height, height);
        for (var y = start; y < end; y++)
        {
            var row = source.Slice(y * width, width);
            var sum = 0f;
            for (var j = minX; j <= maxX; j++)
            {
                sum += row[j] * kernel[j - x + offset];
            }

            destination[y] = sum * scale;
        }
    }

    /// <summary>The 5×5 separable convolution of libjxl (<c>Separable5</c>), which mirrors the plane at its borders.</summary>
    private static void Separable5(FloatPlaneArena arena, FloatPlane input, float[] kernel, FloatPlane output, int maxDegreeOfParallelism)
    {
        var width = input.Width;
        var height = input.Height;
        var sumWeights = 0f;
        foreach (var weight in kernel)
        {
            sumWeights += weight;
        }

        var scale = 1.0f / sumWeights;
        var w0 = kernel[2] * scale;
        var w1 = kernel[1] * scale;
        var w2 = kernel[0] * scale;

        var horizontal = arena.Rent(width, height);
        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            var source = input.Span;
            var rows = horizontal.Span;
            for (var y = start; y < end; y++)
            {
                var row = source.Slice(y * width, width);
                var destination = rows.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    destination[x] = w0 * row[x]
                        + w1 * (row[Mirror(x - 1, width)] + row[Mirror(x + 1, width)])
                        + w2 * (row[Mirror(x - 2, width)] + row[Mirror(x + 2, width)]);
                }
            }
        });

        RowBands.For(input.Size, maxDegreeOfParallelism, (start, end) =>
        {
            var rows = horizontal.Span;
            var result = output.Span;
            for (var y = start; y < end; y++)
            {
                var center = rows.Slice(y * width, width);
                var up1 = rows.Slice(Mirror(y - 1, height) * width, width);
                var down1 = rows.Slice(Mirror(y + 1, height) * width, width);
                var up2 = rows.Slice(Mirror(y - 2, height) * width, width);
                var down2 = rows.Slice(Mirror(y + 2, height) * width, width);
                var destination = result.Slice(y * width, width);
                for (var x = 0; x < width; x++)
                {
                    destination[x] = w0 * center[x] + w1 * (up1[x] + down1[x]) + w2 * (up2[x] + down2[x]);
                }
            }
        });

        arena.Release(horizontal);
    }

    /// <summary>Mirrors a coordinate into [0, size), repeating the edge sample (jxl::Mirror).</summary>
    private static int Mirror(int x, int size)
    {
        while (x < 0 || x >= size)
        {
            x = x < 0 ? -x - 1 : 2 * size - 1 - x;
        }

        return x;
    }
}
