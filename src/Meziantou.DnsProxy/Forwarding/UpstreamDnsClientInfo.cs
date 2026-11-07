using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;

namespace Meziantou.DnsProxy.Forwarding;

internal sealed class UpstreamDnsClientInfo : IUpstreamDnsClient, IDisposable
{
    public UpstreamDnsClientInfo(string displayName, string endpoint, DnsClient client, SocketsHttpHandler? httpHandler)
    {
        DisplayName = displayName;
        Endpoint = endpoint;
        Client = client;
        HttpHandler = httpHandler;
    }

    public string DisplayName { get; }

    public string Endpoint { get; }

    public DnsClient Client { get; }

    private SocketsHttpHandler? HttpHandler { get; }

    public Task<DnsResponseMessage> SendAsync(DnsQueryMessage query, CancellationToken cancellationToken)
    {
        return Client.SendAsync(query, cancellationToken);
    }

    public void Dispose()
    {
        Client.Dispose();
        HttpHandler?.Dispose();
    }
}
