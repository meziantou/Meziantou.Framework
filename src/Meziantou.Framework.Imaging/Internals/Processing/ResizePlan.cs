using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Everything a resize needs besides the pixels, shared by every frame of one operation: the geometry, the index or
/// weight tables of both axes, and the row scratch buffers. Every buffer is rented from the allocation scope of the image
/// (<see cref="AllocationKind.Temporary"/>), so the scratch is budgeted together with the original and replacement
/// storages, and released by <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// <para>
/// The two vertical strategies perform exactly the same floating-point operations in the same order (every output sample
/// is <c>0 + w0 v0 + w1 v1 + ...</c> in increasing source row), so they give bit-identical results; the plan picks gather
/// unless its ring of source rows is both larger than the accumulators of scatter and large in bytes (strong vertical
/// downsampling of wide outputs).
/// </para>
/// <para>
/// Bounded parallelism: the output rows are split into <see cref="Workers"/> contiguous bands, each processed by
/// one worker with its own row buffers (<see cref="GetScratch"/>); the tables are shared and read-only. A band recomputes the
/// horizontally resampled source rows it needs, so every output sample is still computed by the same operations in the same
/// order: the result does not depend on the number of workers. The scratch of every worker is rented up front and charged
/// together.
/// </para>
/// </remarks>
internal sealed class ResizePlan : IDisposable
{
    // The largest gather ring (per worker) picked over smaller scatter accumulators: 3x Lanczos3 to a 1920-pixel-wide
    // 4-sample output (19 rows, 1.2 MB) was 10% faster with gather than with scatter
    private const long MaxGatherRingBytes = 2 * 1024 * 1024;

    [ThreadStatic]
    private static ResizeVerticalStrategy? s_testStrategyOverride;

    private readonly List<PooledBuffer> _buffers = [];
    private ResizeScratch?[] _scratch = [];

