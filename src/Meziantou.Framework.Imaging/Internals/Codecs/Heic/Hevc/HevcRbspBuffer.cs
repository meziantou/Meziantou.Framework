namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The raw byte sequence payload of a NAL unit: the bytes that follow the NAL unit header, without the emulation prevention
/// bytes (Rec. ITU-T H.265, clause 7.3.1.1 "General NAL unit syntax"). A payload that has no emulation prevention byte is
/// read where it is; otherwise it is copied once, without these bytes, into a buffer rented from the allocation scope. The
/// data of the caller are never modified.
/// </summary>
internal sealed class HevcRbspBuffer : IDisposable
{
    private readonly AllocationScope _scope;
    private PooledBuffer? _bytes;
    private Av1PooledArray<int>? _removed;
    private int _removedCount;
    private byte[] _array = [];
    private int _offset;
    private int _length;

    public HevcRbspBuffer(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    private static ReadOnlySpan<byte> EmulationPrevention => [0, 0, 3];

    /// <summary>Gets the array that holds the payload loaded by <see cref="Load(byte[], int, int)"/>: the array of the caller, or the rented buffer.</summary>
    public byte[] Array => _array;

    /// <summary>Gets the position of the payload in <see cref="Array"/>.</summary>
    public int Offset => _offset;

    /// <summary>Gets the number of bytes of the payload.</summary>
    public int Length => _length;

    /// <summary>Gets the number of emulation prevention bytes that the last payload had.</summary>
    public int EmulationPreventionByteCount => _removedCount;

    /// <summary>Gets the payload loaded by <see cref="Load(byte[], int, int)"/>.</summary>
    public ReadOnlySpan<byte> Span => _array.AsSpan(_offset, _length);

    /// <summary>
    /// Loads the payload of a NAL unit: <paramref name="length"/> bytes of <paramref name="data"/> from
    /// <paramref name="offset"/>, the bytes that follow the NAL unit header.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The copy of the payload exceeds the live-allocation limit.</exception>
    public void Load(byte[] data, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(data);
        var payload = data.AsSpan(offset, length);
        _removedCount = CountEmulationPreventionBytes(payload);
        if (_removedCount is 0)
        {
            _array = data;
            _offset = offset;
            _length = length;
            return;
        }

        var buffer = Unescape(payload);
        _array = buffer.RawBuffer;
        _offset = 0;
        _length = length - _removedCount;
    }

    /// <summary>
    /// Returns the payload of a NAL unit without its emulation prevention bytes: <paramref name="payload"/> itself when it
    /// has none, otherwise a copy that is valid until the next load.
    /// </summary>
    /// <exception cref="ImageResourceLimitException">The copy of the payload exceeds the live-allocation limit.</exception>
    public ReadOnlySpan<byte> Load(ReadOnlySpan<byte> payload)
    {
        _array = [];
        _offset = 0;
        _length = 0;
        _removedCount = CountEmulationPreventionBytes(payload);
        if (_removedCount is 0)
            return payload;

        var buffer = Unescape(payload);
        return buffer.RawBuffer.AsSpan(0, payload.Length - _removedCount);
    }

    /// <summary>
    /// Converts a position in the bytes of the NAL unit that follow its header, emulation prevention bytes included, to a
    /// position in the payload: the position of the first byte of the payload that is not before it. Entry points are
    /// expressed this way (clause 7.4.7.1, <c>entry_point_offset_minus1</c>).
    /// </summary>
    public int FromSourceOffset(int sourceOffset)
    {
        if (_removedCount is 0)
            return sourceOffset;

        // removed[i] is the number of payload bytes before the emulation prevention byte i, which is the byte
        // removed[i] + i of the source: the positions are increasing
        var removed = _removed!.Span[.._removedCount];
        var low = 0;
        var high = removed.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (removed[middle] + middle < sourceOffset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return sourceOffset - low;
    }

    /// <summary>
    /// Converts a position in the payload to a position in the bytes of the NAL unit that follow its header, emulation
    /// prevention bytes included: the inverse of <see cref="FromSourceOffset"/>.
    /// </summary>
    public int ToSourceOffset(int offset)
    {
        if (_removedCount is 0)
            return offset;

        // The emulation prevention bytes that precede the byte of the payload: those with removed[i] <= offset
        var removed = _removed!.Span[.._removedCount];
        var low = 0;
        var high = removed.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (removed[middle] <= offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return offset + low;
    }

    private static int CountEmulationPreventionBytes(ReadOnlySpan<byte> payload)
    {
        var count = 0;
        while (true)
        {
            var index = payload.IndexOf(EmulationPrevention);
            if (index < 0)
                return count;

            count++;
            payload = payload[(index + 3)..];
        }
    }

    private PooledBuffer Unescape(ReadOnlySpan<byte> payload)
    {
        var length = payload.Length - _removedCount;
        if (_bytes is null || _bytes.Length < length)
        {
            _bytes?.Dispose();
            _bytes = null;
            _bytes = _scope.Rent(length, AllocationKind.DecoderState, clear: false);
        }

        if (_removed is null || _removed.Length < _removedCount)
        {
            _removed?.Dispose();
            _removed = null;
            _removed = Av1PooledArray<int>.Rent(_scope, _removedCount, clear: false);
        }

        var destination = _bytes.RawBuffer.AsSpan(0, length);
        var removed = _removed.Span;
        var written = 0;
        for (var i = 0; i < _removedCount; i++)
        {
            // The two zero bytes are part of the payload; the byte equal to 3 that follows them is not
            var index = payload.IndexOf(EmulationPrevention);
            payload[..(index + 2)].CopyTo(destination[written..]);
            written += index + 2;
            removed[i] = written;
            payload = payload[(index + 3)..];
        }

        payload.CopyTo(destination[written..]);
        return _bytes;
    }

    public void Dispose()
    {
        _bytes?.Dispose();
        _bytes = null;
        _removed?.Dispose();
        _removed = null;
        _array = [];
        _offset = 0;
        _length = 0;
        _removedCount = 0;
    }
}
