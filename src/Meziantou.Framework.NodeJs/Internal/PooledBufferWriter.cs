using System.Buffers;

namespace Meziantou.Framework.NodeJs.Internal;

/// <summary>A buffer writer backed by <see cref="ArrayPool{T}.Shared"/>, so serializing a large message does not allocate arrays that become garbage.</summary>
internal sealed class PooledBufferWriter : IBufferWriter<byte>, IDisposable
{
    private const int MinimumBufferSize = 256;

    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(MinimumBufferSize);
    private int _written;

    /// <summary>Gets the written data. It is no longer valid once the writer is disposed.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _written);

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count > _buffer.Length - _written)
            throw new InvalidOperationException("Cannot advance past the end of the buffer.");

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        EnsureCapacity(sizeHint);
        return _buffer.AsSpan(_written);
    }

    public void Dispose()
    {
        var buffer = _buffer;
        _buffer = [];
        _written = 0;
        if (buffer.Length > 0)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void EnsureCapacity(int sizeHint)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        sizeHint = Math.Max(sizeHint, 1);
        if (sizeHint <= _buffer.Length - _written)
            return;

        var requiredSize = (long)_written + sizeHint;
        if (requiredSize > Array.MaxLength)
            throw new InvalidOperationException("The message exceeds the maximum size of an array.");

        var newSize = (int)Math.Clamp(Math.Max((long)_buffer.Length * 2, requiredSize), MinimumBufferSize, Array.MaxLength);
        var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
        _buffer.AsSpan(0, _written).CopyTo(newBuffer);
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = newBuffer;
    }
}
