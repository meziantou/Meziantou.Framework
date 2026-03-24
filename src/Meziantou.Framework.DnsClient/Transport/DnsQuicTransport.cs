#if NET9_0_OR_GREATER
using System.Buffers.Binary;
using System.Net;
using System.Net.Quic;
using System.Net.Security;

namespace Meziantou.Framework.DnsClient.Transport;

internal sealed class DnsQuicTransport : IDnsTransport
{
    private readonly string _host;
    private readonly IPEndPoint _endpoint;

    public DnsQuicTransport(string host, IPEndPoint endpoint)
    {
        _host = host;
        _endpoint = endpoint;
    }

    public async Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
    {
        if (!QuicConnection.IsSupported)
            throw new PlatformNotSupportedException("QUIC is not supported on this platform.");

        var connectionOptions = new QuicClientConnectionOptions
        {
            RemoteEndPoint = _endpoint,
            DefaultStreamErrorCode = 0,
            DefaultCloseErrorCode = 0,
            ClientAuthenticationOptions = new SslClientAuthenticationOptions
            {
                TargetHost = _host,
                ApplicationProtocols = [new SslApplicationProtocol("doq")],
            },
        };

        await using var connection = await QuicConnection.ConnectAsync(connectionOptions, cancellationToken).ConfigureAwait(false);
        await using var stream = await connection.OpenOutboundStreamAsync(QuicStreamType.Bidirectional, cancellationToken).ConfigureAwait(false);

        // RFC 9250: 2-byte length prefix
        var lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)query.Length);
        await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(query, cancellationToken).ConfigureAwait(false);
        stream.CompleteWrites();

        // Read response length prefix
        await stream.ReadExactlyAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        var responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);

        // Read response data
        var response = new byte[responseLength];
        await stream.ReadExactlyAsync(response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    public void Dispose()
    {
    }
}
#endif
