namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A growable byte buffer rented from an allocation scope (<see cref="AllocationKind.Temporary"/>): the output of the VP8 encoder.</summary>
internal sealed class WebPPayloadWriter : IDisposable
{
    private const int InitialCapacity = 4096;

    private readonly AllocationScope _scope;
    private PooledBuffer? _buffer;

    public WebPPayloadWriter(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    /// <summary>Gets the number of bytes written.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the written bytes; valid until the writer is disposed or written to.</summary>
    public ReadOnlyMemory<byte> WrittenMemory => _buffer is null ? default : _buffer.RawBuffer.AsMemory(0, Length);

    public Span<byte> WrittenSpan => _buffer is null ? default : _buffer.RawBuffer.AsSpan(0, Length);

    public void Write(byte value)
    {
        Ensure(1);
        _buffer!.RawBuffer[Length++] = value;
    }

    public void Write(ReadOnlySpan<byte> data)
    {
        Ensure(data.Length);
        data.CopyTo(_buffer!.RawBuffer.AsSpan(Length));
        Length += data.Length;
    }

    /// <summary>Increments the byte at <paramref name="index"/> with carry propagation toward the start (boolean encoder carries).</summary>
    public void PropagateCarry(int index)
    {
        var data = _buffer!.RawBuffer;
        while (index >= 0 && data[index] == byte.MaxValue)
        {
            data[index] = 0;
            index--;
        }

        if (index >= 0)
        {
            data[index]++;
        }
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }

    private void Ensure(int count)
    {
        if (_buffer is not null && _buffer.Capacity - Length >= count)
            return;

        var required = (long)Length + count;
        if (required > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        var capacity = Math.Min(CheckedSizes.MaxBufferLength, Math.Max(required, Math.Max(InitialCapacity, (long)(_buffer?.Capacity ?? 0) * 2)));
        var larger = _scope.Rent((int)capacity, AllocationKind.Temporary, clear: false);
        if (_buffer is not null)
        {
            _buffer.RawBuffer.AsSpan(0, Length).CopyTo(larger.RawBuffer);
            _buffer.Dispose();
        }

        _buffer = larger;
    }
}
