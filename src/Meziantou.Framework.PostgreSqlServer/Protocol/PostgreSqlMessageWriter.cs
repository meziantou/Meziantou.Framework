using System.Buffers.Binary;

namespace Meziantou.Framework.PostgreSql.Protocol;

internal sealed class PostgreSqlMessageWriter
{
    private readonly Stream _stream;

    public PostgreSqlMessageWriter(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    public ValueTask WriteSslResponseAsync(bool acceptTls, CancellationToken cancellationToken)
    {
        var buffer = new[] { acceptTls ? (byte)'S' : (byte)'N' };
        return _stream.WriteAsync(buffer, cancellationToken);
    }

    public async ValueTask WriteMessageAsync(byte messageType, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        // Header and payload go out in a single write: two writes on a NetworkStream interact badly with Nagle,
        // holding the payload until the peer's delayed ACK arrives.
        var buffer = new byte[5 + payload.Length];
        buffer[0] = messageType;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(1, 4), payload.Length + 4);
        payload.Span.CopyTo(buffer.AsSpan(5));

        await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        return new ValueTask(_stream.FlushAsync(cancellationToken));
    }
}
