namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The connection of a <see cref="WebSocket"/>, which observes the headers of the frames it reads
/// (https://www.rfc-editor.org/rfc/rfc6455#section-5.2), and writes the Close frames without status code:
/// <see cref="System.Net.WebSockets.WebSocket"/> reports a Close frame without status code as a normal closure (<c>1000</c>),
/// while its close code is <c>1005</c> (https://www.rfc-editor.org/rfc/rfc6455#section-7.1.5), and it sends the <c>1005</c>
/// status code for <see cref="System.Net.WebSockets.WebSocketCloseStatus.Empty"/>, which must not be sent
/// (https://www.rfc-editor.org/rfc/rfc6455#section-7.4.1).
/// </summary>
internal sealed class WebSocketFrameObserver(Stream stream) : Stream
{
    private readonly byte[] _header = new byte[14];
    private int _headerLength;
    private long _remainingPayload;

    /// <summary>Gets whether the last Close frame that was read has a status code (a payload of at least 2 bytes).</summary>
    public bool LastCloseFrameHasStatusCode { get; private set; }

    /// <summary>
    /// Gets or sets whether the next Close frame written with the <c>1005</c> status code (<see cref="System.Net.WebSockets.WebSocketCloseStatus.Empty"/>)
    /// is written without status code.
    /// </summary>
    public bool WriteNextCloseFrameWithoutStatusCode { get; set; }

    public override bool CanRead => stream.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => stream.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Observe(buffer.AsSpan(offset, stream.Read(buffer, offset, count)));

    public override int Read(Span<byte> buffer) => Observe(buffer[..stream.Read(buffer)]);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var count = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return Observe(buffer.Span[..count]);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer) => stream.Write(RewriteCloseFrame(buffer.ToArray()));

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (WriteNextCloseFrameWithoutStatusCode)
            return stream.WriteAsync(RewriteCloseFrame(buffer.ToArray()), cancellationToken);

        return stream.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush() => stream.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            stream.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Removes the status code of a masked Close frame whose status code is <c>1005</c>: the frames are written by a single
    /// write (the header, the masking key and the payload).
    /// </summary>
    private byte[] RewriteCloseFrame(byte[] frame)
    {
        if (WriteNextCloseFrameWithoutStatusCode && frame is [0x88, 0x82, _, _, _, _, _, _] && (frame[6] ^ frame[2]) == 0x03 && (frame[7] ^ frame[3]) == 0xED)
        {
            WriteNextCloseFrameWithoutStatusCode = false;
            return [0x88, 0x80, frame[2], frame[3], frame[4], frame[5]];
        }

        return frame;
    }

    /// <summary>Follows the frames in the bytes that were read: the headers are parsed, the payloads are skipped.</summary>
    private int Observe(ReadOnlySpan<byte> bytes)
    {
        var count = bytes.Length;
        while (bytes.Length > 0)
        {
            if (_remainingPayload > 0)
            {
                var skipped = (int)Math.Min(_remainingPayload, bytes.Length);
                _remainingPayload -= skipped;
                bytes = bytes[skipped..];
                continue;
            }

            _header[_headerLength++] = bytes[0];
            bytes = bytes[1..];
            if (_headerLength < 2)
                continue;

            var length7 = _header[1] & 0x7F;
            var headerLength = 2 + (length7 == 126 ? 2 : length7 == 127 ? 8 : 0) + ((_header[1] & 0x80) != 0 ? 4 : 0);
            if (_headerLength < headerLength)
                continue;

            long payloadLength = length7 switch
            {
                126 => (_header[2] << 8) | _header[3],
                127 => (long)(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(_header.AsSpan(2, 8)) & long.MaxValue),
                _ => length7,
            };

            // A Close frame (https://www.rfc-editor.org/rfc/rfc6455#section-5.5.1)
            if ((_header[0] & 0x0F) == 0x8)
            {
                LastCloseFrameHasStatusCode = payloadLength >= 2;
            }

            _headerLength = 0;
            _remainingPayload = payloadLength;
        }

        return count;
    }
}
