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
}
