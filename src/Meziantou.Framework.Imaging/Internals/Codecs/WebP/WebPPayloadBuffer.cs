namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Accumulates one chunk payload (a VP8/VP8L bitstream or an ALPH chunk) streamed by <see cref="WebPStructureParser"/>, in
/// buffers rented from the operation scope (<see cref="AllocationKind.DecoderState"/>). The buffer grows with the bytes
/// actually received (doubling, capped by the declared size), so a declared chunk size never causes an allocation larger
/// than twice the data that arrived.
/// </summary>
internal sealed class WebPPayloadBuffer : IDisposable
{
    private const int InitialCapacity = 64 * 1024;

    private readonly AllocationScope _scope;
    private readonly long _declaredLength;
    private PooledBuffer? _buffer;

    public WebPPayloadBuffer(AllocationScope scope, long declaredLength)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (declaredLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(scope.Limits);

        _scope = scope;
        _declaredLength = declaredLength;
    }

    /// <summary>Gets the number of bytes received.</summary>
    public int Length { get; private set; }

    /// <summary>Gets a value indicating whether every declared byte was received.</summary>
    public bool IsComplete => Length == _declaredLength;

    /// <summary>Gets the underlying array (the payload is its first <see cref="Length"/> bytes).</summary>
    public byte[] Data => _buffer?.RawBuffer ?? [];

    public ReadOnlySpan<byte> Span => Data.AsSpan(0, Length);

    /// <summary>Appends received bytes.</summary>
    public void Append(ReadOnlySpan<byte> data)
    {
        if (data.Length > _declaredLength - Length)
            throw new InvalidOperationException("The payload exceeds its declared length.");

        var required = Length + data.Length;
        if (_buffer is null || _buffer.Capacity < required)
        {
            var capacity = Math.Min(_declaredLength, Math.Max(required, Math.Max(InitialCapacity, (long)(_buffer?.Capacity ?? 0) * 2)));
            var larger = _scope.Rent((int)capacity, AllocationKind.DecoderState, clear: false);
            if (_buffer is not null)
            {
                _buffer.RawBuffer.AsSpan(0, Length).CopyTo(larger.RawBuffer);
                _buffer.Dispose();
            }

            _buffer = larger;
        }

        data.CopyTo(_buffer.RawBuffer.AsSpan(Length));
        Length = required;
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
