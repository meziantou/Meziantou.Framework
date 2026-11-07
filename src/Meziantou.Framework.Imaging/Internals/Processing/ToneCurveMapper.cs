using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The histogram-based tone operations of whole frames: global histogram equalization, contrast-limited adaptive
/// histogram equalization (CLAHE) and auto-levels. The histograms of a group of frames (one frame, or every frame of an
/// image when they share their histograms) become lookup tables of the sample levels, which then remap the pixels.
/// </summary>
/// <remarks>
/// <para>
/// A "plane" is what a table maps: the luma of the pixels in <see cref="HistogramColorMode.Luminance"/> mode (and for gray
/// formats, whose luma is their value), or each of the red, green and blue samples. A mapped luma <c>Y'</c> adds
/// <c>Y' - Y</c> to the three colors of the pixel, clamped. Fully transparent pixels are not counted, but are remapped.
/// </para>
/// <para>
/// The working memory (a row of samples, the histograms of a tile, the tables of every tile and, for the adaptive mode,
/// the interpolation of every column) is rented from the scope as <see cref="AllocationKind.Temporary"/> at creation,
/// before a pixel changes.
/// </para>
/// </remarks>
internal sealed class ToneCurveMapper : IDisposable
{
    /// <summary>The number of bins of the histogram of a tile when it is clipped: one per level of 8-bit samples.</summary>
    internal const int ClipBinCount = 256;

    private readonly PixelFormat _format;
    private readonly int _bytesPerPixel;
    private readonly int _width;
    private readonly int _height;
    private readonly int _levels;
    private readonly ushort _max;
    private readonly int _planes;
    private readonly bool _hasAlpha;
    private readonly bool _adaptive;
    private readonly int _tilesX;
    private readonly int _tilesY;
    private readonly int[] _edgesX;
    private readonly int[] _edgesY;
    private readonly double _clipLimit;
    private PooledBuffer? _row;
    private PooledBuffer? _histogram;
    private PooledBuffer? _tables;
    private PooledBuffer? _columnTiles;
    private PooledBuffer? _columnWeights;

