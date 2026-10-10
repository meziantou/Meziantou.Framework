using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Writes the least-significant-bit-first bit stream of the WebP lossless format into a growable buffer rented from an
/// allocation scope (<see cref="AllocationKind.Temporary"/>): the inverse of <see cref="Vp8LBitReader"/>.
/// </summary>
internal sealed class Vp8LBitWriter : IDisposable
{
    private const int InitialCapacity = 4096;

    private readonly AllocationScope _scope;
    private PooledBuffer? _buffer;
    private int _length;
    private ulong _bits;
    private int _bitCount;

    public Vp8LBitWriter(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    /// <summary>Gets the number of complete bytes written so far (pending bits excluded).</summary>
    public int Length => _length;

    /// <summary>Gets the number of bits written so far.</summary>
    public long BitCount => ((long)_length * 8) + _bitCount;

    /// <summary>Writes the low <paramref name="count"/> bits of <paramref name="value"/> (0 to 32 bits).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteBits(uint value, int count)
    {
        _bits |= (ulong)(value & (uint)((1UL << count) - 1)) << _bitCount;
        _bitCount += count;
        if (_bitCount >= 32)
        {
            Flush32();
        }
    }

    /// <summary>Pads the last byte with zero bits.</summary>
    public void Finish()
    {
        while (_bitCount > 0)
        {
            Ensure(1);
            _buffer!.RawBuffer[_length++] = (byte)_bits;
            _bits >>= 8;
            _bitCount = Math.Max(0, _bitCount - 8);
        }
    }

    /// <summary>Gets the written bytes (call <see cref="Finish"/> first).</summary>
    public ReadOnlySpan<byte> WrittenSpan => _buffer is null ? default : _buffer.RawBuffer.AsSpan(0, _length);

    /// <summary>Gets the written bytes (call <see cref="Finish"/> first); valid until the writer is disposed.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer is null ? default : _buffer.RawBuffer.AsMemory(0, _length);

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }

    private void Flush32()
    {
        Ensure(4);
        var data = _buffer!.RawBuffer;
        data[_length] = (byte)_bits;
        data[_length + 1] = (byte)(_bits >> 8);
        data[_length + 2] = (byte)(_bits >> 16);
        data[_length + 3] = (byte)(_bits >> 24);
        _length += 4;
        _bits >>= 32;
        _bitCount -= 32;
    }

    private void Ensure(int count)
    {
        if (_buffer is not null && _buffer.Capacity - _length >= count)
            return;

        var required = (long)_length + count;
        var capacity = Math.Max(required, Math.Max(InitialCapacity, (long)(_buffer?.Capacity ?? 0) * 2));
        if (required > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        capacity = Math.Min(capacity, CheckedSizes.MaxBufferLength);
        var larger = _scope.Rent((int)capacity, AllocationKind.Temporary, clear: false);
        if (_buffer is not null)
        {
            _buffer.RawBuffer.AsSpan(0, _length).CopyTo(larger.RawBuffer);
            _buffer.Dispose();
        }

        _buffer = larger;
    }
}
