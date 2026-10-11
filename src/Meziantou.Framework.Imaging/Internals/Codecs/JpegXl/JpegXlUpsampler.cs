namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The upsampling of a channel by 2, 4 or 8 (ISO/IEC 18181-1, section K.5): each sample becomes a square of samples,
/// each one a weighted sum of the 5x5 samples around the source, clamped to the range of these samples.
/// </summary>
/// <remarks>
/// The weights of a factor are the upper triangle of a symmetric matrix indexed by the output position in a quarter of
/// the square and the input position; the kernels of the other quarters are its mirror images. Samples outside the
/// channel are the mirror image of the samples inside, the edge sample included.
/// </remarks>
internal static class JpegXlUpsampler
{
    private const int KernelSide = 5;
    private const int KernelSize = KernelSide * KernelSide;

    /// <summary>Builds the kernels of a factor: for each output position of the square, in raster order, its 5x5 weights.</summary>
    /// <param name="factor">The factor: 2, 4 or 8.</param>
    /// <param name="transformData">The transform data of the image, which may hold custom weights.</param>
    public static float[] CreateKernels(int factor, JpegXlTransformData transformData)
    {
        ArgumentNullException.ThrowIfNull(transformData);
        var weights = factor switch
        {
            2 => transformData.Upsampling2Weights ?? JpegXlUpsamplingTables.Weights2,
            4 => transformData.Upsampling4Weights ?? JpegXlUpsamplingTables.Weights4,
            8 => transformData.Upsampling8Weights ?? JpegXlUpsamplingTables.Weights8,
            _ => throw new ArgumentOutOfRangeException(nameof(factor)),
        };

        var half = factor / 2;
        var kernels = new float[factor * factor * KernelSize];
        for (var ky = 0; ky < half; ky++)
        {
            for (var kx = 0; kx < half; kx++)
            {
                var topLeft = ((ky * factor) + kx) * KernelSize;
                var topRight = ((ky * factor) + (factor - 1 - kx)) * KernelSize;
                var bottomLeft = (((factor - 1 - ky) * factor) + kx) * KernelSize;
                var bottomRight = (((factor - 1 - ky) * factor) + (factor - 1 - kx)) * KernelSize;
                for (var py = 0; py < KernelSide; py++)
                {
                    for (var px = 0; px < KernelSide; px++)
                    {
                        var j = (KernelSide * ky) + py;
                        var i = (KernelSide * kx) + px;
                        var low = Math.Min(i, j);
                        var high = Math.Max(i, j);
                        var weight = weights[(KernelSide * half * low) - (low * (low - 1) / 2) + high - low];
                        kernels[topLeft + (py * KernelSide) + px] = weight;
                        kernels[topRight + (py * KernelSide) + (KernelSide - 1 - px)] = weight;
                        kernels[bottomLeft + ((KernelSide - 1 - py) * KernelSide) + px] = weight;
                        kernels[bottomRight + ((KernelSide - 1 - py) * KernelSide) + (KernelSide - 1 - px)] = weight;
                    }
                }
            }
        }

        return kernels;
    }

    /// <summary>Upsamples a channel.</summary>
    /// <param name="source">The samples, in rows of <paramref name="sourceStride"/> values.</param>
    /// <param name="sourceStride">The distance between two rows of <paramref name="source"/>.</param>
    /// <param name="sourceWidth">The width of the channel.</param>
    /// <param name="sourceHeight">The height of the channel.</param>
    /// <param name="factor">The factor: 2, 4 or 8.</param>
    /// <param name="kernels">The kernels of the factor (<see cref="CreateKernels"/>).</param>
    /// <param name="target">Receives the samples, in rows of <paramref name="targetWidth"/> values.</param>
    /// <param name="targetWidth">The width of the result: at most the width of the channel times the factor.</param>
    /// <param name="targetHeight">The height of the result: at most the height of the channel times the factor.</param>
    /// <param name="cancellationToken">The token checked between rows.</param>
    public static void Upsample(ReadOnlySpan<float> source, int sourceStride, int sourceWidth, int sourceHeight, int factor, ReadOnlySpan<float> kernels, Span<float> target, int targetWidth, int targetHeight, CancellationToken cancellationToken)
    {
        Span<float> window = stackalloc float[KernelSize];
        Span<int> rows = stackalloc int[KernelSide];
        Span<int> columns = stackalloc int[KernelSide];
        for (var y = 0; y < sourceHeight && y * factor < targetHeight; y++)
        {
            if ((y & 31) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            for (var i = 0; i < KernelSide; i++)
            {
                rows[i] = Mirror(y + i - 2, sourceHeight) * sourceStride;
            }

            for (var x = 0; x < sourceWidth && x * factor < targetWidth; x++)
            {
                for (var i = 0; i < KernelSide; i++)
                {
                    columns[i] = Mirror(x + i - 2, sourceWidth);
                }

                var min = float.MaxValue;
                var max = float.MinValue;
                for (var iy = 0; iy < KernelSide; iy++)
                {
                    for (var ix = 0; ix < KernelSide; ix++)
                    {
                        var value = source[rows[iy] + columns[ix]];
                        window[(iy * KernelSide) + ix] = value;
                        min = MathF.Min(min, value);
                        max = MathF.Max(max, value);
                    }
                }

                for (var oy = 0; oy < factor; oy++)
                {
                    var targetY = (y * factor) + oy;
                    if (targetY >= targetHeight)
                        break;

                    for (var ox = 0; ox < factor; ox++)
                    {
                        var targetX = (x * factor) + ox;
                        if (targetX >= targetWidth)
                            break;

                        // Three partial sums, of every third weight, added in a fixed order
                        var kernel = kernels.Slice(((oy * factor) + ox) * KernelSize, KernelSize);
                        var sum0 = window[0] * kernel[0];
                        var sum1 = window[1] * kernel[1];
                        var sum2 = window[2] * kernel[2];
                        for (var i = 3; i < 24; i += 3)
                        {
                            sum0 += window[i] * kernel[i];
                            sum1 += window[i + 1] * kernel[i + 1];
                            sum2 += window[i + 2] * kernel[i + 2];
                        }

                        sum0 += window[24] * kernel[24];
                        var result = (sum1 + sum2) + sum0;
                        target[(targetY * targetWidth) + targetX] = MathF.Min(max, MathF.Max(min, result));
                    }
                }
            }
        }
    }

    private static int Mirror(int position, int size)
    {
        while (position < 0 || position >= size)
        {
            position = position < 0 ? -position - 1 : (2 * size) - 1 - position;
        }

        return position;
    }
}
