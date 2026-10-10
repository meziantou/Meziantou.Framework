using System.Buffers;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A buffer rented from a <see cref="SlabPool"/> and charged, at its full capacity, to an <see cref="AllocationScope"/>.
/// Disposal returns the buffer to the pool and releases the charge exactly once (idempotent and thread-safe).
/// </summary>
internal sealed class PooledBuffer : IMemoryOwner<byte>
{
    private byte[]? _buffer;

    internal PooledBuffer(AllocationScope scope, AllocationKind kind, byte[] buffer, int length)
    {
        Scope = scope;
        Kind = kind;
        _buffer = buffer;
        Length = length;
        Capacity = buffer.Length;
    }

    public AllocationScope Scope { get; }

    public AllocationKind Kind { get; }

    /// <summary>Gets the requested length (the length of <see cref="Memory"/>).</summary>
    public int Length { get; }

    /// <summary>Gets the actual capacity of the rented buffer, which is the charged size.</summary>
    public int Capacity { get; }

    public bool IsDisposed => Volatile.Read(ref _buffer) is null;

    /// <summary>Gets the first <see cref="Length"/> bytes of the buffer.</summary>
    /// <exception cref="ObjectDisposedException">The buffer is disposed.</exception>
    public Memory<byte> Memory => RawBuffer.AsMemory(0, Length);

    /// <summary>Gets the first <see cref="Length"/> bytes of the buffer.</summary>
    /// <exception cref="ObjectDisposedException">The buffer is disposed.</exception>
    public Span<byte> Span => RawBuffer.AsSpan(0, Length);

    /// <summary>Gets the underlying array (its length is <see cref="Capacity"/>). Only the first <see cref="Length"/> bytes are meaningful.</summary>
    /// <exception cref="ObjectDisposedException">The buffer is disposed.</exception>
    internal byte[] RawBuffer => Volatile.Read(ref _buffer) ?? throw new ObjectDisposedException(nameof(PooledBuffer));

    public void Dispose()
    {
        var buffer = Interlocked.Exchange(ref _buffer, value: null);
        if (buffer is null)
            return;

        Scope.Pool.Return(buffer);
        Scope.ReleaseAllocation(Capacity, Kind);
    }
}
