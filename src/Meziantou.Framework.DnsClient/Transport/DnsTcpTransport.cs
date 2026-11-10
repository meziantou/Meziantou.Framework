using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.DnsClient.Transport;

internal sealed class DnsTcpTransport : IDnsTransport, IDnsStreamTransport
{
    private readonly IPEndPoint _endpoint;

    public DnsTcpTransport(IPEndPoint endpoint)
    {
        _endpoint = endpoint;
    }

    public async Task<DnsTransportResponse> SendAsync(byte[] query, CancellationToken cancellationToken)
    {
        var connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await connection.WriteMessageAsync(query, cancellationToken).ConfigureAwait(false);
            var response = await connection.ReadMessageAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new EndOfStreamException("The server closed the connection without answering.");

            return new DnsTransportResponse(response);
        }
    }

    public async Task<DnsStreamConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_endpoint.Address, _endpoint.Port, cancellationToken).ConfigureAwait(false);
            return new DnsStreamConnection(client.GetStream(), client);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
    }
}
