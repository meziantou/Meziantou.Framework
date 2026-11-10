using System.Buffers.Binary;

namespace Meziantou.Framework.DnsClient.Transport;

/// <summary>A connection that carries DNS messages, each prefixed with its 2-byte length (RFC 1035 section 4.2.2, RFC 7766, RFC 7858).</summary>
internal sealed class DnsStreamConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly IDisposable? _owner;

    public DnsStreamConnection(Stream stream, IDisposable? owner)
    {
        _stream = stream;
        _owner = owner;
    }

    public async Task WriteMessageAsync(byte[] message, CancellationToken cancellationToken)
    {
        if (message.Length > ushort.MaxValue)
            throw new ArgumentException($"A DNS message cannot exceed {ushort.MaxValue} bytes.", nameof(message));

        // One write, so the length and the message are not sent as two segments
        var buffer = new byte[message.Length + 2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)message.Length);
        message.CopyTo(buffer, 2);
        await _stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the next message, or returns <see langword="null"/> when the server closed the connection between two messages.</summary>
    /// <exception cref="EndOfStreamException">The connection was closed in the middle of a message.</exception>
    public async Task<byte[]?> ReadMessageAsync(CancellationToken cancellationToken)
    {
        var lengthPrefix = new byte[2];
        var read = await _stream.ReadAtLeastAsync(lengthPrefix, lengthPrefix.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        if (read is 0)
            return null;

        if (read < lengthPrefix.Length)
            throw new EndOfStreamException("The connection was closed in the middle of the length of a DNS message.");

        var message = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];
        await _stream.ReadExactlyAsync(message, cancellationToken).ConfigureAwait(false);
        return message;
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _owner?.Dispose();
    }
}
