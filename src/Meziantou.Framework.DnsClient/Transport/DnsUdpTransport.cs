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
        using var client = new UdpClient(_endpoint.AddressFamily);

        // Connecting makes the kernel drop datagrams from any source other than the server we queried, which is the
        // first line of defence against off-path answer injection. The identifier and question are checked by the
        // caller as well, because an on-path attacker can still spoof the source address.
        client.Connect(_endpoint);

        await client.Client.SendAsync(query, SocketFlags.None, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            var result = await client.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            if (result.RemoteEndPoint.Equals(_endpoint))
                return result.Buffer;

            // A datagram from an unexpected source: ignore it and keep waiting rather than failing the query, so a
            // single stray packet cannot deny service. The caller's timeout bounds this loop.
        }
    }

    public void Dispose()
    {
    }
}
