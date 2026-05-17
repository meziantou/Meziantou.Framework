using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
