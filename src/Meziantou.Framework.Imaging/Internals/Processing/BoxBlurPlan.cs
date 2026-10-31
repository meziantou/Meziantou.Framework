using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The prepared state of a box blur (<see cref="BoxBlurrer"/>): the radius, the edge rule, the working-sample layout of the
/// pixel format, the row bands and the scratch of each worker. One plan serves every frame of an image.
/// </summary>
/// <remarks>
/// <para>
/// A worker keeps, in one rental charged to the scope of the image before any pixel changes: the window of the
/// horizontally summed rows of the output row (as many as the box has rows), the summed rows it reads below its band
/// (the bottom halo, loaded before any row is rewritten; the rows above its band go straight to the window), the running
/// sum of the rows that follow a block, the output row, and the padded working row with its block sums.
/// </para>
/// <para>
/// A frame that is not taller than the radius is blurred from a row cache instead (<see cref="UsesRowCache"/>): the
/// edge mode reads its rows many times each, so the horizontal sums of every row are computed once, in one rental shared
/// by the workers, and each worker keeps the vertical suffix sums of its band (as many as the box or the band has rows,
/// and a running sum), the running sum of the rows that follow a block, the output row, and the padded working row with
/// its block sums. This is never more scratch than the window.
/// </para>
/// </remarks>
internal sealed class BoxBlurPlan : IDisposable
{
    private static readonly AsyncLocal<bool?> TestRowCacheValue = new();

    private PooledBuffer?[] _scratch = [];
    private PooledBuffer? _rowCache;

    /// <summary>Initializes a new instance of the <see cref="BoxBlurPlan"/> class.</summary>
    /// <param name="radius">The number of pixels averaged on each side of a pixel, positive.</param>
    /// <param name="edgeMode">The pixels read outside the image.</param>
    /// <param name="format">The pixel format.</param>
    /// <param name="size">The size of the frames.</param>
    /// <param name="maxDegreeOfParallelism">The largest number of workers.</param>
    /// <param name="transfer">The transfer characteristics of linear-light filtering, or 0 to filter the stored values.</param>
    public BoxBlurPlan(int radius, ConvolutionEdgeMode edgeMode, PixelFormat format, Size size, int maxDegreeOfParallelism = 1, byte transfer = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(radius);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        Radius = radius;
        EdgeMode = edgeMode;
        Transfer = transfer;
        Size = size;
        Channels = PixelFormats.GetComponentCount(format);
        Alpha = PixelFormats.HasAlpha(format) ? WorkingAlpha.Premultiplied : WorkingAlpha.None;
        MaxValue = WorkingSamples.GetMaxValue(format);
        Workers = RowBands.GetWorkerCount(size, maxDegreeOfParallelism);
        UsesRowCache = edgeMode != ConvolutionEdgeMode.Zero && (TestRowCache ?? size.Height <= radius);
        var bandRows = 0;
        for (var band = 0; band < Workers; band++)
        {
            bandRows = Math.Max(bandRows, GetBandStart(band + 1) - GetBandStart(band));
        }

        SuffixRows = Math.Min(Diameter, bandRows) + 1;
        WorkingRows = UsesRowCache ? SuffixRows + 2 : Diameter + radius + 2;
    }

    /// <summary>
    /// Gets or sets whether the plans created on the current asynchronous flow use the row cache (<see langword="true"/>)
    /// or the window (<see langword="false"/>) whatever the size of the frame, or choose by the size (<see langword="null"/>,
    /// the default). The zero edge mode always uses the window. Tests compare both paths bit for bit.
    /// </summary>
    internal static bool? TestRowCache
    {
        get => TestRowCacheValue.Value;
        set => TestRowCacheValue.Value = value;
    }

    /// <summary>Gets the number of pixels averaged on each side of a pixel.</summary>
    public int Radius { get; }

    /// <summary>Gets the number of pixels of a side of the box, <c>2 Radius + 1</c>.</summary>
    public int Diameter => (2 * Radius) + 1;

    public ConvolutionEdgeMode EdgeMode { get; }

    /// <summary>Gets the transfer characteristics whose function decodes the samples to linear light, or 0 when they are filtered as stored.</summary>
    public byte Transfer { get; }

    /// <summary>Gets the size of the frames.</summary>
    public Size Size { get; }

    /// <summary>Gets the number of samples per pixel (alpha included).</summary>
    public int Channels { get; }

