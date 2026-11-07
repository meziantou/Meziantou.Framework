using Meziantou.Framework.DnsClient;
using Meziantou.Framework.DnsClient.Query;
using Meziantou.Framework.DnsClient.Response;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.DnsProxy.Forwarding;

internal sealed class UpstreamDnsClientFactory : IUpstreamDnsClientProvider, IDisposable
{
    private readonly List<UpstreamDnsClientInfo> _upstreams;
    private readonly IReadOnlyList<IUpstreamDnsClient> _clients;

    public UpstreamDnsClientFactory(IOptions<DnsProxyOptions> options, TimeProvider timeProvider, ILogger<UpstreamDnsClientFactory> logger)
    {
        var upstreams = new List<UpstreamDnsClientInfo>();
        var dnsProxyOptions = options.Value;
        var bootstrapResolver = CreateBootstrapResolver(dnsProxyOptions, timeProvider);
        Func<string, IReadOnlyList<IPAddress>>? serverAddressResolver = bootstrapResolver is null ? null : bootstrapResolver.Resolve;
        foreach (var upstream in dnsProxyOptions.Upstreams.OrderBy(upstream => upstream.Priority))
        {
            if (upstream.Url is null)
            {
                continue;
            }

            var protocol = GetProtocol(upstream.Url);
            var endpoint = GetEndpoint(upstream.Url, protocol);
            var displayName = string.IsNullOrWhiteSpace(upstream.Name) ? upstream.Url.OriginalString : $"{upstream.Name} ({upstream.Url.OriginalString})";
            SocketsHttpHandler? httpHandler = protocol == DnsClientProtocol.Https ? CreateHttpHandler(upstream.Url.Scheme.Equals("h3", StringComparison.OrdinalIgnoreCase), bootstrapResolver) : null;
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
                httpHandler = CreateHttpHandler(useHttp3: false, bootstrapResolver);
                endpoint = GetHttpsFallbackEndpoint(upstream.Url);
                dnsClient = new DnsClient(endpoint, DnsClientProtocol.Https, CreateDnsClientOptions(dnsProxyOptions, httpHandler, serverAddressResolver));
                effectiveProtocol = DnsClientProtocol.Https;
                logger.LogWarning(ex, "DNS over QUIC is not supported on this platform for {Upstream}. Falling back to DNS over HTTPS.", upstream.Url);
            }

            upstreams.Add(new UpstreamDnsClientInfo(displayName, endpoint, dnsClient, httpHandler));
        }

        _upstreams = upstreams;
        _clients = [.. upstreams];
    }

    public IReadOnlyList<IUpstreamDnsClient> GetUpstreams() => _clients;

    public IReadOnlyList<UpstreamDnsClientInfo> GetUpstreamDetails() => _upstreams;

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

    private static SocketsHttpHandler CreateHttpHandler(bool useHttp3, BootstrapDnsResolver? bootstrapResolver)
    {
        var handler = new SocketsHttpHandler();
        if (bootstrapResolver is not null)
        {
            handler.ConnectCallback = async (context, cancellationToken) =>
            {
                var addresses = await bootstrapResolver.ResolveAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
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

    private static BootstrapDnsResolver? CreateBootstrapResolver(DnsProxyOptions options, TimeProvider timeProvider)
    {
        var bootstrapServers = options.BootstrapDnsServers
            .Select(server => IPAddress.TryParse(server, out var address) ? address : null)
            .OfType<IPAddress>()
            .ToArray();
        if (bootstrapServers.Length == 0)
            return null;

        return new BootstrapDnsResolver(bootstrapServers, timeProvider);
    }
}
