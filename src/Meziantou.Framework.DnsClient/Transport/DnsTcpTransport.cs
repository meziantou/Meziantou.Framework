using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.DnsClient.Transport;

internal sealed class DnsTcpTransport : IDnsTransport
{
    private readonly IPEndPoint _endpoint;

    public DnsTcpTransport(IPEndPoint endpoint)
    {
        _endpoint = endpoint;
    }

    public async Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_endpoint.Address, _endpoint.Port, cancellationToken).ConfigureAwait(false);

        var stream = client.GetStream();

        // TCP DNS uses a 2-byte length prefix (RFC 7766)
        var lengthPrefix = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(lengthPrefix, (ushort)query.Length);
        await stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(query, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

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
