using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The prepared state of a median filter (<see cref="MedianFilter"/>): the radius, the edge rule, the sample keys of the
/// pixel format, the row bands and the scratch of each worker. One plan serves every frame of an image.
/// </summary>
/// <remarks>
/// A worker keeps, in rentals charged to the scope of the image before any pixel changes, the keys of the rows of its band
/// that the window reads (a ring as tall as the window), of the rows it reads above and below its band (the halos, loaded
/// before any row is rewritten), of the output row, and one histogram per sample of a pixel (two trees of 16 bits for
/// 32-bit keys), or the keys of one window when they are sorted (<see cref="SortsWindows"/>).
/// </remarks>
internal sealed class MedianPlan : IDisposable
{
    /// <summary>The number of counters of a tree of 16-bit keys: 16 per level of 4 bits.</summary>
    internal const int WideTreeLength = 16 + 256 + 4096 + 65536;

    private PooledBuffer?[] _keys = [];
    private PooledBuffer?[] _counts = [];

    /// <summary>Initializes a new instance of the <see cref="MedianPlan"/> class.</summary>
    /// <param name="radius">The number of pixels on each side of a pixel, positive.</param>
    /// <param name="edgeMode">The pixels read outside the image.</param>
    /// <param name="format">The pixel format.</param>
    /// <param name="size">The size of the frames.</param>
    /// <param name="maxDegreeOfParallelism">The largest number of workers.</param>
    public MedianPlan(int radius, ConvolutionEdgeMode edgeMode, PixelFormat format, Size size, int maxDegreeOfParallelism = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        Radius = radius;
        EdgeMode = edgeMode;
        Size = size;
        Channels = PixelFormats.GetComponentCount(format);
        KeyBits = format == PixelFormat.RgbaFloat ? 32 : PixelFormats.GetBitsPerComponent(format);
        Workers = RowBands.GetWorkerCount(size, maxDegreeOfParallelism);
    }

    /// <summary>Gets the number of pixels on each side of a pixel.</summary>
    public int Radius { get; }

    /// <summary>Gets the number of pixels of a side of the window, <c>2 Radius + 1</c>.</summary>
    public int Diameter => (2 * Radius) + 1;

    public ConvolutionEdgeMode EdgeMode { get; }

    /// <summary>Gets the size of the frames.</summary>
    public Size Size { get; }

    /// <summary>Gets the number of samples per pixel (alpha included).</summary>
    public int Channels { get; }

    /// <summary>Gets the number of bits of the sample keys: 8, 16 or 32 (<see cref="MedianFilter.ToKey"/>).</summary>
    public int KeyBits { get; }

    /// <summary>
    /// Gets a value indicating whether the median of every window is found by sorting its keys rather than in histograms:
    /// 32-bit keys of the smallest windows (9 keys), for which sorting is faster.
    /// </summary>
    public bool SortsWindows => KeyBits == 32 && Radius == 1;

    /// <summary>
    /// Gets the number of counters of the histogram of one sample: 16 per level of 4 bits of the keys, and two trees of
    /// 16 bits for 32-bit keys (<see cref="MedianFilter"/>).
    /// </summary>
    public int HistogramLength => KeyBits switch
    {
        8 => 16 + 256,
        16 => WideTreeLength,
        _ => 2 * WideTreeLength,
    };

    /// <summary>Gets the number of workers (and row bands) of each frame.</summary>
    public int Workers { get; }

    /// <summary>Gets the number of bytes of scratch storage rented by <see cref="Prepare"/> (pool capacity, as charged).</summary>
    public long ScratchBytes { get; private set; }

    /// <summary>Gets the number of keys of a padded row: the radius on each side.</summary>
    internal int PaddedLength => (Size.Width + (2 * Radius)) * Channels;

    /// <summary>Rents the scratch of every worker. Called once per operation, before any pixel changes.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit; the plan must still be disposed.</exception>
    public void Prepare(AllocationScope scope)
    {
        var before = scope.LiveBytes;
        var paddedLength = ((long)Size.Width + (2L * Radius)) * Channels;

        // The ring and the two halos, then the output row
        if (!CheckedSizes.TryMultiply(paddedLength, (long)Diameter + (2L * Radius), out var rows) || !CheckedSizes.TryAdd(rows, (long)Size.Width * Channels, out var keys))
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        var counts = SortsWindows ? (long)Diameter * Diameter : (long)HistogramLength * Channels;
        _keys = new PooledBuffer?[Workers];
        _counts = new PooledBuffer?[Workers];
        for (var worker = 0; worker < Workers; worker++)
        {
            _keys[worker] = ResampleWeights.Rent<uint>(scope, keys);
            _counts[worker] = ResampleWeights.Rent<int>(scope, counts);
        }

        ScratchBytes = scope.LiveBytes - before;
    }

    /// <summary>Gets the key rows of a worker: the ring, the top halo, the bottom halo and the output row.</summary>
    public Span<uint> GetKeys(int worker) => unsafe(MemoryMarshal.Cast<byte, uint>((_keys[worker] ?? throw new ObjectDisposedException(nameof(MedianPlan))).Span));

    /// <summary>Gets the histograms of a worker (one per sample of a pixel), or the keys of one window to sort (<see cref="SortsWindows"/>).</summary>
    public Span<int> GetCounts(int worker) => unsafe(MemoryMarshal.Cast<byte, int>((_counts[worker] ?? throw new ObjectDisposedException(nameof(MedianPlan))).Span));

    /// <summary>Gets the first row of a band.</summary>
    public int GetBandStart(int band) => RowBands.GetBandStart(Size.Height, band, Workers);

    /// <summary>Gets the image row read for row <paramref name="y"/>, which may be outside the image, or -1 when it reads zeros.</summary>
    public int MapRow(int y) => ConvolutionPlan.MapIndex(y, Size.Height, EdgeMode);

    /// <summary>Gets the image column read for column <paramref name="x"/>, which may be outside the image, or -1 when it reads zeros.</summary>
    public int MapColumn(int x) => ConvolutionPlan.MapIndex(x, Size.Width, EdgeMode);

    public void Dispose()
    {
        foreach (var buffer in _keys)
        {
            buffer?.Dispose();
        }

        foreach (var buffer in _counts)
        {
            buffer?.Dispose();
        }

        _keys = [];
        _counts = [];
    }
}
