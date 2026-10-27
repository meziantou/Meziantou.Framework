using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Everything a transformation of the frames needs besides the pixels, shared by every frame of one operation: the
/// geometry, the filter, the working samples and the row buffers of the workers. Every buffer is rented from the
/// allocation scope of the image (<see cref="AllocationKind.Temporary"/>), so the scratch is budgeted together with the
/// original and replacement storages, and released by <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// The output rows are split into <see cref="Workers"/> contiguous bands, each processed by one worker with its own
/// buffers. Every output pixel is computed alone, by the same operations whatever the band: the result does not depend
/// on the number of workers.
/// </remarks>
internal sealed class WarpPlan : IDisposable
{
    private PooledBuffer?[] _scratch = [];

    public WarpPlan(WarpGeometry geometry, ResamplingFilter filter, Rgba64 background, ResizeWorkingSpace workingSpace, PixelFormat format, int maxDegreeOfParallelism, byte transfer)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        Geometry = geometry;
        Filter = filter;
        Background = background;
        Transfer = workingSpace == ResizeWorkingSpace.LinearSrgb ? transfer : (byte)0;
        Channels = PixelFormats.GetComponentCount(format);
        MaxValue = WorkingSamples.GetMaxValue(format);
        Alpha = PixelFormats.HasAlpha(format) ? WorkingAlpha.Premultiplied : WorkingAlpha.None;
        Workers = RowBands.GetWorkerCount(geometry.OutputSize, maxDegreeOfParallelism);
        if (filter != ResamplingFilter.NearestNeighbor)
        {
            // The weights a pixel keeps are those of the source pixels it reads: never more than the kernel reaches, nor
            // than the source has
            var radius = ResamplingKernels.GetRadius(filter);
            geometry.GetMaxFilterScales(out var scaleX, out var scaleY);
            TapsX = GetMaxTaps(radius * scaleX, geometry.SourceSize.Width);
            TapsY = GetMaxTaps(radius * scaleY, geometry.SourceSize.Height);
        }
    }

    public WarpGeometry Geometry { get; }

    public ResamplingFilter Filter { get; }

    /// <summary>Gets the color of the source beyond its edges, and of the canvas where the source does not reach.</summary>
    public Rgba64 Background { get; }

    /// <summary>
    /// Gets the transfer characteristics (<see cref="CicpTransfer"/>) whose function decodes the samples to the linear
    /// light they are filtered in, or 0 when they are filtered as they are stored.
    /// </summary>
    public byte Transfer { get; }

    /// <summary>Gets the number of samples per pixel (alpha included).</summary>
    public int Channels { get; }

    /// <summary>Gets the maximum sample value: 255 or 65,535, or 1 for floating-point samples.</summary>
    public int MaxValue { get; }

    /// <summary>Gets how alpha takes part in the working samples: premultiplied when the format has alpha.</summary>
    public WorkingAlpha Alpha { get; }

    /// <summary>Gets the number of workers (and row bands) of each frame.</summary>
    public int Workers { get; }

    /// <summary>Gets the largest number of source columns a pixel reads (filtered transformation).</summary>
    public int TapsX { get; }

    /// <summary>Gets the largest number of source rows a pixel reads (filtered transformation).</summary>
    public int TapsY { get; }

    /// <summary>
    /// Rents the buffers of the workers. Called once per operation after the replacement storages are reserved, so that
    /// the limit covers the original storages, the replacements and the scratch together.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit; the plan must still be disposed.</exception>
    public void Prepare(AllocationScope scope)
    {
        if (Filter == ResamplingFilter.NearestNeighbor)
            return;

        _scratch = new PooledBuffer?[Workers];
        for (var worker = 0; worker < Workers; worker++)
        {
            _scratch[worker] = ResampleWeights.Rent<double>(scope, GetScratchLength());
        }
    }

    /// <summary>
    /// Gets the buffers of a worker, between 0 and <see cref="Workers"/> - 1: the working samples of an output row, those
    /// of the source columns a pixel reads in one row, the horizontal and vertical weights, and two pixels of sums.
    /// </summary>
    public Span<double> GetScratch(int worker)
        => unsafe(MemoryMarshal.Cast<byte, double>((_scratch[worker] ?? throw new ObjectDisposedException(nameof(WarpPlan))).Span))[..(int)GetScratchLength()];

    /// <summary>Gets the first output row of a band.</summary>
    public int GetBandStart(int band) => RowBands.GetBandStart(Geometry.OutputSize.Height, band, Workers);

    public void Dispose()
    {
        foreach (var buffer in _scratch)
        {
            buffer?.Dispose();
        }

        _scratch = [];
    }

    private long GetScratchLength()
        => ((long)Geometry.OutputSize.Width * Channels) + ((long)TapsX * Channels) + TapsX + TapsY + (2L * Channels);

    private static int GetMaxTaps(double reach, int length)
    {
        // The integers strictly within the reach of a center (or in the half-open interval of a box): at most 2 ceil(reach)
        var taps = 2 * Math.Ceiling(reach);
        return taps >= length ? length : (int)taps;
    }
}
