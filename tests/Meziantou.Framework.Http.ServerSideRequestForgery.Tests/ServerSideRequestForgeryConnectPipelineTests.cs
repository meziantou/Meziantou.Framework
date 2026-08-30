using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Meziantou.Extensions.Logging.InMemory;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

public sealed class ServerSideRequestForgeryConnectPipelineTests
{
    private static readonly AsyncLocal<Guid> MeterTestContext = new();

    [Fact]
    public void ConfigureSsrf_SetsConnectCallback()
    {
        using var handler = new SocketsHttpHandler();
        handler.ConfigureSsrf(new ServerSideRequestForgeryOptions(), new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]));
        Assert.NotNull(handler.ConnectCallback);
    }

    [Theory]
    [InlineData(AddressFamily.InterNetwork)]
    [InlineData(AddressFamily.InterNetworkV6)]
    public void CreateConnectSocket_DisablesNagleAlgorithm(AddressFamily addressFamily)
    {
        using var socket = ServerSideRequestForgeryConnectPipeline.CreateConnectSocket(addressFamily);

        Assert.True(socket.NoDelay);
        Assert.Equal(addressFamily, socket.AddressFamily);
        Assert.Equal(SocketType.Stream, socket.SocketType);
        Assert.Equal(ProtocolType.Tcp, socket.ProtocolType);
    }

    [Fact]
    public async Task ConnectCallback_SendsRequestToTheValidatedAddress()
    {
        using var server = new LoopbackHttpServer();
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Add(Uri.UriSchemeHttp);
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        using var handler = new SocketsHttpHandler();
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);

        // 'example.invalid' cannot be resolved by real DNS (RFC2606), so a response can only come back if the
        // connection used the address the resolver returned instead of resolving the host again.
        var response = await httpClient.GetAsync(new Uri($"http://example.invalid:{server.Port}/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConnectCallback_FallsBackToAnotherValidatedAddressWhenTheConnectFails()
    {
        using var server = new LoopbackHttpServer();
        var options = new ServerSideRequestForgeryOptions
        {
            // Picks the first remaining address, so the unreachable IPv6 loopback is tried before the live server.
            ResolutionStrategy = new FirstAddressStrategy(),
        };
        options.SafeSchemes.Add(Uri.UriSchemeHttp);
        options.SafeIpNetworks.Add(IPNetwork.Parse("::1/128"));
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        using var handler = new SocketsHttpHandler();
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.IPv6Loopback, IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);

        // The server listens on the IPv4 loopback only, so the IPv6 attempt on the same port cannot succeed.
        var response = await httpClient.GetAsync(new Uri($"http://example.invalid:{server.Port}/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, server.AcceptedConnectionCount);
    }

    [Fact]
    public async Task ConnectCallback_AsksTheStrategyAgainWithoutTheAddressThatFailed()
    {
        using var server = new LoopbackHttpServer();
        var strategy = new RecordingStrategy();
        var options = new ServerSideRequestForgeryOptions
        {
            ResolutionStrategy = strategy,
        };
        options.SafeSchemes.Add(Uri.UriSchemeHttp);
        options.SafeIpNetworks.Add(IPNetwork.Parse("::1/128"));
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        using var handler = new SocketsHttpHandler();
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.IPv6Loopback, IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);

        _ = await httpClient.GetAsync(new Uri($"http://example.invalid:{server.Port}/"), TestContext.Current.CancellationToken);

        Assert.Collection(
            strategy.Calls,
            firstCall => Assert.Equal([IPAddress.IPv6Loopback, IPAddress.Loopback], firstCall),
            secondCall => Assert.Equal([IPAddress.Loopback], secondCall));
    }

    [Fact]
    public async Task ConnectCallback_DoesNotFallBackToAnAddressTheStrategyExcludes()
    {
        using var server = new LoopbackHttpServer();
        var options = new ServerSideRequestForgeryOptions
        {
            ResolutionStrategy = IpAddressResolutionStrategy.Ipv6Only,
        };
        options.SafeSchemes.Add(Uri.UriSchemeHttp);
        options.SafeIpNetworks.Add(IPNetwork.Parse("::1/128"));
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        using var handler = new SocketsHttpHandler();
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.IPv6Loopback, IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);

        // The live server is on the IPv4 loopback, which Ipv6Only must never fall back to even though the
        // address passed validation and is reachable.
        await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri($"http://example.invalid:{server.Port}/"), TestContext.Current.CancellationToken));

        Assert.Equal(0, server.AcceptedConnectionCount);
    }

    [Fact]
    public async Task ConnectCallback_SurfacesRejectionAsInnerExceptionOfHttpRequestException()
    {
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Add(Uri.UriSchemeHttp);

        using var handler = new SocketsHttpHandler();
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("http://example.invalid/"), TestContext.Current.CancellationToken));

        Assert.IsType<ServerSideRequestForgeryException>(exception.InnerException);
    }

    [Fact]
    public async Task ConnectCallback_DoesNotConnectWhenTheSchemeIsRejected()
    {
        using var server = new LoopbackHttpServer();
        var options = new ServerSideRequestForgeryOptions();
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        using var handler = new SocketsHttpHandler();
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri($"http://example.invalid:{server.Port}/"), TestContext.Current.CancellationToken));

        Assert.IsType<ServerSideRequestForgeryException>(exception.InnerException);
        Assert.Equal(0, server.AcceptedConnectionCount);
    }

    [Fact]
    public async Task ConnectCallback_RejectsConnectionThroughAProxy()
    {
        var options = CreateProxyTestOptions();
        using var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new WebProxy("http://127.0.0.1:9", BypassOnLocal: false),
        };
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("https://example.invalid/"), TestContext.Current.CancellationToken));

        // Without the guard this reaches the proxy and fails with a socket error instead.
        Assert.IsType<ServerSideRequestForgeryException>(exception.InnerException);
    }

    [Fact]
    public async Task ConnectCallback_AllowsDirectConnectionWhenTheProxyBypassesTheTarget()
    {
        var options = CreateProxyTestOptions();
        using var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new WebProxy("http://127.0.0.1:9", BypassOnLocal: false, BypassList: ["example\\.invalid"]),
        };
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("http://example.invalid:1/"), TestContext.Current.CancellationToken));

        // The connection is direct, so validation passes and the request fails only because nothing is listening.
        Assert.IsType<SocketException>(exception.InnerException);
    }

    [Fact]
    public async Task ConnectCallback_AllowsConnectionWhenUseProxyIsFalse()
    {
        var options = CreateProxyTestOptions();
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            Proxy = new WebProxy("http://127.0.0.1:9", BypassOnLocal: false),
        };
        handler.ConfigureSsrf(options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("http://example.invalid:1/"), TestContext.Current.CancellationToken));

        Assert.IsType<SocketException>(exception.InnerException);
    }

    [Fact]
    public void EnsureConnectionIsNotToAProxy_LogsRejectionReason()
    {
        using var loggerProvider = new InMemoryLoggerProvider();
        var options = new ServerSideRequestForgeryOptions
        {
            Logger = loggerProvider.CreateLogger("ssrf-test"),
        };
        using var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new WebProxy("http://proxy.invalid:8080", BypassOnLocal: false),
        };

        Assert.Throws<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.EnsureConnectionIsNotToAProxy(
            handler,
            new Uri("https://example.com/"),
            new DnsEndPoint("proxy.invalid", 8080),
            options));

        Assert.Contains(loggerProvider.Logs.Warnings, entry => entry.EventId.Id == 7);
    }

    [Fact]
    public void EnsureConnectionIsNotToAProxy_DoesNotThrowWhenTheEndPointIsNotTheProxy()
    {
        var options = new ServerSideRequestForgeryOptions();
        using var handler = new SocketsHttpHandler
        {
            UseProxy = true,
            Proxy = new WebProxy("http://proxy.invalid:8080", BypassOnLocal: false),
        };

        ServerSideRequestForgeryConnectPipeline.EnsureConnectionIsNotToAProxy(
            handler,
            new Uri("https://example.com/"),
            new DnsEndPoint("example.com", 443),
            options);
    }

    private static ServerSideRequestForgeryOptions CreateProxyTestOptions()
    {
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Add(Uri.UriSchemeHttp);
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));
        return options;
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
    public async Task ResolveAndSelectIpAddressAsync_AllowsCustomSafeScheme()
    {
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Add(Uri.UriSchemeHttp);

        var address = await ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("http://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 80),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None);

        Assert.Equal(IPAddress.Parse("203.0.113.10"), address);
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_RejectsAddressFromCustomUnsafeNetwork()
    {
        var options = new ServerSideRequestForgeryOptions();
        options.UnsafeIpNetworks.Clear();
        options.UnsafeIpNetworks.Add(IPNetwork.Parse("203.0.113.0/24"));

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());
    }

    [Theory]
    // IPv4 ranges that are reserved but were not covered by the original default list.
    [InlineData("192.0.0.170")]
    [InlineData("192.88.99.1")]
    // IPv6 forms that embed an unsafe IPv4 address. Each of these reaches 127.0.0.1 or 169.254.169.254
    // on a network that routes the corresponding transition mechanism.
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::127.0.0.1")]
    [InlineData("::169.254.169.254")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("64:ff9b::a9fe:a9fe")]
    [InlineData("2002:7f00:0001::")]
    [InlineData("2002:a9fe:a9fe::")]
    [InlineData("2001::1")]
    public async Task ResolveAndSelectIpAddressAsync_RejectsAddressEmbeddingUnsafeIpv4Target(string address)
    {
        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: new ServerSideRequestForgeryOptions(),
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse(address)]),
            cancellationToken: CancellationToken.None).AsTask());
    }

    [Theory]
    // Global unicast addresses that sit close to the newly blocked ranges and must stay reachable.
    [InlineData("2001:4860:4860::8888")]
    [InlineData("2003::1")]
    [InlineData("192.1.0.1")]
    [InlineData("192.89.0.1")]
    public async Task ResolveAndSelectIpAddressAsync_AllowsGlobalUnicastAddressNearBlockedRange(string address)
    {
        var options = new ServerSideRequestForgeryOptions
        {
            ResolutionStrategy = IpAddressResolutionStrategy.Random,
        };

        var selectedAddress = await ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse(address)]),
            cancellationToken: CancellationToken.None);

        Assert.Equal(IPAddress.Parse(address), selectedAddress);
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_UsesResolutionStrategyFromOptions()
    {
        var options = new ServerSideRequestForgeryOptions
        {
            ResolutionStrategy = IpAddressResolutionStrategy.Ipv6Only,
            DisallowMixedSafeAndUnsafeIpAddresses = false,
        };

        var address = await ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10"), IPAddress.Parse("2001:db8::10")]),
            cancellationToken: CancellationToken.None);

        Assert.Equal(IPAddress.Parse("2001:db8::10"), address);
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
    public async Task ResolveAndSelectIpAddressAsync_ThrowsHostNotFoundWhenNoIpAddressIsResolved()
    {
        var exception = await Assert.ThrowsAsync<SocketException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: new ServerSideRequestForgeryOptions(),
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([]),
            cancellationToken: CancellationToken.None).AsTask());

        Assert.Equal(SocketError.HostNotFound, exception.SocketErrorCode);
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

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_LogsRejectionReason()
    {
        using var loggerProvider = new InMemoryLoggerProvider();
        var options = new ServerSideRequestForgeryOptions
        {
            Logger = loggerProvider.CreateLogger("ssrf-test"),
        };
        options.SafeSchemes.Clear();
        options.SafeSchemes.Add(Uri.UriSchemeHttps);

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("http://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 80),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());

        Assert.Contains(loggerProvider.Logs.Warnings, entry => entry.EventId.Id == 1 && entry.Message.Contains("Scheme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_LogsHostMismatchRejectionReason()
    {
        using var loggerProvider = new InMemoryLoggerProvider();
        var options = new ServerSideRequestForgeryOptions
        {
            Logger = loggerProvider.CreateLogger("ssrf-test"),
        };

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("other.example", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());

        Assert.Contains(loggerProvider.Logs.Warnings, entry => entry.EventId.Id == 2);
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_LogsMixedAddressesRejectionReason()
    {
        using var loggerProvider = new InMemoryLoggerProvider();
        var options = new ServerSideRequestForgeryOptions
        {
            Logger = loggerProvider.CreateLogger("ssrf-test"),
        };

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("https://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 443),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Loopback, IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());

        Assert.Contains(loggerProvider.Logs.Warnings, entry => entry.EventId.Id == 4);
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_IncrementsRejectedRequestsCounter()
    {
        const string ExpectedReasonTag = "resolution_strategy_failure";
        var context = Guid.NewGuid();
        var rejectedRequestCount = 0L;
        string? reasonTag = null;
        var previousContext = MeterTestContext.Value;
        MeterTestContext.Value = context;
        try
        {
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ServerSideRequestForgeryMetrics.MeterName && instrument.Name == ServerSideRequestForgeryMetrics.RejectedRequestsCounterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                _ = instrument;
                _ = state;
                if (MeterTestContext.Value != context)
                {
                    return;
                }

                var hasExpectedReasonTag = false;
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, ServerSideRequestForgeryMetrics.ReasonTagName, StringComparison.Ordinal))
                    {
                        reasonTag = tag.Value?.ToString();
                        hasExpectedReasonTag = string.Equals(reasonTag, ExpectedReasonTag, StringComparison.Ordinal);
                    }
                }

                if (!hasExpectedReasonTag)
                {
                    return;
                }

                Interlocked.Add(ref rejectedRequestCount, measurement);
            });
            listener.Start();

            var options = new ServerSideRequestForgeryOptions();
            options.ResolutionStrategy = new ThrowingResolutionStrategy();

            await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
                requestUri: new Uri("https://example.com"),
                dnsEndPoint: new DnsEndPoint("example.com", 443),
                options: options,
                dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
                cancellationToken: CancellationToken.None).AsTask());

            Assert.Equal(1, rejectedRequestCount);
            Assert.Equal(ExpectedReasonTag, reasonTag);
        }
        finally
        {
            MeterTestContext.Value = previousContext;
        }
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_DoesNotIncrementRejectedRequestsCounterForHostNotFound()
    {
        const string ExpectedReasonTag = "resolution_strategy_failure";
        var context = Guid.NewGuid();
        var rejectedRequestCount = 0L;
        var previousContext = MeterTestContext.Value;
        MeterTestContext.Value = context;
        try
        {
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == ServerSideRequestForgeryMetrics.MeterName && instrument.Name == ServerSideRequestForgeryMetrics.RejectedRequestsCounterName)
                {
                    meterListener.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                _ = instrument;
                _ = state;
                if (MeterTestContext.Value != context)
                {
                    return;
                }

                var hasExpectedReasonTag = false;
                foreach (var tag in tags)
                {
                    if (string.Equals(tag.Key, ServerSideRequestForgeryMetrics.ReasonTagName, StringComparison.Ordinal) && string.Equals(tag.Value?.ToString(), ExpectedReasonTag, StringComparison.Ordinal))
                    {
                        hasExpectedReasonTag = true;
                        break;
                    }
                }

                if (!hasExpectedReasonTag)
                {
                    return;
                }

                Interlocked.Add(ref rejectedRequestCount, measurement);
            });
            listener.Start();

            await Assert.ThrowsAsync<SocketException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
                requestUri: new Uri("https://example.com"),
                dnsEndPoint: new DnsEndPoint("example.com", 443),
                options: new ServerSideRequestForgeryOptions(),
                dnsIpAddressResolver: new FakeDnsIpAddressResolver([]),
                cancellationToken: CancellationToken.None).AsTask());

            Assert.Equal(0, rejectedRequestCount);
        }
        finally
        {
            MeterTestContext.Value = previousContext;
        }
    }

    private sealed class FirstAddressStrategy : IpAddressResolutionStrategy
    {
        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = options;
            _ = cancellationToken;
            if (addresses.Count == 0)
                throw new ServerSideRequestForgeryException("No safe IP addresses available after validation.");

            return ValueTask.FromResult(addresses[0]);
        }
    }

    private sealed class RecordingStrategy : IpAddressResolutionStrategy
    {
        public List<IPAddress[]> Calls { get; } = [];

        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = options;
            _ = cancellationToken;
            Calls.Add([.. addresses]);
            if (addresses.Count == 0)
                throw new ServerSideRequestForgeryException("No safe IP addresses available after validation.");

            return ValueTask.FromResult(addresses[0]);
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

    private sealed class ThrowingResolutionStrategy : IpAddressResolutionStrategy
    {
        protected internal override ValueTask<IPAddress> ResolveAsync(IReadOnlyList<IPAddress> addresses, ServerSideRequestForgeryOptions options, CancellationToken cancellationToken)
        {
            _ = addresses;
            _ = options;
            _ = cancellationToken;
            throw new ServerSideRequestForgeryException("strategy failure");
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