    /// <summary>Rents the working memory of the operation.</summary>
    /// <param name="scope">The scope charged for the working memory.</param>
    /// <param name="format">The pixel format of the frames: an integer one.</param>
    /// <param name="size">The size of the frames.</param>
    /// <param name="colorMode">The samples that are remapped.</param>
    /// <param name="adaptive">The options of an adaptive equalization, or <see langword="null"/> for one table per plane.</param>
    /// <exception cref="ImageResourceLimitException">The working memory exceeds the allocation limit.</exception>
    public ToneCurveMapper(AllocationScope scope, PixelFormat format, Size size, HistogramColorMode colorMode, HistogramEqualizationOptions? adaptive)
    {
        _format = format;
        _bytesPerPixel = PixelFormats.GetBytesPerPixel(format);
        _width = size.Width;
        _height = size.Height;
        _levels = ToneSamples.GetLevelCount(format);
        _max = (ushort)(_levels - 1);
        _planes = colorMode == HistogramColorMode.Luminance || PixelFormats.IsGrayscale(format) ? 1 : 3;
        _hasAlpha = PixelFormats.HasAlpha(format);
        _adaptive = adaptive is not null;
        _tilesX = adaptive is null ? 1 : Math.Min(adaptive.TileCountX, _width);
        _tilesY = adaptive is null ? 1 : Math.Min(adaptive.TileCountY, _height);
        _edgesX = GetTileEdges(_width, _tilesX);
        _edgesY = GetTileEdges(_height, _tilesY);
        _clipLimit = adaptive?.ClipLimit ?? double.PositiveInfinity;

        var tableBytes = (long)_tilesX * _tilesY * _planes * _levels * sizeof(ushort);
        if (tableBytes > CheckedSizes.MaxBufferLength || (_adaptive && (long)_width * sizeof(double) > CheckedSizes.MaxBufferLength))
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        try
        {
            _row = ToneSamples.RentRow(scope, _width);
            _histogram = scope.Rent(_planes * _levels * sizeof(long), AllocationKind.Temporary, clear: false);
            _tables = scope.Rent((int)tableBytes, AllocationKind.Temporary, clear: false);
            if (_adaptive)
            {
                _columnTiles = scope.Rent(_width * sizeof(int), AllocationKind.Temporary, clear: false);
                _columnWeights = scope.Rent(_width * sizeof(double), AllocationKind.Temporary, clear: false);
                var tiles = ColumnTiles;
                var weights = ColumnWeights;
                for (var x = 0; x < _width; x++)
                {
                    Locate(x, _edgesX, out tiles[x], out weights[x]);
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private Span<ushort> Row => ToneSamples.GetSamples(GetBuffer(_row), _width);

    private Span<ushort> LumaRow => ToneSamples.GetLuma(GetBuffer(_row), _width);

    private Span<long> Histogram => unsafe(MemoryMarshal.Cast<byte, long>(GetBuffer(_histogram).Span))[..(_planes * _levels)];

    private Span<ushort> Tables => unsafe(MemoryMarshal.Cast<byte, ushort>(GetBuffer(_tables).Span));

    private Span<int> ColumnTiles => unsafe(MemoryMarshal.Cast<byte, int>(GetBuffer(_columnTiles).Span))[.._width];

    private Span<double> ColumnWeights => unsafe(MemoryMarshal.Cast<byte, double>(GetBuffer(_columnWeights).Span))[.._width];

    /// <summary>Equalizes the histograms of a group of frames, with the mode given at creation.</summary>
    /// <param name="group">The frames whose histograms are combined, and that are remapped.</param>
    /// <param name="cancellationToken">The token checked between row bands.</param>
    public void Equalize(ImageFrame[] group, CancellationToken cancellationToken)
    {
        if (!_adaptive)
        {
            Accumulate(group, cancellationToken);
            for (var plane = 0; plane < _planes; plane++)
            {
                BuildEqualizationTable(Histogram.Slice(plane * _levels, _levels), Tables.Slice(plane * _levels, _levels));
            }

            ApplyGlobal(group, cancellationToken);
            return;
        }

        for (var tileY = 0; tileY < _tilesY; tileY++)
        {
            for (var tileX = 0; tileX < _tilesX; tileX++)
            {
                Histogram.Clear();
                foreach (var frame in group)
                {
                    using var lease = frame.GetStorage().AcquireLease();
                    AddRegion(lease, _edgesX[tileX], _edgesX[tileX + 1], _edgesY[tileY], _edgesY[tileY + 1], cancellationToken);
                }

                for (var plane = 0; plane < _planes; plane++)
                {
                    BuildTileTable(Histogram.Slice(plane * _levels, _levels), Tables.Slice(GetTableOffset(tileX, tileY, plane), _levels));
                }
            }
        }

        foreach (var frame in group)
        {
            using var lease = frame.GetStorage().AcquireLease();
            ApplyAdaptive(lease, cancellationToken);
        }
    }

    /// <summary>Stretches the levels of a group of frames between two percentiles of their histograms.</summary>
    /// <param name="group">The frames whose histograms are combined, and that are remapped.</param>
    /// <param name="lowPercentile">The low percentile, from 0 to 100, less than <paramref name="highPercentile"/>.</param>
    /// <param name="highPercentile">The high percentile, from 0 to 100.</param>
    /// <param name="cancellationToken">The token checked between row bands.</param>
    public void AutoLevels(ImageFrame[] group, double lowPercentile, double highPercentile, CancellationToken cancellationToken)
    {
        Accumulate(group, cancellationToken);
        for (var plane = 0; plane < _planes; plane++)
        {
            BuildLevelsTable(Histogram.Slice(plane * _levels, _levels), Tables.Slice(plane * _levels, _levels), lowPercentile, highPercentile);
        }

        ApplyGlobal(group, cancellationToken);
    }

    public void Dispose()
    {
        _row?.Dispose();
        _row = null;
        _histogram?.Dispose();
        _histogram = null;
        _tables?.Dispose();
        _tables = null;
        _columnTiles?.Dispose();
        _columnTiles = null;
        _columnWeights?.Dispose();
        _columnWeights = null;
    }

    /// <summary>
    /// The textbook mapping of a global equalization: <c>round((cdf(v) - cdf_min) * max / (N - cdf_min))</c>, 0 below the
    /// lowest level present. A histogram of one level or of no sample gives the identity.
    /// </summary>
    internal static void BuildEqualizationTable(ReadOnlySpan<long> histogram, Span<ushort> table)
    {
        var max = table.Length - 1;
        var total = Sum(histogram);
        var lowest = histogram.IndexOfAnyExcept(0L);
        if (lowest < 0 || histogram[lowest] == total)
        {
            SetIdentity(table);
            return;
        }

        var minimum = histogram[lowest];
        var cumulative = 0L;
        for (var level = 0; level < table.Length; level++)
        {
            cumulative += histogram[level];
            table[level] = level < lowest ? (ushort)0 : Scale(cumulative - minimum, max, total - minimum);
        }
    }

    /// <summary>
    /// The mapping of an auto-levels: the levels between the low and the high percentiles are stretched linearly to the
    /// whole range (see <see cref="AutoLevelsOptions"/>).
    /// </summary>
    internal static void BuildLevelsTable(ReadOnlySpan<long> histogram, Span<ushort> table, double lowPercentile, double highPercentile)
    {
        var max = table.Length - 1;
        var total = Sum(histogram);
        var low = -1;
        var high = -1;
        if (total > 0)
        {
            var below = 0L;
            for (var level = 0; level <= max; level++)
            {
                below += histogram[level];
                if (below * 100d > total * lowPercentile)
                {
                    low = level;
                    break;
                }
            }

            var above = 0L;
            for (var level = max; level >= 0; level--)
            {
                above += histogram[level];
                if (above * 100d > total * (100 - highPercentile))
                {
                    high = level;
                    break;
                }
            }
        }

        if (high <= low)
        {
            SetIdentity(table);
            return;
        }

        for (var level = 0; level <= max; level++)
        {
            table[level] = level <= low ? (ushort)0 : level >= high ? (ushort)max : Scale(level - low, max, high - low);
        }
    }

    /// <summary>
    /// The mapping of a tile of an adaptive equalization: <c>round(cdf(v) * max / n)</c>, where the histogram is clipped in
    /// <see cref="ClipBinCount"/> bins (see <see cref="HistogramEqualizationOptions.ClipLimit"/>). A tile without counted
    /// sample gives the identity.
    /// </summary>
    /// <remarks>
    /// With 8-bit samples, each bin is one level. With 16-bit samples, each bin covers 256 levels: the samples it keeps
    /// after the clip follow the histogram of its levels, and the samples it gets from the excess are spread evenly over its
    /// levels. Without clip, the mapping is that of the histogram of the levels.
    /// </remarks>
    internal void BuildTileTable(Span<long> histogram, Span<ushort> table)
    {
        var total = Sum(histogram);
        if (total == 0)
        {
            SetIdentity(table);
            return;
        }

        var levelsPerBin = histogram.Length / ClipBinCount;
        if (levelsPerBin == 1)
        {
            ClipHistogram(histogram, total, _clipLimit);
            var cumulative = 0L;
            for (var level = 0; level < table.Length; level++)
            {
                cumulative += histogram[level];
                table[level] = Scale(cumulative, _max, total);
            }

            return;
        }

        Span<long> counts = stackalloc long[2 * ClipBinCount];
        var binCounts = counts[..ClipBinCount];
        var clippedCounts = counts[ClipBinCount..];
        for (var bin = 0; bin < ClipBinCount; bin++)
        {
            binCounts[bin] = Sum(histogram.Slice(bin * levelsPerBin, levelsPerBin));
        }

        binCounts.CopyTo(clippedCounts);
        var limit = ClipHistogram(clippedCounts, total, _clipLimit);

        // In units of 1 / (count * levelsPerBin) samples, the cdf of the i-th level of a bin of count samples is
        // before * count * levelsPerBin + kept * fine * levelsPerBin + added * (i + 1) * count, where fine is the number
        // of samples of the bin up to that level
        var before = 0L;
        for (var bin = 0; bin < ClipBinCount; bin++)
        {
            var count = (ulong)Math.Max(binCounts[bin], 1);
            var kept = Math.Min(binCounts[bin], limit);
            var added = clippedCounts[bin] - kept;
            var unit = (UInt128)count * (uint)levelsPerBin;
            var start = (UInt128)(ulong)before * unit;
            var denominator = (UInt128)(ulong)total * unit;
            var keptWeight = (UInt128)(ulong)kept * (uint)levelsPerBin;
            var addedWeight = (UInt128)(ulong)added * count;
            var fine = 0L;
            for (var i = 0; i < levelsPerBin; i++)
            {
                var level = (bin * levelsPerBin) + i;
                fine += histogram[level];
                table[level] = Scale(start + (keptWeight * (ulong)fine) + (addedWeight * (uint)(i + 1)), _max, denominator);
            }

            before += clippedCounts[bin];
        }
    }

    /// <summary>
    /// Clips the bins of a histogram of <paramref name="total"/> samples at <c>max(1, floor(clipLimit * total / bins))</c>
    /// and spreads the excess over every bin, as Zuiderveld does: each bin gets the same share, then the remainder
    /// <c>r</c> of the division is given one sample at a time to every <c>max(1, floor(bins / r))</c>-th bin, from bin 0.
    /// The total is kept.
    /// </summary>
    /// <returns>The largest number of samples a bin keeps, or <see cref="long.MaxValue"/> when nothing is clipped.</returns>
    internal static long ClipHistogram(Span<long> histogram, long total, double clipLimit)
    {
        var bins = histogram.Length;
        var limitValue = Math.Floor(clipLimit * total / bins);
        if (!(limitValue < total))
            return long.MaxValue;

        var limit = Math.Max(1L, (long)limitValue);
        var excess = 0L;
        for (var level = 0; level < bins; level++)
        {
            if (histogram[level] > limit)
            {
                excess += histogram[level] - limit;
                histogram[level] = limit;
            }
        }

        if (excess == 0)
            return limit;

        var share = excess / bins;
        var remainder = excess % bins;
        if (share > 0)
        {
            for (var level = 0; level < bins; level++)
            {
                histogram[level] += share;
            }
        }

        if (remainder > 0)
        {
            var step = Math.Max(1, bins / (int)remainder);
            for (var level = 0; level < bins && remainder > 0; level += step, remainder--)
            {
                histogram[level]++;
            }
        }

        return limit;
    }

    /// <summary>
    /// Finds the tiles whose centers surround a pixel along an axis: the pixel center <c>pos + 0.5</c> lies between the
    /// centers of tiles <paramref name="tile"/> and <c>tile + 1</c>, at <paramref name="weight"/> of the way. Before the
    /// first center and after the last one, the weight is 0 (the nearest tile alone).
    /// </summary>
    internal static void Locate(int position, ReadOnlySpan<int> edges, out int tile, out double weight)
    {
        // Doubled coordinates: the center of the pixel is 2 x + 1, the center of tile k is edges[k] + edges[k + 1]
        var count = edges.Length - 1;
        var point = (2L * position) + 1;
        tile = 0;
        weight = 0;
        if (count == 1 || point <= edges[0] + edges[1])
            return;

        if (point >= (long)edges[count - 1] + edges[count])
        {
            tile = count - 1;
            return;
        }

        while ((long)edges[tile + 1] + edges[tile + 2] <= point)
        {
            tile++;
        }

        long start = edges[tile] + edges[tile + 1];
        long end = edges[tile + 1] + edges[tile + 2];
        weight = (point - start) / (double)(end - start);
    }

    /// <summary>Gets the first pixel of each tile of an axis, followed by the length of the axis.</summary>
    private static int[] GetTileEdges(int length, int count)
    {
        var edges = new int[count + 1];
        for (var k = 0; k <= count; k++)
        {
            edges[k] = (int)((long)k * length / count);
        }

        return edges;
    }

    /// <summary>Gets <c>round(numerator * max / denominator)</c>, ties upward, for a numerator from 0 to the denominator.</summary>
    private static ushort Scale(long numerator, int max, long denominator)
        => Scale((UInt128)(ulong)numerator, max, (UInt128)(ulong)denominator);

    /// <summary>Gets <c>round(numerator * max / denominator)</c>, ties upward, for a numerator from 0 to the denominator.</summary>
    private static ushort Scale(UInt128 numerator, int max, UInt128 denominator)
        => (ushort)(((numerator * (uint)max * 2) + denominator) / (denominator * 2));

    private static long Sum(ReadOnlySpan<long> histogram)
    {
        var total = 0L;
        foreach (var count in histogram)
        {
            total += count;
        }

        return total;
    }

    private static void SetIdentity(Span<ushort> table)
    {
        for (var level = 0; level < table.Length; level++)
        {
            table[level] = (ushort)level;
        }
    }

    private static PooledBuffer GetBuffer(PooledBuffer? buffer) => buffer ?? throw new ObjectDisposedException(nameof(ToneCurveMapper));

    private int GetTableOffset(int tileX, int tileY, int plane) => ((((tileY * _tilesX) + tileX) * _planes) + plane) * _levels;

    /// <summary>Clears the histograms and counts every pixel of a group of frames.</summary>
    private void Accumulate(ImageFrame[] group, CancellationToken cancellationToken)
    {
        Histogram.Clear();
        foreach (var frame in group)
        {
            using var lease = frame.GetStorage().AcquireLease();
            AddRegion(lease, 0, _width, 0, _height, cancellationToken);
        }
    }

    /// <summary>Adds the pixels of a rectangle of a leased frame to the histograms, except the fully transparent ones.</summary>
    private void AddRegion(scoped in PixelLease lease, int left, int right, int top, int bottom, CancellationToken cancellationToken)
    {
        var histogram = Histogram;
        var width = right - left;
        var samples = Row[..(4 * width)];
        var luma = LumaRow[..width];

        // 8-bit levels are counted in partial counters (one set per plane), added up at the end of the region or before
        // they can overflow
        var partialCounts = _levels == 256;
        Span<uint> partial = partialCounts ? stackalloc uint[_planes * ToneSamples.PartialCountLength] : default;
        partial.Clear();
        var pending = 0L;
        for (var y = top; y < bottom; y++)
        {
            if ((y - top) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var pixels = lease.GetRowBytes(y)[(left * _bytesPerPixel)..(right * _bytesPerPixel)];
            if (_planes == 1)
            {
                // The samples are only read for the formats whose luma is not computed from the stored bytes
                ToneSamples.ReadLuma(_format, pixels, samples, luma);
            }
            else
            {
                ToneSamples.Read(_format, pixels, samples);
            }

            if (partialCounts)
            {
                // The fully transparent pixels are counted apart
                if (_hasAlpha)
                {
                    if (_planes == 1 && (_format is PixelFormat.Rgba32 or PixelFormat.Bgra32))
                    {
                        ToneSamples.MarkTransparent32(pixels, luma);
                    }
                    else
                    {
                        ToneSamples.MarkTransparent(samples, _planes == 1 ? luma : default);
                    }
                }

                if (_planes == 1)
                {
                    ToneSamples.CountBytes(luma, 1, partial[..ToneSamples.PartialCountLength]);
                }
                else
                {
                    ToneSamples.CountBytes(samples, 4, partial[..ToneSamples.PartialCountLength]);
                    ToneSamples.CountBytes(samples[1..], 4, partial.Slice(ToneSamples.PartialCountLength, ToneSamples.PartialCountLength));
                    ToneSamples.CountBytes(samples[2..], 4, partial.Slice(2 * ToneSamples.PartialCountLength, ToneSamples.PartialCountLength));
                }

                pending += width;
                if (pending > uint.MaxValue - (long)width)
                {
                    FlushPartialCounts(partial, histogram);
                    pending = 0;
                }

                continue;
            }

            for (var x = 0; x < width; x++)
            {
                if (_hasAlpha && samples[(4 * x) + 3] == 0)
                    continue;

                if (_planes == 1)
                {
                    histogram[luma[x]]++;
                }
                else
                {
                    histogram[samples[4 * x]]++;
                    histogram[_levels + samples[(4 * x) + 1]]++;
                    histogram[(2 * _levels) + samples[(4 * x) + 2]]++;
                }
            }
        }

        if (partialCounts)
        {
            FlushPartialCounts(partial, histogram);
        }
    }

    /// <summary>Adds the partial counters of every plane to the histograms and clears them.</summary>
    private void FlushPartialCounts(Span<uint> partial, Span<long> histogram)
    {
        for (var plane = 0; plane < _planes; plane++)
        {
            ToneSamples.FlushPartialCounts(partial.Slice(plane * ToneSamples.PartialCountLength, ToneSamples.PartialCountLength), histogram.Slice(plane * _levels, _levels));
        }
    }

    /// <summary>Remaps every pixel of a group of frames with the table of each plane.</summary>
    private void ApplyGlobal(ImageFrame[] group, CancellationToken cancellationToken)
    {
        foreach (var frame in group)
        {
            using var lease = frame.GetStorage().AcquireLease();
            var tables = Tables;
            var samples = Row;
            var luma = LumaRow;
            if (_planes == 1 && (_format is PixelFormat.Rgba32 or PixelFormat.Bgra32) && Vector128.IsHardwareAccelerated && !Resampler.TestForceScalar)
            {
                ApplyGlobalLuma32(lease, tables[.._levels], cancellationToken);
                continue;
            }

            for (var y = 0; y < _height; y++)
            {
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var row = lease.GetRowBytes(y)[..(_width * _bytesPerPixel)];
                ToneSamples.Read(_format, row, samples);
                if (_planes == 1)
                {
                    ToneSamples.ComputeLuma(samples, luma);
                    var table = tables[.._levels];
                    if (_levels == 256)
                    {
                        // The changes of 8-bit lumas fit in 16 bits
                        for (var x = 0; x < luma.Length; x++)
                        {
                            int level = luma[x];
                            luma[x] = (ushort)(table[level] - level);
                        }

                        ShiftBytes(samples, unsafe(MemoryMarshal.Cast<ushort, short>(luma)));
                    }
                    else
                    {
                        for (var x = 0; x < luma.Length; x++)
                        {
                            int level = luma[x];
                            Shift(samples.Slice(4 * x, 3), table[level] - level);
                        }
                    }
                }
                else
                {
                    var red = tables[.._levels];
                    var green = tables.Slice(_levels, _levels);
                    var blue = tables.Slice(2 * _levels, _levels);
                    for (var i = 0; i < samples.Length; i += 4)
                    {
                        samples[i] = red[samples[i]];
                        samples[i + 1] = green[samples[i + 1]];
                        samples[i + 2] = blue[samples[i + 2]];
                    }
                }

                ToneSamples.WriteColors(_format, samples, row);
            }
        }
    }

    /// <summary>
    /// Remaps every <see cref="PixelFormat.Rgba32"/> or <see cref="PixelFormat.Bgra32"/> pixel of a leased frame with the
    /// table of the luma, in place: the change of luma of each level is stored as a saturated increase and decrease of the
    /// three colors (<see cref="ToneSamples.ShiftColors32"/>).
    /// </summary>
    private void ApplyGlobalLuma32(scoped in PixelLease lease, ReadOnlySpan<ushort> table, CancellationToken cancellationToken)
    {
        Span<uint> changes = stackalloc uint[2 * 256];
        var increasesOfLevel = changes[..256];
        var decreasesOfLevel = changes[256..];
        for (var level = 0; level < 256; level++)
        {
            var change = table[level] - level;
            increasesOfLevel[level] = (uint)Math.Max(change, 0) * 0x010101u;
            decreasesOfLevel[level] = (uint)Math.Max(-change, 0) * 0x010101u;
        }

        // The samples of the row hold the increases and the decreases of its pixels
        var samples = Row;
        var luma = LumaRow;
        var shifts = unsafe(MemoryMarshal.Cast<ushort, uint>(samples));
        var increases = shifts[.._width];
        var decreases = shifts.Slice(_width, _width);
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var row = lease.GetRowBytes(y)[..(4 * _width)];
            ToneSamples.ReadLuma(_format, row, samples, luma);
            for (var x = 0; x < luma.Length; x++)
            {
                var level = (byte)luma[x];
                increases[x] = increasesOfLevel[level];
                decreases[x] = decreasesOfLevel[level];
            }

            ToneSamples.ShiftColors32(row, increases, decreases);
        }
    }

    /// <summary>Remaps every pixel of a leased frame with the bilinear interpolation of the tables of the four nearest tiles.</summary>
    private void ApplyAdaptive(scoped in PixelLease lease, CancellationToken cancellationToken)
    {
        var tables = Tables;
        var samples = Row;
        var lumaRow = LumaRow;
        var columnTiles = ColumnTiles;
        var columnWeights = ColumnWeights;
        for (var y = 0; y < _height; y++)
        {
            if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            Locate(y, _edgesY, out var top, out var weightY);
            var bottom = Math.Min(top + 1, _tilesY - 1);
            var row = lease.GetRowBytes(y)[..(_width * _bytesPerPixel)];
            ToneSamples.Read(_format, row, samples);
            if (_planes == 1)
            {
                ToneSamples.ComputeLuma(samples, lumaRow);
            }

            for (var x = 0; x < _width; x++)
            {
                var left = columnTiles[x];
                var right = Math.Min(left + 1, _tilesX - 1);
                var weightX = columnWeights[x];
                var i = 4 * x;
                if (_planes == 1)
                {
                    int luma = lumaRow[x];
                    Shift(samples.Slice(i, 3), Interpolate(tables, left, right, top, bottom, 0, luma, weightX, weightY) - luma);
                }
                else
                {
                    for (var plane = 0; plane < 3; plane++)
                    {
                        samples[i + plane] = (ushort)Interpolate(tables, left, right, top, bottom, plane, samples[i + plane], weightX, weightY);
                    }
                }
            }

            ToneSamples.WriteColors(_format, samples, row);
        }
    }

    /// <summary>Interpolates the mappings of a level by four tiles, rounded to the nearest level, ties upward.</summary>
    private int Interpolate(ReadOnlySpan<ushort> tables, int left, int right, int top, int bottom, int plane, int level, double weightX, double weightY)
    {
        var upper = (tables[GetTableOffset(left, top, plane) + level] * (1 - weightX)) + (tables[GetTableOffset(right, top, plane) + level] * weightX);
        var lower = (tables[GetTableOffset(left, bottom, plane) + level] * (1 - weightX)) + (tables[GetTableOffset(right, bottom, plane) + level] * weightX);
        var value = (upper * (1 - weightY)) + (lower * weightY);
        return (int)Math.Min(value + 0.5, _max);
    }

    /// <summary>
    /// Adds a change of luma to the three colors of every pixel of 8-bit samples, clamped to the sample range; the alpha is
    /// left as it is.
    /// </summary>
    /// <param name="samples">The samples, four per pixel, at most 255.</param>
    /// <param name="changes">The change of each pixel, from -255 to 255.</param>
    internal static void ShiftBytes(Span<ushort> samples, ReadOnlySpan<short> changes)
    {
        var count = samples.Length / 4;
        var x = 0;
        if (Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian && !Resampler.TestForceScalar)
        {
            ref var start = ref unsafe(Unsafe.As<ushort, short>(ref MemoryMarshal.GetReference(samples)));
            ref var deltas = ref unsafe(MemoryMarshal.GetReference(changes[..count]));
            var max = Vector128.Create((short)255);

            // Constant indices in range keep the shuffle a single instruction; the alpha lanes get no change
            var first = Vector128.Create((short)0, 0, 0, 0, 1, 1, 1, 1);
            var second = Vector128.Create((short)2, 2, 2, 2, 3, 3, 3, 3);
            var colors = Vector128.Create(-1, -1, -1, 0, -1, -1, -1, 0);
            for (; x + 4 <= count; x += 4)
            {
                var change = unsafe(Vector128.CreateScalarUnsafe(unsafe(Unsafe.ReadUnaligned<ulong>(ref Unsafe.As<short, byte>(ref Unsafe.Add(ref deltas, x)))))).AsInt16();
                var low = unsafe(Vector128.LoadUnsafe(ref start, (nuint)(4 * x))) + (Vector128.Shuffle(change, first) & colors);
                var high = unsafe(Vector128.LoadUnsafe(ref start, (nuint)((4 * x) + 8))) + (Vector128.Shuffle(change, second) & colors);
                unsafe { Vector128.Min(Vector128.Max(low, Vector128<short>.Zero), max).StoreUnsafe(ref start, (nuint)(4 * x)); }
                unsafe { Vector128.Min(Vector128.Max(high, Vector128<short>.Zero), max).StoreUnsafe(ref start, (nuint)((4 * x) + 8)); }
            }
        }

        ShiftBytesScalar(samples[(4 * x)..], changes[x..count]);
    }

    /// <summary>The scalar reference of <see cref="ShiftBytes"/>.</summary>
    internal static void ShiftBytesScalar(Span<ushort> samples, ReadOnlySpan<short> changes)
    {
        for (var x = 0; x < changes.Length; x++)
        {
            for (var c = 0; c < 3; c++)
            {
                samples[(4 * x) + c] = (ushort)Math.Clamp(samples[(4 * x) + c] + changes[x], 0, byte.MaxValue);
            }
        }
    }

    /// <summary>Adds a change of luma to the three colors of a pixel, clamped to the sample range.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Shift(Span<ushort> colors, int delta)
    {
        colors[0] = (ushort)Math.Clamp(colors[0] + delta, 0, _max);
        colors[1] = (ushort)Math.Clamp(colors[1] + delta, 0, _max);
        colors[2] = (ushort)Math.Clamp(colors[2] + delta, 0, _max);
    }
}
