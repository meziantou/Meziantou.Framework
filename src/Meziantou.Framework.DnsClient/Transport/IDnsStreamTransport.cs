namespace Meziantou.Framework.DnsClient.Transport;

/// <summary>A transport over a stream (TCP, DNS over TLS), which can carry several responses, as a zone transfer does.</summary>
internal interface IDnsStreamTransport
{
    Task<DnsStreamConnection> ConnectAsync(CancellationToken cancellationToken);
}
