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

    private sealed class LoopbackHttpServer : IDisposable
    {
        private const string Response = "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok";

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private int _acceptedConnectionCount;

        public LoopbackHttpServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, port: 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = Task.Run(() => AcceptLoopAsync(_cancellationTokenSource.Token));
        }

        public int Port { get; }

        public int AcceptedConnectionCount => Volatile.Read(ref _acceptedConnectionCount);

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    Interlocked.Increment(ref _acceptedConnectionCount);

                    using var stream = client.GetStream();
                    await ReadRequestHeadersAsync(stream, cancellationToken).ConfigureAwait(false);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(Response), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static async Task ReadRequestHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;

                count += read;
                if (Encoding.ASCII.GetString(buffer, 0, count).Contains("\r\n\r\n", StringComparison.Ordinal))
                    break;
            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            _listener.Dispose();
            _cancellationTokenSource.Dispose();
        }
    }
}
