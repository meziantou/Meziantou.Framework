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
        var header = new byte[5];
        header[0] = messageType;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), payload.Length + 4);

        await _stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
    }
}
