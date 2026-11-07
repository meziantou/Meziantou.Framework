namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The bytes of the codestream that were received and not parsed yet, in a buffer rented from the operation scope
/// (<see cref="AllocationKind.DecoderState"/>). The buffer grows with the bytes actually received, never with a size the
/// codestream announces.
/// </summary>
internal sealed class JpegXlCodestreamBuffer : IDisposable
{
    private const int InitialCapacity = 4096;

    private readonly AllocationScope _scope;
    private PooledBuffer? _buffer;

    public JpegXlCodestreamBuffer(AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        _scope = scope;
    }

    /// <summary>Gets the number of bytes held.</summary>
    public int Length { get; private set; }

    /// <summary>Gets the underlying array: the bytes are its first <see cref="Length"/> bytes.</summary>
    public byte[] Data => _buffer?.RawBuffer ?? [];

    public ReadOnlySpan<byte> Span => Data.AsSpan(0, Length);

    /// <summary>Appends received bytes.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        var required = (long)Length + data.Length;
        if (required > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(_scope.Limits);

        if (_buffer is null || _buffer.Capacity < required)
        {
            var capacity = (int)Math.Min(CheckedSizes.MaxBufferLength, Math.Max(required, Math.Max(InitialCapacity, (long)(_buffer?.Capacity ?? 0) * 2)));
            var larger = _scope.Rent(capacity, AllocationKind.DecoderState, clear: false);
            if (_buffer is not null)
            {
                _buffer.RawBuffer.AsSpan(0, Length).CopyTo(larger.RawBuffer);
                _buffer.Dispose();
            }

            _buffer = larger;
        }

        data.CopyTo(_buffer.RawBuffer.AsSpan(Length));
        Length = (int)required;
    }

    /// <summary>Removes the first <paramref name="count"/> bytes.</summary>
    public void Discard(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, Length);
        if (count == 0)
            return;

        var data = Data;
        data.AsSpan(count, Length - count).CopyTo(data);
        Length -= count;
        if (Length == 0)
        {
            // Nothing is kept between two frames: give the memory back
            _buffer?.Dispose();
            _buffer = null;
        }
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
        Length = 0;
    }
}
