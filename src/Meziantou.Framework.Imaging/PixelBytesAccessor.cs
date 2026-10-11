using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>Scoped access to the raw bytes of the rows of a frame, valid only during a <see cref="ImageFrame.ProcessPixelBytes(PixelBytesAction)"/> callback.</summary>
/// <remarks>
/// Each row is contiguous and contains exactly <see cref="RowLength"/> bytes of visible pixels; 16-bit components are in
/// native endianness. The accessor is bound to the lease of the callback: once the callback returns,
/// <see cref="GetRowSpan(int)"/> throws an <see cref="InvalidOperationException"/>. A default instance has no rows.
/// </remarks>
public readonly ref struct PixelBytesAccessor
{
    private readonly PixelStorage? _storage;
    private readonly long _token;

    internal PixelBytesAccessor(PixelStorage storage, long token, PixelFormat pixelFormat)
    {
        _storage = storage;
        _token = token;
        PixelFormat = pixelFormat;
    }

    /// <summary>Gets the number of pixels per row.</summary>
    public int Width => _storage?.Width ?? 0;

    /// <summary>Gets the number of rows.</summary>
    public int Height => _storage?.Height ?? 0;

    /// <summary>Gets the pixel format describing the byte layout.</summary>
    public PixelFormat PixelFormat { get; }

    /// <summary>Gets the number of visible bytes per row (<c>Width * bytesPerPixel</c>).</summary>
    public int RowLength => _storage?.RowLength ?? 0;

    /// <summary>Gets the bytes of a row of visible pixels.</summary>
    /// <param name="y">The row index, from 0 to <c>Height - 1</c>.</param>
    /// <returns>A span of exactly <see cref="RowLength"/> bytes, valid until the callback returns.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the accessor has returned.</exception>
    public Span<byte> GetRowSpan(int y)
    {
        var storage = _storage;
        if (storage is null || storage.LeaseToken != _token)
            ThrowNotActive();

        return storage.GetRowSpanCore(y);
    }

    [DoesNotReturn]
    private static void ThrowNotActive()
        => throw new InvalidOperationException("The pixel accessor is only valid during the callback that received it.");
}
