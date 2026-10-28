namespace Meziantou.Framework.Imaging;

/// <summary>
/// An immutable histogram of one channel of the pixels of an image or a frame
/// (<see cref="ImageProcessingExtensions.ComputeHistogram(Image, HistogramChannel, CancellationToken)"/>): the number of
/// pixels at each sample level.
/// </summary>
/// <remarks>
/// There is one bin per sample level, at the storage precision: 256 bins for 8-bit pixel formats, 65,536 for 16-bit ones.
/// Every pixel is counted, fully transparent ones included.
/// </remarks>
public sealed class ImageHistogram
{
    private readonly long[] _counts;

    internal ImageHistogram(HistogramChannel channel, long[] counts, long totalCount)
    {
        Channel = channel;
        _counts = counts;
        TotalCount = totalCount;
    }

    /// <summary>Gets the channel that is counted.</summary>
    public HistogramChannel Channel { get; }

    /// <summary>Gets the number of bins: 256 for 8-bit pixel formats, 65,536 for 16-bit ones.</summary>
    public int BinCount => _counts.Length;

    /// <summary>Gets the number of pixels counted, the sum of the bins.</summary>
    public long TotalCount { get; }

    /// <summary>Gets the number of pixels of every level, indexed by the level.</summary>
    public ReadOnlySpan<long> Counts => _counts;

    /// <summary>Gets the number of pixels whose sample is a level.</summary>
    /// <param name="level">The level, from 0 to <c>BinCount - 1</c>.</param>
    /// <returns>The number of pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is out of range.</exception>
    public long this[int level]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(level);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(level, _counts.Length);
            return _counts[level];
        }
    }

    /// <summary>
    /// Gets the threshold that Otsu's method chooses: the level <c>t</c> that maximizes the variance between the class of
    /// the levels up to <c>t</c> and the class of the levels above it, <c>w1 * w2 * (m1 - m2)²</c>, where <c>w1</c> and
    /// <c>w2</c> are the numbers of pixels of the classes and <c>m1</c> and <c>m2</c> their mean levels.
    /// </summary>
    /// <remarks>
    /// The candidates are the levels from the lowest one present to the one below the highest one present, and the first
    /// maximum wins. The variances are computed as scikit-image's <c>threshold_otsu</c> computes them, from exact integer
    /// sums, so the threshold is the one it returns for the same integer samples. When a single level is present, it is the
    /// threshold. <see cref="ImageProcessingExtensions.Threshold(Image, ThresholdOptions?, CancellationToken)"/> with
    /// <see cref="ThresholdMethod.Otsu"/> uses the threshold of the <see cref="HistogramChannel.Luminance"/> histogram.
    /// </remarks>
    /// <returns>The threshold: the pixels above it form the upper class.</returns>
    public int GetOtsuThreshold()
    {
        var counts = _counts;
        var first = counts.AsSpan().IndexOfAnyExcept(0L);
        var last = counts.AsSpan().LastIndexOfAnyExcept(0L);
        if (first < 0 || first == last)
            return Math.Max(first, 0);

        long totalWeight = 0;
        long totalSum = 0;
        for (var level = first; level <= last; level++)
        {
            totalWeight += counts[level];
            totalSum += counts[level] * level;
        }

        // The classes of the candidate t are [first, t] and [t + 1, last]: both have at least one pixel
        long lowerWeight = 0;
        long lowerSum = 0;
        var best = first;
        var bestVariance = double.NegativeInfinity;
        for (var level = first; level < last; level++)
        {
            lowerWeight += counts[level];
            lowerSum += counts[level] * level;
            var upperWeight = totalWeight - lowerWeight;
            var difference = ((double)lowerSum / lowerWeight) - ((double)(totalSum - lowerSum) / upperWeight);
            var variance = (double)lowerWeight * upperWeight * (difference * difference);
            if (variance > bestVariance)
            {
                bestVariance = variance;
                best = level;
            }
        }

        return best;
    }
}