    public ResizePlan(ResizeGeometry geometry, ResamplingFilter filter, ResizeWorkingSpace workingSpace, PixelFormat format, int maxDegreeOfParallelism = 1, byte transfer = CicpTransfer.Srgb)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
        Geometry = geometry;
        Filter = filter;
        IsLinear = workingSpace == ResizeWorkingSpace.LinearSrgb;
        Transfer = IsLinear ? transfer : (byte)0;
        Channels = PixelFormats.GetComponentCount(format);
        HasAlpha = PixelFormats.HasAlpha(format);
        MaxValue = PixelFormats.GetBitsPerComponent(format) == 16 ? ushort.MaxValue : byte.MaxValue;
        // Nearest neighbor copies pixels (memory bound): measured without gain from parallel bands, so it stays sequential
        Workers = filter == ResamplingFilter.NearestNeighbor ? 1 : RowBands.GetWorkerCount(geometry.OutputSize, maxDegreeOfParallelism);
    }

    /// <summary>Gets or sets the vertical strategy forced by tests on the current thread (both strategies must agree bit for bit).</summary>
    internal static ResizeVerticalStrategy? TestStrategyOverride
    {
        get => s_testStrategyOverride;
        set => s_testStrategyOverride = value;
    }

    public ResizeGeometry Geometry { get; }

    public ResamplingFilter Filter { get; }

    /// <summary>Gets a value indicating whether samples are filtered in linear light.</summary>
    public bool IsLinear { get; }

    /// <summary>
    /// Gets the transfer characteristics (<see cref="CicpTransfer"/>) whose function decodes the samples to the linear
    /// light they are filtered in, or 0 when they are filtered as they are stored.
    /// </summary>
    public byte Transfer { get; }

    /// <summary>Gets the number of samples per pixel (alpha included).</summary>
    public int Channels { get; }

    /// <summary>Gets a value indicating whether the last sample of each pixel is a straight alpha.</summary>
    public bool HasAlpha { get; }

    /// <summary>Gets how alpha takes part in the working samples: premultiplied when the format has alpha.</summary>
    public WorkingAlpha Alpha => HasAlpha ? WorkingAlpha.Premultiplied : WorkingAlpha.None;

    /// <summary>Gets the maximum sample value (255 or 65,535).</summary>
    public int MaxValue { get; }

    /// <summary>Gets the horizontal weights (filtered resize).</summary>
    public ResampleWeights? XWeights { get; private set; }

    /// <summary>Gets the vertical weights (filtered resize).</summary>
    public ResampleWeights? YWeights { get; private set; }

    /// <summary>Gets the vertical strategy (filtered resize).</summary>
    public ResizeVerticalStrategy Strategy { get; private set; }

    /// <summary>
    /// Gets the number of workers (and row bands) of each frame: 1 unless the configuration allows more, the output is large
    /// enough (<see cref="RowBands.GetWorkerCount"/>) and the filter is not nearest neighbor.
    /// </summary>
    public int Workers { get; }

    /// <summary>Gets the number of bytes of scratch storage rented by <see cref="Prepare"/> (pool capacity, as charged).</summary>
    public long ScratchBytes { get; private set; }

    /// <summary>Gets the source column index of each output column (nearest neighbor).</summary>
    public ReadOnlySpan<int> XIndices => unsafe(MemoryMarshal.Cast<byte, int>(_buffers[0].Span));

    /// <summary>Gets the source row index of each output row (nearest neighbor).</summary>
    public ReadOnlySpan<int> YIndices => unsafe(MemoryMarshal.Cast<byte, int>(_buffers[1].Span));

    /// <summary>Gets the number of ring rows (gather) or accumulator rows (scatter) of each worker.</summary>
    public int RowCount => _scratch.Length == 0 ? 0 : _scratch[0]!.RowCount;

    /// <summary>
    /// Rents and fills the tables and buffers. Called once per operation after the replacement storages are reserved,
    /// so that the limit covers the original storages, the replacements and the scratch together.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The scratch exceeds the allocation limit; the plan must still be disposed.</exception>
    public void Prepare(AllocationScope scope)
    {
        var before = scope.LiveBytes;
        var output = Geometry.OutputSize;
        if (Filter == ResamplingFilter.NearestNeighbor)
        {
            var x = Add(ResampleWeights.Rent<int>(scope, output.Width));
            var y = Add(ResampleWeights.Rent<int>(scope, output.Height));
            var xs = unsafe(MemoryMarshal.Cast<byte, int>(x.Span));
            for (var i = 0; i < output.Width; i++)
            {
                xs[i] = Geometry.X.GetNearestIndex(i);
            }

            var ys = unsafe(MemoryMarshal.Cast<byte, int>(y.Span));
            for (var i = 0; i < output.Height; i++)
            {
                ys[i] = Geometry.Y.GetNearestIndex(i);
            }

            ScratchBytes = scope.LiveBytes - before;
            return;
        }

        XWeights = ResampleWeights.Create(scope, Geometry.X, Filter);
        YWeights = ResampleWeights.Create(scope, Geometry.Y, Filter);
        var rowLength = (long)output.Width * Channels;

        // The working row has one extra element for 3-sample pixels: the vectorized horizontal pass loads 4 samples per pixel
        var workingLength = (((long)XWeights.Last - XWeights.First + 1) * Channels) + (Channels == 3 ? 1 : 0);
        Strategy = TestStrategyOverride ?? SelectStrategy(scope, YWeights, workingLength, rowLength);
        var rows = Strategy == ResizeVerticalStrategy.Gather ? YWeights.MaxCount : YWeights.MaxOverlap;
        _scratch = new ResizeScratch?[Workers];
        for (var worker = 0; worker < Workers; worker++)
        {
            _scratch[worker] = ResizeScratch.Create(scope, workingLength, rowLength, rows);
        }

        ScratchBytes = scope.LiveBytes - before;
    }

    /// <summary>
    /// Picks gather when its ring is not larger than the accumulators of scatter. Otherwise gather is still faster while its
    /// ring stays small (<see cref="MaxGatherRingBytes"/>, measured): it sums the taps of an output row by groups of rows in
    /// registers (<see cref="Resampler.SetWeightedSum"/>) where scatter reads and writes an accumulator row per tap. Scatter
    /// is kept when the larger ring would not fit the allocation limit.
    /// </summary>
    private ResizeVerticalStrategy SelectStrategy(AllocationScope scope, ResampleWeights yWeights, long workingLength, long rowLength)
    {
        if (yWeights.MaxCount <= yWeights.MaxOverlap)
            return ResizeVerticalStrategy.Gather;

        var ringBytes = yWeights.MaxCount * rowLength * sizeof(double);
        if (ringBytes > MaxGatherRingBytes)
            return ResizeVerticalStrategy.Scatter;

        var workerBytes = scope.GetChargedSize(workingLength * sizeof(double)) + scope.GetChargedSize(ringBytes + (rowLength * sizeof(double)));
        return scope.LiveBytes + (Workers * workerBytes) <= scope.Limit ? ResizeVerticalStrategy.Gather : ResizeVerticalStrategy.Scatter;
    }

    /// <summary>Gets the row buffers of a worker, between 0 and <see cref="Workers"/> - 1 (filtered resize).</summary>
    public ResizeScratch GetScratch(int worker) => _scratch[worker]!;

    /// <summary>Gets the first output row of a band (the bands split the output rows into <see cref="Workers"/> contiguous ranges).</summary>
    public int GetBandStart(int band) => RowBands.GetBandStart(Geometry.OutputSize.Height, band, Workers);

    public void Dispose()
    {
        XWeights?.Dispose();
        YWeights?.Dispose();
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }

        _buffers.Clear();
        foreach (var scratch in _scratch)
        {
            scratch?.Dispose();
        }

        _scratch = [];
    }

    private PooledBuffer Add(PooledBuffer buffer)
    {
        _buffers.Add(buffer);
        return buffer;
    }
}
