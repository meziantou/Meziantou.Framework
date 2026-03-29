using Meziantou.Framework.DnsClient;

namespace Meziantou.DnsProxy.Forwarding;

internal sealed class UpstreamDnsClientInfo : IDisposable
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

    public void Dispose()
    {
        Client.Dispose();
        HttpHandler?.Dispose();
    }
}
