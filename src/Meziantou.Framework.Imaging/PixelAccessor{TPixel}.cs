using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>Scoped access to the rows of a frame, valid only during a <see cref="ImageFrame{TPixel}.ProcessPixelRows(PixelRowsAction{TPixel})"/> callback.</summary>
/// <typeparam name="TPixel">The pixel type.</typeparam>
/// <remarks>
/// Each row is contiguous and contains exactly <see cref="Width"/> visible pixels; consecutive rows are not guaranteed to be
/// adjacent in memory. The accessor is bound to the lease of the callback: once the callback returns, <see cref="GetRowSpan(int)"/>
/// throws an <see cref="InvalidOperationException"/>. A default instance has no rows.
/// </remarks>
public readonly ref struct PixelAccessor<TPixel>
    where TPixel : unmanaged
{
    private readonly PixelStorage? _storage;
    private readonly long _token;

    internal PixelAccessor(PixelStorage storage, long token)
    {
        _storage = storage;
        _token = token;
    }

    /// <summary>Gets the number of pixels per row.</summary>
    public int Width => _storage?.Width ?? 0;

    /// <summary>Gets the number of rows.</summary>
    public int Height => _storage?.Height ?? 0;

    /// <summary>Gets a row of visible pixels.</summary>
    /// <param name="y">The row index, from 0 to <c>Height - 1</c>.</param>
    /// <returns>A span of exactly <see cref="Width"/> pixels, valid until the callback returns.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the accessor has returned.</exception>
    public Span<TPixel> GetRowSpan(int y)
    {
        var storage = _storage;
        if (storage is null || storage.LeaseToken != _token)
            ThrowNotActive();

        return unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y)));
    }

    [DoesNotReturn]
    private static void ThrowNotActive()
        => throw new InvalidOperationException("The pixel accessor is only valid during the callback that received it.");
}
