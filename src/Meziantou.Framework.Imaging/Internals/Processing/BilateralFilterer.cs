namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bilateral filter of <see cref="ImageProcessingExtensions.BilateralFilter(Image, int, double, double, ConvolutionEdgeMode, CancellationToken)"/>
/// (Tomasi and Manduchi, 1998): every pixel becomes the mean of the pixels of a disk around it, each weighted by a
/// Gaussian of its distance (the spatial weight) times a Gaussian of the difference between its color and the color of the
/// pixel (the range weight), so that the edges, across which colors differ, are not blurred.
/// </summary>
/// <remarks>
/// <para>
/// The disk holds the offsets <c>(dx, dy)</c> with <c>dx² + dy² ≤ radius²</c>, as OpenCV's <c>cv2.bilateralFilter</c>
/// does. The spatial weight of an offset is <c>exp(-(dx² + dy²) / (2 σs²))</c>. The difference of two pixels is the root
/// mean square of the differences of their colors, as fractions of the sample range (premultiplied by alpha), and of their
/// alphas: <c>δ² = Σ Δcolor² / colorCount + Δalpha²</c>, which is <c>|Δ|</c> for a gray image whatever the number of its
/// color samples. The range weight is <c>exp(-δ² / (2 σr²))</c>, read from a table of <c>exp(-t)</c> with 256 entries per
/// unit of <c>t</c> and linear interpolation (a relative error below 2e-6), and 0 beyond <c>t = 32</c>. The pixel itself has
/// the weight 1. A neighbor whose difference is not a number has the weight 0, and a pixel whose samples are not numbers
/// stays so.
/// </para>
/// <para>
/// The frame is read from its working copy (<see cref="WorkingFrameCopy"/>): large frames are split into row bands, one
/// per worker, and every pixel is computed alone, so the result does not depend on the number of workers. A row is
/// accumulated one row of the disk at a time, in the buffer of its worker: its output samples, then the sums of the
/// weights of its pixels.
/// </para>
/// </remarks>
internal static class BilateralFilterer
{
    /// <summary>The largest value of <c>t = δ² / (2 σr²)</c> with a range weight: <c>exp(-32)</c> is about 1.3e-14.</summary>
    private const int MaxExponent = 32;

    private const int EntriesPerUnit = 256;

    private static readonly double[] RangeWeights = CreateRangeWeights();

    /// <summary>Gets the number of doubles of the row buffer of a worker for a frame of <paramref name="width"/> pixels of <paramref name="channels"/> samples.</summary>
    public static int GetRowDoubles(int width, int channels) => checked(width * (channels + 1));

    /// <summary>Filters a leased frame of the format of the copy in place.</summary>
    /// <exception cref="OperationCanceledException">The token was canceled between two rows.</exception>
    public static void Apply(PixelStorage storage, WorkingFrameCopy copy, int radius, double spatialSigma, double rangeSigma, CancellationToken cancellationToken)
    {
        copy.Load(storage, cancellationToken);
        var spatialWeights = CreateSpatialWeights(radius, spatialSigma);
        var height = copy.Size.Height;
        var workers = copy.Workers;
        var rangeFactor = 1 / (2 * rangeSigma * rangeSigma);
        if (workers == 1)
        {
            FilterRows(storage, copy, radius, spatialWeights, rangeFactor, worker: 0, 0, height, cancellationToken);
            return;
        }

        RowBands.RunParallel(workers, band => FilterRows(storage, copy, radius, spatialWeights, rangeFactor, band, RowBands.GetBandStart(height, band, workers), RowBands.GetBandStart(height, band + 1, workers), cancellationToken));
    }

    /// <summary>Gets the range weight of <c>t = δ² / (2 σr²)</c>: <c>exp(-t)</c>, interpolated in the table, or 0 beyond the table or when <paramref name="t"/> is not a number.</summary>
    internal static double GetRangeWeight(double t)
    {
        if (!(t < MaxExponent))
            return 0;

        var position = t * EntriesPerUnit;
        var index = (int)position;
        var fraction = position - index;
        var low = RangeWeights[index];
        return low + (fraction * (RangeWeights[index + 1] - low));
    }

