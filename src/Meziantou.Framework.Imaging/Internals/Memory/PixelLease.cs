using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// An exclusive scoped lease on a <see cref="PixelStorage"/>, the only way to reach its rows.
/// Use it with <see langword="using"/> so that it is released in a <see langword="finally"/> even when a callback throws.
/// Releasing is idempotent: copies of a released lease (and a lease whose storage was re-leased) are inert.
/// </summary>
/// <remarks>Spans returned by a lease are valid only while it is active; the lease being a ref struct keeps it on the stack.</remarks>
internal ref struct PixelLease
{
    private PixelStorage? _storage;

    internal PixelLease(PixelStorage storage, long token)
    {
        _storage = storage;
        Token = token;
    }

    /// <summary>Gets a value indicating whether the lease is active.</summary>
    public readonly bool IsActive => _storage is not null && _storage.LeaseToken == Token;

    /// <summary>Gets the leased storage.</summary>
    /// <exception cref="InvalidOperationException">The lease is not active.</exception>
    public readonly PixelStorage Storage => GetActiveStorage();

    public readonly int Width => GetActiveStorage().Width;

    public readonly int Height => GetActiveStorage().Height;

    /// <summary>Gets the number of visible bytes per row.</summary>
    public readonly int RowLength => GetActiveStorage().RowLength;

    internal readonly long Token { get; }

    /// <summary>Gets the visible bytes of a row.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">The lease is not active.</exception>
    public readonly Span<byte> GetRowBytes(int y) => GetActiveStorage().GetRowSpanCore(y);

    /// <summary>Gets the visible pixels of a row.</summary>
    /// <typeparam name="TPixel">The pixel type; its size must be the storage pixel size.</typeparam>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="y"/> is out of range.</exception>
    /// <exception cref="InvalidOperationException">The lease is not active, or <typeparamref name="TPixel"/> does not match the pixel size.</exception>
    public readonly Span<TPixel> GetRow<TPixel>(int y)
        where TPixel : unmanaged
    {
        var storage = GetActiveStorage();
        if (Unsafe.SizeOf<TPixel>() != storage.BytesPerPixel)
            throw new InvalidOperationException($"The pixel type '{typeof(TPixel).Name}' does not match the storage pixel size ({storage.BytesPerPixel} bytes).");

        return unsafe(MemoryMarshal.Cast<byte, TPixel>(storage.GetRowSpanCore(y)));
    }

    /// <summary>Zeroes every visible row.</summary>
    public readonly void Clear()
    {
        var storage = GetActiveStorage();
        for (var y = 0; y < storage.Height; y++)
        {
            storage.GetRowSpanCore(y).Clear();
        }
    }

    /// <summary>Copies the visible rows to another leased storage of the same geometry. Copying a storage onto itself does nothing.</summary>
    /// <exception cref="ArgumentException">The geometries differ.</exception>
    public readonly void CopyTo(scoped in PixelLease destination)
    {
        var source = GetActiveStorage();
        var target = destination.GetActiveStorage();
        if (source.Width != target.Width || source.Height != target.Height || source.BytesPerPixel != target.BytesPerPixel)
            throw new ArgumentException("The destination storage has a different geometry.", nameof(destination));

        if (ReferenceEquals(source, target))
            return;

        for (var y = 0; y < source.Height; y++)
        {
            source.GetRowSpanCore(y).CopyTo(target.GetRowSpanCore(y));
        }
    }

    /// <summary>Releases the lease. Idempotent.</summary>
    public void Dispose()
    {
        var storage = _storage;
        if (storage is null)
            return;

        _storage = null;
        storage.Owner.ReleaseLease(storage, Token);
    }

    private readonly PixelStorage GetActiveStorage()
    {
        var storage = _storage;
        if (storage is null || storage.LeaseToken != Token)
            ThrowNotActive();

        return storage;
    }

    [DoesNotReturn]
    private static void ThrowNotActive() => throw new InvalidOperationException("The pixel lease is no longer active.");
}
