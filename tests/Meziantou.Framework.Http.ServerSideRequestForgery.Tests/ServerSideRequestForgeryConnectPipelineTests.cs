using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.Extensions.Logging;

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
        var logger = new TestLogger();
        var options = new ServerSideRequestForgeryOptions
        {
            Logger = logger,
        };
        options.SafeSchemes.Clear();
        options.SafeSchemes.Add(Uri.UriSchemeHttps);

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("http://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 80),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());

        Assert.Contains(logger.Entries, entry => entry.EventId == 1 && entry.Level == LogLevel.Warning && entry.Message.Contains("Scheme", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResolveAndSelectIpAddressAsync_IncrementsRejectedRequestsCounter()
    {
        var rejectedRequestCount = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = static (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == ServerSideRequestForgeryMetrics.MeterName && instrument.Name == ServerSideRequestForgeryMetrics.RejectedRequestsCounterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            _ = instrument;
            _ = tags;
            _ = state;
            Interlocked.Add(ref rejectedRequestCount, measurement);
        });
        listener.Start();

        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Clear();
        options.SafeSchemes.Add(Uri.UriSchemeHttps);

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryConnectPipeline.ResolveAndSelectIpAddressAsync(
            requestUri: new Uri("http://example.com"),
            dnsEndPoint: new DnsEndPoint("example.com", 80),
            options: options,
            dnsIpAddressResolver: new FakeDnsIpAddressResolver([IPAddress.Parse("203.0.113.10")]),
            cancellationToken: CancellationToken.None).AsTask());

        Assert.Equal(1, rejectedRequestCount);
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

    private sealed class TestLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            _ = state;
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            _ = logLevel;
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _ = state;
            Entries.Add(new LogEntry(logLevel, eventId.Id, formatter(state, exception)));
        }

        public sealed record LogEntry(LogLevel Level, int EventId, string Message);
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
