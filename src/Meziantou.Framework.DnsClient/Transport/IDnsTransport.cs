namespace Meziantou.Framework.DnsClient.Transport;

internal interface IDnsTransport : IDisposable
{
    Task<DnsTransportResponse> SendAsync(byte[] query, CancellationToken cancellationToken);
}
