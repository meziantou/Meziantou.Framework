using System.Net;
using System.Net.Security;
using System.Net.Sockets;

namespace Meziantou.Framework.DnsClient.Transport;

internal sealed class DnsTlsTransport : IDnsTransport, IDnsStreamTransport
{
    private readonly string _host;
    private readonly IPEndPoint _endpoint;

    public DnsTlsTransport(string host, IPEndPoint endpoint)
    {
        _host = host;
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

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The connection owns the stream and the client")]
    public async Task<DnsStreamConnection> ConnectAsync(CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(_endpoint.Address, _endpoint.Port, cancellationToken).ConfigureAwait(false);

            // The same 2-byte length framing as TCP (RFC 7858)
            var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
            try
            {
                var sslOptions = new SslClientAuthenticationOptions
                {
                    TargetHost = _host,
                };
                await sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken).ConfigureAwait(false);
                return new DnsStreamConnection(sslStream, client);
            }
            catch
            {
                await sslStream.DisposeAsync().ConfigureAwait(false);
                throw;
            }
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
