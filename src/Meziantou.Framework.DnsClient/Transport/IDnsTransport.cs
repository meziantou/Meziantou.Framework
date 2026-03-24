namespace Meziantou.Framework.DnsClient.Transport;

internal interface IDnsTransport : IDisposable
{
    Task<byte[]> SendAsync(byte[] query, CancellationToken cancellationToken);
}
