using System.Net;
using BenchmarkDotNet.Attributes;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Benchmarks;

/// <summary>Measures the validation the SSRF protection runs before each outbound connection, without the DNS lookup and the connect.</summary>
[MemoryDiagnoser]
public class ConnectPipelineBenchmark : IDisposable
{
    private readonly ServerSideRequestForgeryOptions _defaultOptions = new();
    private readonly ServerSideRequestForgeryOptions _optionsWithSafeNetworks = CreateOptionsWithSafeNetworks();
    private Uri _requestUri = null!;
    private DnsEndPoint _dnsEndPoint = null!;
    private IDnsIpAddressResolver _resolver = null!;
    private HttpRequestMessage _request = null!;
    private SocketsHttpHandler _handlerWithoutProxy = null!;
    private SocketsHttpHandler _handlerWithDefaultProxy = null!;
    private SocketsHttpHandler _handlerWithBypassedProxy = null!;

    /// <summary>The destination and the addresses its lookup returns.</summary>
    [Params("Ipv4", "DualStack", "Ipv6Literal")]
    public string Scenario { get; set; } = "Ipv4";

    [GlobalSetup]
    public void Setup()
    {
        (_requestUri, _resolver) = Scenario switch
        {
            "Ipv4" => (new Uri("https://example.com/"), (IDnsIpAddressResolver)new FixedDnsIpAddressResolver([IPAddress.Parse("93.184.215.14")])),
            "DualStack" => (new Uri("https://example.com/"), new FixedDnsIpAddressResolver(
            [
                IPAddress.Parse("2606:2800:21f:cb07:6820:80da:af6b:8b2c"),
                IPAddress.Parse("2606:2800:21f:cb07:6820:80da:af6b:8b2d"),
                IPAddress.Parse("93.184.215.14"),
                IPAddress.Parse("93.184.215.15"),
            ])),
            "Ipv6Literal" => (new Uri("https://[2606:2800:21f:cb07:6820:80da:af6b:8b2c]/"), SystemDnsIpAddressResolver.Instance),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'"),
        };

        _dnsEndPoint = new DnsEndPoint(_requestUri.IdnHost, _requestUri.Port);
        _request = new HttpRequestMessage(HttpMethod.Get, _requestUri);
        _handlerWithoutProxy = new SocketsHttpHandler { UseProxy = false };
        _handlerWithDefaultProxy = new SocketsHttpHandler();
        _handlerWithBypassedProxy = new SocketsHttpHandler { Proxy = new WebProxy("http://proxy.example.net:8080") { BypassList = [".*"] } };
    }

    [GlobalCleanup]
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _request.Dispose();
            _handlerWithoutProxy.Dispose();
            _handlerWithDefaultProxy.Dispose();
            _handlerWithBypassedProxy.Dispose();
        }
    }

    [Benchmark]
    public ValueTask<IPAddress> ResolveAndSelect() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(_requestUri, _dnsEndPoint, _defaultOptions, _resolver, CancellationToken.None);

    [Benchmark]
    public ValueTask<IPAddress> ResolveAndSelect_SafeNetworks() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(_requestUri, _dnsEndPoint, _optionsWithSafeNetworks, _resolver, CancellationToken.None);

    [Benchmark]
    public void ProxyCheck_UseProxyFalse() => ServerSideRequestForgeryConnectPipeline.EnsureConnectionIsNotToAProxy(_handlerWithoutProxy, _request, _dnsEndPoint, _defaultOptions);

    [Benchmark]
    public void ProxyCheck_DefaultProxy() => ServerSideRequestForgeryConnectPipeline.EnsureConnectionIsNotToAProxy(_handlerWithDefaultProxy, _request, _dnsEndPoint, _defaultOptions);

    [Benchmark]
    public void ProxyCheck_BypassedWebProxy() => ServerSideRequestForgeryConnectPipeline.EnsureConnectionIsNotToAProxy(_handlerWithBypassedProxy, _request, _dnsEndPoint, _defaultOptions);

    private static ServerSideRequestForgeryOptions CreateOptionsWithSafeNetworks()
    {
        var options = new ServerSideRequestForgeryOptions();
        options.SafeIpNetworks.Add(IPNetwork.Parse("10.1.0.0/16"));
        options.SafeIpNetworks.Add(IPNetwork.Parse("fd00:1::/32"));
        return options;
    }

    private sealed class FixedDnsIpAddressResolver(IReadOnlyList<IPAddress> addresses) : IDnsIpAddressResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken) => ValueTask.FromResult(addresses);
    }
}