    /// <summary>Gets how alpha takes part in the working samples (before the opaque shortcut).</summary>
    public WorkingAlpha Alpha { get; }

    /// <summary>Gets the maximum sample value (<see cref="WorkingSamples.GetMaxValue"/>).</summary>
    public int MaxValue { get; }

    /// <summary>Gets the number of workers (and row bands) of each frame.</summary>
    public int Workers { get; }

    /// <summary>
    /// Gets a value indicating whether the horizontal sums of every row of the frame are computed once into a row cache
    /// (a frame not taller than the radius, except with <see cref="ConvolutionEdgeMode.Zero"/>, which reads no row twice),
    /// rather than for every row of the window, which repeats the rows of a short frame many times.
    /// </summary>
    public bool UsesRowCache { get; }

    /// <summary>Gets the number of rows of the vertical suffix sums of a worker of the row cache: those of a block in a band, and a running sum.</summary>
    internal int SuffixRows { get; }

    /// <summary>Gets the number of unpadded rows of the scratch of a worker, before its padded row.</summary>
    internal int WorkingRows { get; }

    /// <summary>Gets the number of bytes of scratch storage rented by <see cref="Prepare"/> (pool capacity, as charged).</summary>
    public long ScratchBytes { get; private set; }

    /// <summary>Gets the number of doubles of an unpadded working row.</summary>
    internal int RowLength => Size.Width * Channels;

    /// <summary>Gets the number of doubles of a padded working row: the radius on each side.</summary>
    internal int PaddedLength => (Size.Width + (2 * Radius)) * Channels;

    /// <summary>Rents the scratch of every worker. Called once per operation, before any pixel changes.</summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit; the plan must still be disposed.</exception>
    public void Prepare(AllocationScope scope)
    {
        var before = scope.LiveBytes;
        var rowLength = (long)Size.Width * Channels;
        var paddedLength = ((long)Size.Width + (2L * Radius)) * Channels;

        // The window and the bottom halo (or the suffix sums of the row cache), then the running sum and the output row,
        // then the padded row and its block sums
        if (!CheckedSizes.TryMultiply(rowLength, WorkingRows, out var rows) || !CheckedSizes.TryMultiply(paddedLength, 2, out var padded) || !CheckedSizes.TryAdd(rows, padded, out var total))
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        if (UsesRowCache)
        {
            if (!CheckedSizes.TryMultiply(rowLength, Size.Height, out var cache))
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            _rowCache = ResampleWeights.Rent<double>(scope, cache);
        }

        _scratch = new PooledBuffer?[Workers];
        for (var worker = 0; worker < Workers; worker++)
        {
            _scratch[worker] = ResampleWeights.Rent<double>(scope, total);
        }

        ScratchBytes = scope.LiveBytes - before;
    }

    /// <summary>Gets the scratch of a worker, between 0 and <see cref="Workers"/> - 1.</summary>
    public Span<double> GetScratch(int worker) => unsafe(MemoryMarshal.Cast<byte, double>((_scratch[worker] ?? throw new ObjectDisposedException(nameof(BoxBlurPlan))).Span));

    /// <summary>Gets the horizontal sums of an image row in the row cache (<see cref="UsesRowCache"/>): <paramref name="length"/> doubles.</summary>
    public Span<double> GetCachedRow(int row, int length)
        => unsafe(MemoryMarshal.Cast<byte, double>((_rowCache ?? throw new ObjectDisposedException(nameof(BoxBlurPlan))).Span)).Slice(row * RowLength, length);

    /// <summary>Gets the first row of a band.</summary>
    public int GetBandStart(int band) => RowBands.GetBandStart(Size.Height, band, Workers);

    /// <summary>Gets the image row read for row <paramref name="y"/>, which may be outside the image, or -1 when it contributes nothing.</summary>
    public int MapRow(int y) => ConvolutionPlan.MapIndex(y, Size.Height, EdgeMode);

    /// <summary>Gets the image column read for column <paramref name="x"/>, which may be outside the image, or -1 when it contributes nothing.</summary>
    public int MapColumn(int x) => ConvolutionPlan.MapIndex(x, Size.Width, EdgeMode);

    public void Dispose()
    {
        foreach (var scratch in _scratch)
        {
            scratch?.Dispose();
        }

        _scratch = [];
        _rowCache?.Dispose();
        _rowCache = null;
    }
}
