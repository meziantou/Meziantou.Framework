using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace Meziantou.Framework.DnsClient.Transport;

internal sealed class DnsTlsTransport : IDnsTransport
{
    private readonly string _host;
    private readonly IPEndPoint _endpoint;

    public DnsTlsTransport(string host, IPEndPoint endpoint)
    {
        _host = host;
        _endpoint = endpoint;
    }

    public async Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_endpoint.Address, _endpoint.Port, cancellationToken).ConfigureAwait(false);

        using var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        var sslOptions = new SslClientAuthenticationOptions
        {
            TargetHost = _host,
        };
        await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);

        // TCP-like 2-byte length prefix (RFC 7858)
        var lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)query.Length);
        await sslStream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await sslStream.WriteAsync(query, cancellationToken).ConfigureAwait(false);
        await sslStream.FlushAsync(cancellationToken).ConfigureAwait(false);

        // Read response length prefix
        await sslStream.ReadExactlyAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        var responseLength = BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix);

        // Read response data
        var response = new byte[responseLength];
        await sslStream.ReadExactlyAsync(response, cancellationToken).ConfigureAwait(false);

        return response;
    }

    public void Dispose()
    {
    }
}
