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

    /// <summary>
    /// Gets how alpha takes part in the working samples: premultiplied when the format has alpha, except in a frame whose
    /// pixels read are all opaque (<see cref="WorkingAlpha.Opaque"/>, decided by <see cref="Resampler"/> for each frame).
    /// </summary>
    public WorkingAlpha Alpha => HasAlpha ? WorkingAlpha.Premultiplied : WorkingAlpha.None;

    /// <summary>Gets the maximum sample value (255 or 65,535).</summary>
    public int MaxValue { get; }

    /// <summary>Gets the horizontal weights (filtered resize).</summary>
    public ResampleWeights? XWeights { get; private set; }

    /// <summary>Gets the vertical weights (filtered resize).</summary>
    public ResampleWeights? YWeights { get; private set; }

    /// <summary>
    /// Gets the horizontal weights of the integer path (<see cref="FixedPointResampler"/>), or <see langword="null"/> when the
    /// resize cannot take it: 16-bit samples, linear light, nearest neighbor, the scatter strategy, or weights for which no
    /// intermediate precision fits 16 bits.
    /// </summary>
    public FixedPointWeights? XFixed { get; private set; }

    /// <summary>Gets the vertical weights of the integer path, or <see langword="null"/> (see <see cref="XFixed"/>).</summary>
    public FixedPointWeights? YFixed { get; private set; }

    /// <summary>Gets the number of fractional bits of the 16-bit intermediate rows of the integer path (<see cref="FixedPointResampler.GetPrecision"/>).</summary>
    public int FixedPrecision { get; private set; }

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

        // The vector loops of the horizontal pass read windows padded to the same number of taps
        var group = Resampler.GetHorizontalWeightsGroup(Channels);
        XWeights = group is 0 ? ResampleWeights.Create(scope, Geometry.X, Filter) : ResampleWeights.CreatePadded(scope, Geometry.X, Filter, group);
        YWeights = ResampleWeights.Create(scope, Geometry.Y, Filter);
        var rowLength = (long)output.Width * Channels;

        // The working row covers the padded windows, whose samples after the used columns stay finite (the buffer is cleared
        // when rented, and only working samples are written to it), and has one extra element for 3-sample pixels: the
        // vectorized horizontal pass loads 4 samples per pixel. The colors of opaque 4-sample pixels (3 per pixel) fit in it.
        var sourceLength = group is 0 ? XWeights.Last - XWeights.First + 1 : XWeights.PaddedSourceLength;
        var workingLength = ((long)sourceLength * Channels) + (Channels == 3 ? 1 : 0);

        // 8-bit samples filtered as stored take the integer path when no pixel read is translucent: it shares the scratch
        // (its double rows are larger than the 16-bit rows), whose working row must also hold the padded 16-bit windows
        var fixedPoint = !IsLinear && MaxValue == byte.MaxValue && !Resampler.TestDisableFixedPoint;
        if (fixedPoint)
        {
            var multiple = Channels is 1 ? FixedPointResampler.GrayTapMultiple : FixedPointResampler.ColorTapMultiple;
            var taps = (XWeights.MaxCount + multiple - 1) / multiple * multiple;
            var fixedBytes = (long)Math.Max(XWeights.Last - XWeights.First + 1, taps) * FixedPointResampler.GetLanes(Channels) * sizeof(short);
            workingLength = Math.Max(workingLength, (fixedBytes + sizeof(double) - 1) / sizeof(double));
        }

        Strategy = TestStrategyOverride ?? SelectStrategy(scope, YWeights, workingLength, rowLength);

        var rows = Strategy == ResizeVerticalStrategy.Gather ? YWeights.MaxCount : YWeights.MaxOverlap;
        _scratch = new ResizeScratch?[Workers];
        for (var worker = 0; worker < Workers; worker++)
        {
            _scratch[worker] = ResizeScratch.Create(scope, workingLength, rowLength, rows);
        }

        // The tables of the integer path come last: they are optional, and skipped when they do not fit the limit
        if (fixedPoint && Strategy == ResizeVerticalStrategy.Gather)
        {
            PrepareFixedPoint(scope);
        }

        ScratchBytes = scope.LiveBytes - before;
    }

    /// <summary>
    /// Quantizes the weights of the integer path and picks its intermediate precision; leaves <see cref="XFixed"/> null when
    /// no precision fits or when the tables exceed the allocation limit (the double-precision path needs no more).
    /// </summary>
    private void PrepareFixedPoint(AllocationScope scope)
    {
        var xWeights = XWeights!;
        var unpadded = xWeights.Group is 1 ? null : ResampleWeights.Create(scope, Geometry.X, Filter);
        try
        {
            XFixed = FixedPointWeights.Create(scope, unpadded ?? xWeights, Channels is 1 ? FixedPointResampler.GrayTapMultiple : FixedPointResampler.ColorTapMultiple);
            YFixed = FixedPointWeights.Create(scope, YWeights!, tapMultiple: 0);
            FixedPrecision = XFixed is null || YFixed is null ? -1 : FixedPointResampler.GetPrecision(XFixed, YFixed);
            if (FixedPrecision < 0)
            {
                DisposeFixedPoint();
            }
        }
        catch (ImageResourceLimitException)
        {
            DisposeFixedPoint();
        }
        catch
        {
            DisposeFixedPoint();
            throw;
        }
        finally
        {
            unpadded?.Dispose();
        }
    }

    private void DisposeFixedPoint()
    {
        XFixed?.Dispose();
        YFixed?.Dispose();
        XFixed = null;
        YFixed = null;
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
        DisposeFixedPoint();
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
