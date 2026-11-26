namespace Meziantou.Framework.WebTransport.Http3;

// Reads the frames of a control, request or CONNECT stream (RFC 9114, section 7.1): a type, a length and a payload.
internal sealed class Http3FrameReader
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;

    public Http3FrameReader(Stream stream) => _stream = stream;

    public int Buffered => _end - _start;

    // Returns null at the end of the stream, when it ends between two frames
    public async ValueTask<(long Type, long Length)?> ReadFrameHeaderAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureAsync(1, cancellationToken).ConfigureAwait(false))
        {
            if (Buffered == 0)
                return null;

            throw new Http3ProtocolException(Http3ErrorCode.FrameError, "The stream ends inside a frame.");
        }

        var type = await ReadVariableLengthIntegerAsync(cancellationToken).ConfigureAwait(false);
        var length = await ReadVariableLengthIntegerAsync(cancellationToken).ConfigureAwait(false);
        return (type, length);
    }

    public async ValueTask<long> ReadVariableLengthIntegerAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureAsync(1, cancellationToken).ConfigureAwait(false))
            throw new Http3ProtocolException(Http3ErrorCode.FrameError, "The stream ends inside a frame.");

        var length = VariableLengthInteger.GetLength(_buffer[_start]);
        if (!await EnsureAsync(length, cancellationToken).ConfigureAwait(false))
            throw new Http3ProtocolException(Http3ErrorCode.FrameError, "The stream ends inside a frame.");

        VariableLengthInteger.TryRead(_buffer.AsSpan(_start, length), out var value, out _);
        _start += length;
        return value;
    }

    public async ValueTask<byte[]> ReadPayloadAsync(long length, int maxLength, CancellationToken cancellationToken)
    {
        if (length > maxLength)
            throw new Http3ProtocolException(Http3ErrorCode.ExcessiveLoad, "The frame is too large.");

        var payload = new byte[length];
        var written = 0;
        while (written < payload.Length)
        {
            written += await ReadPayloadChunkAsync(payload.AsMemory(written), cancellationToken).ConfigureAwait(false);
        }

        return payload;
    }

    // Reads at most destination.Length bytes of the current frame
    public async ValueTask<int> ReadPayloadChunkAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        if (Buffered == 0 && !await EnsureAsync(1, cancellationToken).ConfigureAwait(false))
            throw new Http3ProtocolException(Http3ErrorCode.FrameError, "The stream ends inside a frame.");

        var count = Math.Min(Buffered, destination.Length);
        _buffer.AsSpan(_start, count).CopyTo(destination.Span);
        _start += count;
        return count;
    }

    public async ValueTask SkipAsync(long length, CancellationToken cancellationToken)
    {
        while (length > 0)
        {
            if (Buffered == 0 && !await EnsureAsync(1, cancellationToken).ConfigureAwait(false))
                throw new Http3ProtocolException(Http3ErrorCode.FrameError, "The stream ends inside a frame.");

            var count = (int)Math.Min(Buffered, length);
            _start += count;
            length -= count;
        }
    }

    private async ValueTask<bool> EnsureAsync(int count, CancellationToken cancellationToken)
    {
        while (Buffered < count)
        {
            if (_start > 0)
            {
                _buffer.AsSpan(_start, Buffered).CopyTo(_buffer);
                _end -= _start;
                _start = 0;
            }

            var read = await _stream.ReadAsync(_buffer.AsMemory(_end), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return false;

            _end += read;
        }

        return true;
    }

    public static byte[] EncodeFrame(long type, ReadOnlySpan<byte> payload)
    {
        var result = new byte[VariableLengthInteger.GetEncodedLength(type) + VariableLengthInteger.GetEncodedLength(payload.Length) + payload.Length];
        var position = VariableLengthInteger.Write(result, type);
        position += VariableLengthInteger.Write(result.AsSpan(position), payload.Length);
        payload.CopyTo(result.AsSpan(position));
        return result;
    }
}
