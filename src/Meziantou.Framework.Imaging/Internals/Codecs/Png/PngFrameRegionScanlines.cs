namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The scanlines of a rectangle of a frame in the layout of its pixel format (<see cref="PngEncodedLayout"/>, palette
/// indexes included), for the APNG frames of <see cref="Formats.PngEncoder.OptimizeFrames"/>. With an OVER plan, the pixels
/// equal to the canvas they are drawn on (<see cref="AnimationFrameDelta.IsUnchanged"/>) are written as the transparent
/// value of the layout: zero samples (an alpha channel), or the first palette entry (fully transparent in a frozen palette
/// that has a transparent entry).
/// </summary>
internal sealed class PngFrameRegionScanlines : PngScanlineSource, IDisposable
{
    private readonly PngIndexedRows? _indexedRows;
    private PooledBuffer? _samples;
    private PooledBuffer? _mask;
    private PngEncodedLayout? _layout;
    private PixelStorage? _storage;
    private PngFrameRows? _wideRows;
    private AnimationFrameDelta? _over;
    private Rectangle _region;

    /// <summary>Creates the source of the regions of frames of one width, and rents its scratch.</summary>
    /// <param name="scope">The writer scope, charged for every buffer.</param>
    /// <param name="pixelFormat">The stored pixel format.</param>
    /// <param name="width">The canvas width.</param>
    /// <param name="indexedRows">The palette of indexed output (borrowed), or <see langword="null"/>.</param>
    /// <exception cref="ImageResourceLimitException">The buffers exceed the allocation limit.</exception>
    public PngFrameRegionScanlines(AllocationScope scope, PixelFormat pixelFormat, int width, PngIndexedRows? indexedRows)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _indexedRows = indexedRows;
        var length = (long)width * Math.Max(PixelFormats.GetBytesPerPixel(pixelFormat), 1);
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        try
        {
            _samples = scope.Rent((int)length, AllocationKind.Temporary, clear: false);
            _mask = scope.Rent(width, AllocationKind.Temporary, clear: false);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override int Width => _region.Width;

    public override int Height => _region.Height;

    public override int FilterDistance => Layout.BytesPerPixel;

    private PngEncodedLayout Layout => _layout ?? throw new InvalidOperationException("No region is set.");

    /// <summary>Sets the region that is read.</summary>
    /// <param name="layout">The encoded layout.</param>
    /// <param name="storage">The pixels of the frame (borrowed).</param>
    /// <param name="region">The rectangle, inside the frame.</param>
    /// <param name="wideRows">The wide rows of the same frame, which the comparison with the canvas reads (borrowed); <see langword="null"/> to write every pixel.</param>
    /// <param name="over">The animation whose planned canvas the frame is drawn on with OVER, or <see langword="null"/> to write every pixel.</param>
    public void Set(PngEncodedLayout layout, PixelStorage storage, Rectangle region, PngFrameRows? wideRows, AnimationFrameDelta? over)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(storage);
        if (layout.IsIndexed && _indexedRows is null)
            throw new ArgumentException("An indexed layout needs the palette.", nameof(layout));

        _layout = layout;
        _storage = storage;
        _region = region;
        (_wideRows, _over) = over is null ? (null, null) : (wideRows ?? throw new ArgumentNullException(nameof(wideRows)), over);
    }

    public override int GetScanlineLength(int width) => Layout.GetScanlineLength(width);

    public override void WriteScanline(int y, int x0, int dx, int count, Span<byte> scanline)
    {
        ObjectDisposedException.ThrowIf(_samples is null, this);
        var layout = Layout;
        var row = _region.Y + y;
        var start = _region.X + x0;

        // The unchanged pixels, from the wide rows (their own lease, released before the stored row is read)
        var mask = Span<byte>.Empty;
        if (_over is { } over)
        {
            mask = _mask!.Span[..count];
            var wide = _wideRows!.GetRow(row);
            var canvas = over.GetPlannedCanvasRow(row, out var clearedStart, out var clearedEnd);
            for (int i = 0, x = start; i < count; i++, x += dx)
            {
                mask[i] = over.IsUnchanged(wide, x, canvas, clearedStart, clearedEnd) ? (byte)1 : (byte)0;
            }
        }

        using var lease = _storage!.AcquireLease();
        var source = lease.GetRowBytes(row);
        if (layout.IsIndexed)
        {
            var indexes = layout.BitDepth == 8 ? scanline[..count] : _samples.Span[..count];
            _indexedRows!.Map(source, start, dx, indexes);
            for (var i = 0; i < mask.Length; i++)
            {
                if (mask[i] != 0)
                {
                    indexes[i] = 0;
                }
            }

            if (layout.BitDepth != 8)
            {
                layout.PackIndexes(indexes, scanline);
            }

            return;
        }

        var bytesPerPixel = layout.BytesPerPixel;
        var span = ((count - 1) * dx) + 1;
        if (dx == 1)
        {
            layout.ToSamples(source.Slice(start * bytesPerPixel, span * bytesPerPixel), scanline);
        }
        else
        {
            var samples = _samples.Span[..(span * bytesPerPixel)];
            layout.ToSamples(source.Slice(start * bytesPerPixel, span * bytesPerPixel), samples);
            for (var i = 0; i < count; i++)
            {
                samples.Slice(i * dx * bytesPerPixel, bytesPerPixel).CopyTo(scanline[(i * bytesPerPixel)..]);
            }
        }

        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] != 0)
            {
                scanline.Slice(i * bytesPerPixel, bytesPerPixel).Clear();
            }
        }
    }

    public void Dispose()
    {
        _storage = null;
        _wideRows = null;
        _over = null;
        _layout = null;
        _samples?.Dispose();
        _samples = null;
        _mask?.Dispose();
        _mask = null;
    }
}
