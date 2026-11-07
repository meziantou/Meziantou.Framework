namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The scanlines of a rectangle of wide rows in a <see cref="PngOptimizedLayout"/>: the image of a still PNG, a poster, or
/// an APNG frame region. With an OVER reference, the pixels equal to the canvas they are drawn on are written as the
/// transparent value of the layout, which <c>APNG_BLEND_OP_OVER</c> leaves unchanged.
/// </summary>
internal sealed class PngRegionScanlines : PngScanlineSource, IDisposable
{
    private PooledBuffer? _mask;
    private PooledBuffer? _unpacked;
    private PngOptimizedLayout? _layout;
    private PngWideRows? _rows;
    private Rectangle _region;
    private PngCanvasReference? _over;

    /// <summary>Creates the source of regions at most <paramref name="maxWidth"/> pixels wide, and rents its scratch.</summary>
    /// <param name="scope">The writer scope, charged for every buffer.</param>
    /// <param name="maxWidth">The canvas width.</param>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    public PngRegionScanlines(AllocationScope scope, int maxWidth)
    {
        ArgumentNullException.ThrowIfNull(scope);
        try
        {
            _mask = scope.Rent(maxWidth, AllocationKind.Temporary, clear: false);
            _unpacked = scope.Rent(maxWidth, AllocationKind.Temporary, clear: false);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override int Width => _region.Width;

    public override int Height => _region.Height;

    public override int FilterDistance => Layout.FilterDistance;

    private PngOptimizedLayout Layout => _layout ?? throw new InvalidOperationException("No region is set.");

    /// <summary>Sets the region that is read.</summary>
    /// <param name="layout">The layout of the samples.</param>
    /// <param name="rows">The wide rows of the canvas or of the frame (borrowed).</param>
    /// <param name="region">The rectangle, inside the rows.</param>
    /// <param name="over">The canvas of an OVER frame, or <see langword="null"/> to write every pixel.</param>
    public void Set(PngOptimizedLayout layout, PngWideRows rows, Rectangle region, PngCanvasReference? over)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(rows);
        if (over is not null && !layout.HasTransparentValue)
            throw new ArgumentException("An OVER frame needs a layout with a transparent value.", nameof(over));

        _layout = layout;
        _rows = rows;
        _region = region;
        _over = over;
    }

    public override int GetScanlineLength(int width) => Layout.GetScanlineLength(width);

    public override void WriteScanline(int y, int x0, int dx, int count, Span<byte> scanline)
    {
        ObjectDisposedException.ThrowIf(_mask is null, this);
        var layout = Layout;
        var row = _rows!.GetRow(_region.Y + y);
        var start = _region.X + x0;
        ReadOnlySpan<byte> mask = default;
        if (_over is { } over)
        {
            var unchanged = _mask.Span[..count];
            var reference = over.GetRow(_region.Y + y, out var clearedStart, out var clearedEnd);
            var bytesPerPixel = layout.WideBytesPerPixel;
            for (int i = 0, x = start; i < count; i++, x += dx)
            {
                var pixel = row.Slice(x * bytesPerPixel, bytesPerPixel);
                var equal = x >= clearedStart && x < clearedEnd ? !pixel.ContainsAnyExcept((byte)0) : pixel.SequenceEqual(reference.Slice(x * bytesPerPixel, bytesPerPixel));
                unchanged[i] = equal ? (byte)1 : (byte)0;
            }

            mask = unchanged;
        }

        layout.WriteSamples(row, start, dx, count, mask, _unpacked!.Span, scanline);
    }

    public void Dispose()
    {
        _rows = null;
        _layout = null;
        _unpacked?.Dispose();
        _unpacked = null;
        _mask?.Dispose();
        _mask = null;
    }
}
