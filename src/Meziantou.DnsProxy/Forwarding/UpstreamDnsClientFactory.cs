using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.DnsProxy.Forwarding;

internal sealed class UpstreamDnsClientFactory : IDisposable
{
    private readonly IReadOnlyList<UpstreamDnsClientInfo> _upstreams;

    public UpstreamDnsClientFactory(IOptions<DnsProxyOptions> options, ILogger<UpstreamDnsClientFactory> logger)
    {
        var upstreams = new List<UpstreamDnsClientInfo>();
        var dnsProxyOptions = options.Value;
        var serverAddressResolver = CreateBootstrapResolver(dnsProxyOptions);
        foreach (var upstream in dnsProxyOptions.Upstreams.OrderBy(upstream => upstream.Priority))
        {
            if (upstream.Url is null)
            {
                continue;
            }

            var protocol = GetProtocol(upstream.Url);
            var endpoint = GetEndpoint(upstream.Url, protocol);
            var displayName = string.IsNullOrWhiteSpace(upstream.Name) ? upstream.Url.OriginalString : $"{upstream.Name} ({upstream.Url.OriginalString})";
            SocketsHttpHandler? httpHandler = protocol == DnsClientProtocol.Https ? CreateHttpHandler(upstream.Url.Scheme.Equals("h3", StringComparison.OrdinalIgnoreCase), serverAddressResolver) : null;
            DnsClient dnsClient;
            DnsClientProtocol effectiveProtocol = protocol;
            var clientOptions = CreateDnsClientOptions(dnsProxyOptions, httpHandler, serverAddressResolver);
            try
            {
                dnsClient = new DnsClient(endpoint, protocol, clientOptions);
            }
            catch (PlatformNotSupportedException ex) when (protocol == DnsClientProtocol.Quic)
            {
                httpHandler?.Dispose();
                httpHandler = CreateHttpHandler(useHttp3: false, serverAddressResolver);
                endpoint = GetHttpsFallbackEndpoint(upstream.Url);
                dnsClient = new DnsClient(endpoint, DnsClientProtocol.Https, CreateDnsClientOptions(dnsProxyOptions, httpHandler, serverAddressResolver));
                effectiveProtocol = DnsClientProtocol.Https;
                logger.LogWarning(ex, "DNS over QUIC is not supported on this platform for {Upstream}. Falling back to DNS over HTTPS.", upstream.Url);
            }

            upstreams.Add(new UpstreamDnsClientInfo(displayName, endpoint, dnsClient, httpHandler));
        }

        _upstreams = upstreams;
    }

    public IReadOnlyList<UpstreamDnsClientInfo> GetUpstreams() => _upstreams;

    public void Dispose()
    {
        foreach (var upstream in _upstreams)
        {
            upstream.Dispose();
        }
    }

    private static DnsClientProtocol GetProtocol(Uri url)
    {
        return url.Scheme.ToLowerInvariant() switch
        {
            "quic" => DnsClientProtocol.Quic,
            "h3" or "https" => DnsClientProtocol.Https,
            "tls" => DnsClientProtocol.Tls,
            "tcp" => DnsClientProtocol.Tcp,
            "udp" => DnsClientProtocol.Udp,
            _ => DnsClientProtocol.Https,
        };
    }

    private static string GetEndpoint(Uri url, DnsClientProtocol protocol)
    {
        if (protocol == DnsClientProtocol.Https)
            return url.Scheme.Equals("h3", StringComparison.OrdinalIgnoreCase) ? ChangeScheme(url, "https").ToString() : url.ToString();

        return url.IsDefaultPort ? url.Host : FormatHostAndPort(url.Host, url.Port);
    }

    private static string FormatHostAndPort(string host, int port)
    {
        return IPAddress.TryParse(host, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{host}]:{port}"
            : $"{host}:{port}";
    }

    private static string GetHttpsFallbackEndpoint(Uri url)
    {
        return ChangeScheme(new UriBuilder(url) { Path = "/dns-query" }.Uri, "https").ToString();
    }

    private static Uri ChangeScheme(Uri url, string scheme)
    {
        var builder = new UriBuilder(url)
        {
            Scheme = scheme,
        };

        if (url.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri;
    }

    private static SocketsHttpHandler CreateHttpHandler(bool useHttp3, Func<string, IReadOnlyList<IPAddress>>? serverAddressResolver)
    {
        var handler = new SocketsHttpHandler();
        if (serverAddressResolver is not null)
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = serverAddressResolver(context.DnsEndPoint.Host);
                foreach (var address in addresses)
                {
                    var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                    }
                }

                throw new SocketException((int)SocketError.HostNotFound);
            };
        }

        if (useHttp3)
        {
            handler.EnableMultipleHttp2Connections = true;
            handler.SslOptions.ApplicationProtocols =
            [
                new System.Net.Security.SslApplicationProtocol("h3"),
                new System.Net.Security.SslApplicationProtocol("h2"),
            ];
        }

        return handler;
    }

    private static DnsClientOptions CreateDnsClientOptions(DnsProxyOptions options, HttpMessageHandler? httpHandler, Func<string, IReadOnlyList<IPAddress>>? serverAddressResolver)
    {
        return new DnsClientOptions
        {
            DnssecValidationMode = options.DnssecValidationMode,
            HttpHandler = httpHandler,
            ServerAddressResolver = serverAddressResolver,
        };
    }

    private static Func<string, IReadOnlyList<IPAddress>>? CreateBootstrapResolver(DnsProxyOptions options)
    {
        var bootstrapServers = options.BootstrapDnsServers
            .Select(server => IPAddress.TryParse(server, out var address) ? address : null)
            .OfType<IPAddress>()
            .ToArray();
        if (bootstrapServers.Length == 0)
            return null;

        return host => ResolveWithBootstrapServers(host, bootstrapServers);
    }

    private static List<IPAddress> ResolveWithBootstrapServers(string host, IReadOnlyList<IPAddress> bootstrapServers)
    {
        if (IPAddress.TryParse(host, out var address))
            return [address];

        var addresses = new List<IPAddress>();
        foreach (var bootstrapServer in bootstrapServers)
        {
            using var client = new DnsClient(bootstrapServer.ToString(), DnsClientProtocol.Udp, new DnsClientOptions
            {
                Timeout = TimeSpan.FromSeconds(2),
                EnableEdns = false,
            });

            QueryBootstrapServer(client, host, DnsQueryType.A, addresses);
            QueryBootstrapServer(client, host, DnsQueryType.AAAA, addresses);
            if (addresses.Count > 0)
                break;
        }

        return addresses;
    }

    private static void QueryBootstrapServer(DnsClient client, string host, DnsQueryType queryType, List<IPAddress> addresses)
    {
        try
        {
            var response = client.QueryAsync(host, queryType).GetAwaiter().GetResult();
            addresses.AddRange(response.Answers.GetIPAddresses());
        }
        catch
        {
        }
    }
}
