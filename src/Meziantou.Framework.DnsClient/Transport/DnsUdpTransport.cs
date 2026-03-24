using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.DnsClient.Transport;

internal sealed class DnsUdpTransport : IDnsTransport
{
    private readonly IPEndPoint _endpoint;

    public DnsUdpTransport(IPEndPoint endpoint)
    {
        _endpoint = endpoint;
    }

    public async Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken)
    {
        using var client = new UdpClient();
        await client.SendAsync(query, query.Length, _endpoint).ConfigureAwait(false);

        // Standard DNS over UDP has a 512-byte limit, but EDNS can extend this
        var receiveTask = client.ReceiveAsync(cancellationToken);
        var result = await receiveTask.ConfigureAwait(false);

        return result.Buffer;
    }

    public void Dispose()
    {
    }
}