    /// <summary>Gets the spatial weights of the disk, row by row: entry <c>(dy + radius) × (2 radius + 1) + dx + radius</c>, 0 outside the disk.</summary>
    private static double[] CreateSpatialWeights(int radius, double spatialSigma)
    {
        var diameter = (2 * radius) + 1;
        var weights = new double[diameter * diameter];
        var factor = 1 / (2 * spatialSigma * spatialSigma);
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var distance = (dx * dx) + (dy * dy);
                if (distance <= radius * radius)
                {
                    weights[((dy + radius) * diameter) + dx + radius] = Math.Exp(-distance * factor);
                }
            }
        }

        return weights;
    }

    private static double[] CreateRangeWeights()
    {
        var weights = new double[(MaxExponent * EntriesPerUnit) + 2];
        for (var i = 0; i < weights.Length; i++)
        {
            weights[i] = Math.Exp(-(double)i / EntriesPerUnit);
        }

        return weights;
    }

    /// <summary>Gets the half-width of the row <paramref name="dy"/> of the disk.</summary>
    private static int GetHalfWidth(int radius, int dy)
    {
        var halfWidth = (int)Math.Sqrt((radius * radius) - (dy * dy));
        while ((halfWidth + 1) * (halfWidth + 1) + (dy * dy) <= radius * radius)
        {
            halfWidth++;
        }

        while (halfWidth > 0 && (halfWidth * halfWidth) + (dy * dy) > radius * radius)
        {
            halfWidth--;
        }

        return halfWidth;
    }

    private static void FilterRows(PixelStorage storage, WorkingFrameCopy copy, int radius, double[] spatialWeights, double rangeFactor, int worker, int start, int end, CancellationToken cancellationToken)
    {
        var width = copy.Size.Width;
        var channels = copy.WorkingChannels;
        var colorCount = channels == 1 ? 1 : 3;
        var hasAlpha = channels == 4;
        var colorScale = copy.ColorScale;
        var alphaScale = copy.AlphaScale;

        // The distance factor of every working sample: its scale squared, the colors divided by their number
        Span<double> factors = stackalloc double[channels];
        for (var c = 0; c < channels; c++)
        {
            factors[c] = hasAlpha && c == 3 ? alphaScale * alphaScale * rangeFactor : colorScale * colorScale / colorCount * rangeFactor;
        }

        var buffer = copy.GetRowBuffer(worker);
        var output = buffer[..(width * channels)];
        var weightSums = buffer.Slice(width * channels, width);
        var diameter = (2 * radius) + 1;
        for (var y = start; y < end; y++)
        {
            if ((y - start) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            output.Clear();
            weightSums.Clear();
            var center = copy.GetRow(y);
            for (var dy = -radius; dy <= radius; dy++)
            {
                var sourceRow = copy.MapRow(y + dy);
                var neighbors = sourceRow < 0 ? default : copy.GetRow(sourceRow);
                var halfWidth = GetHalfWidth(radius, dy);
                var weights = spatialWeights.AsSpan(((dy + radius) * diameter) + radius - halfWidth, (2 * halfWidth) + 1);
                for (var x = 0; x < width; x++)
                {
                    var pixel = center.Slice(x * channels, channels);
                    var sums = output.Slice(x * channels, channels);
                    var weightSum = 0d;
                    for (var dx = -halfWidth; dx <= halfWidth; dx++)
                    {
                        // A pixel read beyond the edges in the zero mode is transparent black
                        var column = x + dx;
                        if ((uint)column >= (uint)width)
                        {
                            column = copy.MapColumn(column);
                        }

                        var t = 0d;
                        var transparent = sourceRow < 0 || column < 0;
                        var offset = column * channels;
                        for (var c = 0; c < channels; c++)
                        {
                            var difference = (transparent ? 0 : neighbors[offset + c]) - (double)pixel[c];
                            t += difference * difference * factors[c];
                        }

                        var weight = weights[dx + halfWidth] * GetRangeWeight(t);
                        if (weight == 0)
                            continue;

                        weightSum += weight;
                        if (!transparent)
                        {
                            for (var c = 0; c < channels; c++)
                            {
                                sums[c] += weight * neighbors[offset + c];
                            }
                        }
                    }

                    weightSums[x] += weightSum;
                }
            }

            for (var x = 0; x < width; x++)
            {
                var weightSum = weightSums[x];
                var sums = output.Slice(x * channels, channels);
                for (var c = 0; c < channels; c++)
                {
                    sums[c] /= weightSum;
                }
            }

            copy.Store(storage, y, output);
        }
    }
}
