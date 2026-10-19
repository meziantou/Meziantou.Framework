using System.Buffers;

namespace Meziantou.Framework.Fonts;

/// <summary>Rents the buffers of an operation from the shared array pool, within a number of live bytes.</summary>
/// <remarks>The allocator is thread-safe: the buffers can be rented and returned by several threads.</remarks>
internal sealed class PooledFontAllocator : FontAllocator
{
    private long _liveBytes;

    public PooledFontAllocator(long limit)
    {
        Limit = limit;
    }

    public override long Limit { get; }

    /// <summary>Gets the number of bytes rented and not returned.</summary>
    public long LiveBytes => Interlocked.Read(ref _liveBytes);

    public override FontBuffer Rent(int length, bool clear)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        var live = Interlocked.Add(ref _liveBytes, length);
        if (live > Limit)
        {
            Interlocked.Add(ref _liveBytes, -length);
            throw new FontResourceLimitException(FontResourceLimitKind.LiveAllocationBytes, Limit, live);
        }

        var array = ArrayPool<byte>.Shared.Rent(length);
        if (clear)
        {
            array.AsSpan(0, length).Clear();
        }

        return new Buffer(this, array, length);
    }

    private sealed class Buffer(PooledFontAllocator allocator, byte[] array, int length) : FontBuffer
    {
        private byte[]? _array = array;

        public override int Length { get; } = length;

        public override Span<byte> Span => (Volatile.Read(ref _array) ?? throw new ObjectDisposedException(nameof(FontBuffer))).AsSpan(0, Length);

        public override void Dispose()
        {
            var rented = Interlocked.Exchange(ref _array, value: null);
            if (rented is null)
                return;

            ArrayPool<byte>.Shared.Return(rented);
            Interlocked.Add(ref allocator._liveBytes, -Length);
        }
    }
}
