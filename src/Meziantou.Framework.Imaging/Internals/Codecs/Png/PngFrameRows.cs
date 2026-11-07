namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The rows of a frame converted to the wide format: each row is leased, converted exactly to <see cref="PixelFormat.Rgba32"/>
/// or <see cref="PixelFormat.Rgba64"/> into a private row, and released.
/// </summary>
internal sealed class PngFrameRows : PngWideRows, IDisposable
{
    private readonly PixelFormat _wideFormat;
    private PooledBuffer? _row;
    private PixelStorage? _storage;
    private PixelFormat _pixelFormat;

    /// <summary>Creates the converter of rows of one width, and rents its row.</summary>
    /// <param name="scope">The writer scope, charged for the row.</param>
    /// <param name="wideFormat">The wide format.</param>
    /// <param name="width">The width of the frames.</param>
    /// <exception cref="ImageResourceLimitException">The row exceeds the allocation limit.</exception>
    public PngFrameRows(AllocationScope scope, PixelFormat wideFormat, int width)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _wideFormat = wideFormat;
        var length = (long)width * PixelFormats.GetBytesPerPixel(wideFormat);
        if (length > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        _row = scope.Rent((int)length, AllocationKind.Temporary, clear: false);
        Width = width;
    }

    public override int Width { get; }

    public override int Height => Storage.Height;

    private PixelStorage Storage => _storage ?? throw new InvalidOperationException("No frame is set.");

    /// <summary>Sets the frame whose rows are read.</summary>
    /// <param name="frame">The frame (borrowed), or <see langword="null"/> to release it.</param>
    public void SetFrame(ImageFrame? frame)
    {
        _storage = frame?.GetStorage();
        _pixelFormat = frame?.PixelFormat ?? default;
    }

    public override ReadOnlySpan<byte> GetRow(int y)
    {
        ObjectDisposedException.ThrowIf(_row is null, this);
        var destination = _row.Span;
        using var lease = Storage.AcquireLease();
        PixelConverter.ConvertRow(_pixelFormat, lease.GetRowBytes(y), _wideFormat, destination);
        return destination;
    }

    public void Dispose()
    {
        _storage = null;
        _row?.Dispose();
        _row = null;
    }
}
