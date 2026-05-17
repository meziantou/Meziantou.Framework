using System.Net;
using Meziantou.Framework.Http.ServerSideRequestForgery;
using Xunit;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

public sealed class ServerSideRequestForgeryConnectPipelineTests
{
    [Fact]
    public void ConfigureSsrf_SetsConnectCallback()
    {
        using var handler = new SocketsHttpHandler();
        handler.ConfigureSsrf(new ServerSideRequestForgeryOptions(), new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]));
        Assert.NotNull(handler.ConnectCallback);
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_RejectsUnsafeScheme()
    {
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Clear();
        options.SafeSchemes.Add("https");

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("http://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 80),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_RejectsMixedResultsByDefault()
    {
        var options = new ServerSideRequestForgeryOptions();

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Loopback, IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_AllowsMixedResultsWhenConfigured()
    {
        var options = new ServerSideRequestForgeryOptions
        {
            DisallowMixedSafeAndUnsafeIpAddresses = false,
            ResolutionStrategy = IpAddressResolutionStrategy.Ipv4Only,
        };

        var address = await ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Loopback, IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None);

        Assert.Equal(IPAddress.Parse("203.0.113.10"), address);
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_SafeNetworkOverridesUnsafeNetwork()
    {
        var options = new ServerSideRequestForgeryOptions
        {
            ResolutionStrategy = IpAddressResolutionStrategy.Ipv4Only,
        };
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        var address = await ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Loopback]),
            cancellationToken: CancellationToken.None);

        Assert.Equal(IPAddress.Loopback, address);
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_RejectsHostMismatch()
    {
        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("other.example", 443),
            options: new ServerSideRequestForgeryOptions(),
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_RejectsAddressOutsideValidatedSet()
    {
        var options = new ServerSideRequestForgeryOptions
        {
            ResolutionStrategy = new ReturningUnknownAddressStrategy(),
        };

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_PassesOptionsToResolutionStrategy()
    {
        var strategy = new OptionsAwareStrategy();
        var options = new ServerSideRequestForgeryOptions
        {
            ResolutionStrategy = strategy,
        };

        _ = await ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None);

        Assert.Same(options, strategy.LastSeenOptions);
    }

    private sealed class FakeDnsIpAddressResolver(IReadOnlyList<IPAddress> addresses) : IDnsIpAddressResolver
    {
        public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            _ = host;
            _ = cancellationToken;
            return ValueTask.FromResult(addresses);
        }
    }

    private sealed class ReturningUnknownAddressStrategy : IpAddressResolutionStrategy
    {
        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = addresses;
            _ = options;
            _ = cancellationToken;
            return ValueTask.FromResult(IPAddress.Parse("198.51.100.11"));
        }
    }

    private sealed class OptionsAwareStrategy : IpAddressResolutionStrategy
    {
        public ServerSideRequestForgeryOptions? LastSeenOptions { get; private set; }

        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastSeenOptions = options;
            return ValueTask.FromResult(addresses[0]);
        }
    }
}
