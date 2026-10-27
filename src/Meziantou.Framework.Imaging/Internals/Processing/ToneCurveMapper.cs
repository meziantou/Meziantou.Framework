using System.Runtime.InteropServices;

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

    private Span<ushort> Row => unsafe(MemoryMarshal.Cast<byte, ushort>(GetBuffer(_row).Span))[..(4 * _width)];

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
    /// The mapping of a tile of an adaptive equalization: the histogram is clipped (see
    /// <see cref="HistogramEqualizationOptions.ClipLimit"/>), then <c>round(cdf(v) * max / n)</c>. A tile without counted
    /// sample gives the identity.
    /// </summary>
    internal void BuildTileTable(Span<long> histogram, Span<ushort> table)
    {
        var total = Sum(histogram);
        if (total == 0)
        {
            SetIdentity(table);
            return;
        }

        ClipHistogram(histogram, total, _clipLimit);
        var cumulative = 0L;
        for (var level = 0; level < table.Length; level++)
        {
            cumulative += histogram[level];
            table[level] = Scale(cumulative, _max, total);
        }
    }

    /// <summary>
    /// Clips the bins of a histogram of <paramref name="total"/> samples at <c>max(1, floor(clipLimit * total / bins))</c>
    /// and spreads the excess over every bin, as Zuiderveld does: each bin gets the same share, then the remainder
    /// <c>r</c> of the division is given one sample at a time to every <c>max(1, floor(bins / r))</c>-th bin, from bin 0.
    /// The total is kept.
    /// </summary>
    internal static void ClipHistogram(Span<long> histogram, long total, double clipLimit)
    {
        var bins = histogram.Length;
        var limitValue = Math.Floor(clipLimit * total / bins);
        if (!(limitValue < total))
            return;

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
            return;

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
        => (ushort)((((UInt128)(ulong)numerator * (uint)max * 2) + (ulong)denominator) / ((UInt128)(ulong)denominator * 2));

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
        var samples = Row[..(4 * (right - left))];
        for (var y = top; y < bottom; y++)
        {
            if ((y - top) % ProcessingKernels.RowsPerCancellationCheck == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ToneSamples.Read(_format, lease.GetRowBytes(y)[(left * _bytesPerPixel)..(right * _bytesPerPixel)], samples);
            for (var i = 0; i < samples.Length; i += 4)
            {
                if (_hasAlpha && samples[i + 3] == 0)
                    continue;

                if (_planes == 1)
                {
                    histogram[(int)PixelConverter.Luma(samples[i], samples[i + 1], samples[i + 2], _max)]++;
                }
                else
                {
                    histogram[samples[i]]++;
                    histogram[_levels + samples[i + 1]]++;
                    histogram[(2 * _levels) + samples[i + 2]]++;
                }
            }
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
            for (var y = 0; y < _height; y++)
            {
                if (y % ProcessingKernels.RowsPerCancellationCheck == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var row = lease.GetRowBytes(y)[..(_width * _bytesPerPixel)];
                ToneSamples.Read(_format, row, samples);
                for (var i = 0; i < samples.Length; i += 4)
                {
                    if (_planes == 1)
                    {
                        var luma = (int)PixelConverter.Luma(samples[i], samples[i + 1], samples[i + 2], _max);
                        Shift(samples.Slice(i, 3), tables[luma] - luma);
                    }
                    else
                    {
                        samples[i] = tables[samples[i]];
                        samples[i + 1] = tables[_levels + samples[i + 1]];
                        samples[i + 2] = tables[(2 * _levels) + samples[i + 2]];
                    }
                }

                ToneSamples.WriteColors(_format, samples, row);
            }
        }
    }

    /// <summary>Remaps every pixel of a leased frame with the bilinear interpolation of the tables of the four nearest tiles.</summary>
    private void ApplyAdaptive(scoped in PixelLease lease, CancellationToken cancellationToken)
    {
        var tables = Tables;
        var samples = Row;
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
            for (var x = 0; x < _width; x++)
            {
                var left = columnTiles[x];
                var right = Math.Min(left + 1, _tilesX - 1);
                var weightX = columnWeights[x];
                var i = 4 * x;
                if (_planes == 1)
                {
                    var luma = (int)PixelConverter.Luma(samples[i], samples[i + 1], samples[i + 2], _max);
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

    /// <summary>Adds a change of luma to the three colors of a pixel, clamped to the sample range.</summary>
    private void Shift(Span<ushort> colors, int delta)
    {
        for (var c = 0; c < colors.Length; c++)
        {
            colors[c] = (ushort)Math.Clamp(colors[c] + delta, 0, _max);
        }
    }
}
