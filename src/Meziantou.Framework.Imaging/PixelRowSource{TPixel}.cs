using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging;

/// <summary>
/// Read-only access to the rows of a frame as they were before a neighborhood operation, valid only during
/// <see cref="IPixelNeighborhoodOperation{TPixel}.Invoke"/>.
/// </summary>
/// <typeparam name="TPixel">The pixel type of the rows.</typeparam>
/// <remarks>
/// The rows are a copy of the frame, so they never show the rows the operation already wrote. Each row contains exactly
/// <see cref="Width"/> pixels; consecutive rows are not guaranteed to be adjacent in memory. A default instance has no rows.
/// </remarks>
public readonly ref struct PixelRowSource<TPixel>
    where TPixel : unmanaged
{
    private readonly PixelStorage? _storage;
    private readonly long _token;

    internal PixelRowSource(PixelStorage storage, long token)
    {
        _storage = storage;
        _token = token;
    }

    /// <summary>Gets the number of pixels per row.</summary>
    public int Width => _storage?.Width ?? 0;

    /// <summary>Gets the number of rows.</summary>
    public int Height => _storage?.Height ?? 0;

    /// <summary>Gets a row.</summary>
    /// <param name="y">The row index, from 0 to <c>Height - 1</c>.</param>
    /// <returns>A span of exactly <see cref="Width"/> pixels, valid until the operation returns.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">The operation that received the source has returned.</exception>
    public ReadOnlySpan<TPixel> GetRow(int y)
    {
        var storage = _storage;
        if (storage is null || storage.LeaseToken != _token)
            ThrowNotActive();

        return unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y)));
    }

    /// <summary>Gets a row, the nearest one when the index is outside the frame (the edge rows are repeated).</summary>
    /// <param name="y">The row index, which may be negative or past the last row.</param>
    /// <returns>A span of exactly <see cref="Width"/> pixels, valid until the operation returns.</returns>
    /// <exception cref="InvalidOperationException">The operation that received the source has returned.</exception>
    public ReadOnlySpan<TPixel> GetRowClamped(int y) => GetRow(Math.Clamp(y, 0, Math.Max(Height - 1, 0)));

    [DoesNotReturn]
    private static void ThrowNotActive()
        => throw new InvalidOperationException("The row source is only valid during the operation that received it.");
}
