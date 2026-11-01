using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Meziantou.Framework.TemporaryContainers.Strategies;

namespace Meziantou.Framework.TemporaryContainers.Tests;

public sealed class WaitStrategyTests
{
    private static readonly TimeSpan HangTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public void ForCommand_RejectsAnEmptyCommand()
    {
        Assert.Throws<ArgumentException>("command", () => Wait.ForCommand());
    }

    [Fact]
    public void ForHttp_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentException>("configure", () => Wait.ForHttp(8080, "/", options => options.RequestTimeout = TimeSpan.Zero));
        Assert.Throws<ArgumentException>("configure", () => Wait.ForHttp(8080, "/", options => options.PollInterval = TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void ForAny_RequiresAStrategy()
    {
        Assert.Throws<ArgumentException>("strategies", () => Wait.ForAny());
    }

    [Fact]
    public void WithTimeout_RequiresAPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>("timeout", () => Wait.ForDelay(TimeSpan.Zero).WithTimeout(TimeSpan.Zero));
    }

    [Fact]
    public void ToString_DescribesTheComposedStrategies()
    {
        var strategy = Wait.ForAll(Wait.ForCommand("pg_isready"), Wait.ForHttp(8080, "/health")).WithTimeout(TimeSpan.FromSeconds(5));

        Assert.Equal("all of (command 'pg_isready', HTTP GET /health on port 8080) (timeout 00:00:05)", strategy.ToString());
    }

    [Fact]
    public async Task ForHttp_WaitsForASuccessStatusCode()
    {
        await using var server = new TestHttpServer(index => index < 2 ? (503, "starting") : (200, "ready"));
        var strategy = (HttpWaitStrategy)Wait.ForHttp(8080, "/health", options => options.PollInterval = TimeSpan.Zero);

        await strategy.WaitCoreAsync(server.GetUri("http", "/health"), XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken);

        Assert.Equal(3, server.RequestCount);
        Assert.Equal("/health", server.LastRequestPath);
    }

    [Fact]
    public async Task ForHttp_WaitsForAnExpectedStatusCode()
    {
        await using var server = new TestHttpServer(index => index < 2 ? (200, "") : (401, ""));
        var strategy = (HttpWaitStrategy)Wait.ForHttp(8080, "/", options =>
        {
            options.PollInterval = TimeSpan.Zero;
            options.StatusCodes.Add(HttpStatusCode.Unauthorized);
        });

        await strategy.WaitCoreAsync(server.GetUri("http", "/"), XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken);

        Assert.Equal(3, server.RequestCount);
    }

    [Fact]
    public async Task ForHttp_WaitsForTheResponseBody()
    {
        await using var server = new TestHttpServer(index => index < 2 ? (200, "{\"status\":\"starting\"}") : (200, "{\"status\":\"ready\"}"));
        var strategy = (HttpWaitStrategy)Wait.ForHttp(8080, "/", options =>
        {
            options.PollInterval = TimeSpan.Zero;
            options.Method = HttpMethod.Post;
            options.ResponseBodyPredicate = body => body.Contains("ready", StringComparison.Ordinal);
        });

        await strategy.WaitCoreAsync(server.GetUri("http", "/"), XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken);

        Assert.Equal(3, server.RequestCount);
        Assert.Equal("POST", server.LastRequestMethod);
    }

    [Fact]
    public async Task ForHttp_AcceptsASelfSignedCertificateWhenValidationIsDisabled()
    {
        using var certificate = CreateSelfSignedCertificate();
        await using var server = new TestHttpServer(_ => (200, ""), certificate);
        var strategy = (HttpWaitStrategy)Wait.ForHttp(8443, "/", options =>
        {
            options.UseHttps = true;
            options.ValidateServerCertificate = false;
        });

        await strategy.WaitCoreAsync(server.GetUri("https", "/"), XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken);

        Assert.Equal(1, server.RequestCount);
    }

    [Fact]
    public async Task ForHttp_ValidatesTheCertificateByDefault()
    {
        using var certificate = CreateSelfSignedCertificate();
        await using var server = new TestHttpServer(_ => (200, ""), certificate);
        var strategy = (HttpWaitStrategy)Wait.ForHttp(8443, "/", options =>
        {
            options.UseHttps = true;
            options.PollInterval = TimeSpan.FromMilliseconds(10);
        });
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(XunitCancellationToken);

        var wait = strategy.WaitCoreAsync(server.GetUri("https", "/"), cts.Token);
        while (server.ConnectionCount < 2)
        {
            await Task.Delay(10, XunitCancellationToken);
        }

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.Equal(0, server.RequestCount);
    }

    [Fact]
    public async Task ForHealthcheck_WaitsUntilTheContainerIsHealthy()
    {
        var inspectCount = 0;
        var strategy = (HealthcheckWaitStrategy)Wait.ForHealthcheck(options => options.PollInterval = TimeSpan.Zero);

        await strategy.WaitCoreAsync(_ =>
        {
            inspectCount++;
            return Task.FromResult(CreateInfo(ContainerState.Running, inspectCount < 3 ? ContainerHealthStatus.Starting : ContainerHealthStatus.Healthy));
        }, XunitCancellationToken);

        Assert.Equal(3, inspectCount);
    }

    [Fact]
    public async Task ForHealthcheck_FailsAtOnceWithoutAHealthcheck()
    {
        var strategy = (HealthcheckWaitStrategy)Wait.ForHealthcheck();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.WaitCoreAsync(_ => Task.FromResult(CreateInfo(ContainerState.Running, ContainerHealthStatus.None)), XunitCancellationToken));

        Assert.Contains("HEALTHCHECK", exception.Message);
    }

    [Fact]
    public async Task ForHealthcheck_ReportsTheOutputOfAFailedHealthcheck()
    {
        var strategy = (HealthcheckWaitStrategy)Wait.ForHealthcheck();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.WaitCoreAsync(_ => Task.FromResult(CreateInfo(ContainerState.Running, ContainerHealthStatus.Unhealthy, "connection refused\n")), XunitCancellationToken));

        Assert.Contains("health check of the container failed", exception.Message);
        Assert.Contains("connection refused", exception.Message);
    }

    [Fact]
    public async Task ForHealthcheck_FailsWhenTheContainerStops()
    {
        var strategy = (HealthcheckWaitStrategy)Wait.ForHealthcheck();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.WaitCoreAsync(_ => Task.FromResult(CreateInfo(ContainerState.Exited, ContainerHealthStatus.Starting) with { ExitCode = 3 }), XunitCancellationToken));

        Assert.Contains("exit code 3", exception.Message);
    }

    [Fact]
    public async Task WithTimeout_ReportsTheStrategyThatTimedOut()
    {
        await using var container = CreateContainer();
        var strategy = new FakeWaitStrategy("never", ct => Task.Delay(Timeout.InfiniteTimeSpan, ct)).WithTimeout(TimeSpan.FromMilliseconds(50));

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => strategy.WaitAsync(container, XunitCancellationToken));

        Assert.Contains("'never'", exception.Message);
    }

    [Fact]
    public async Task WithTimeout_LetsTheCallerCancellationThrough()
    {
        await using var container = CreateContainer();
        var strategy = new FakeWaitStrategy("never", ct => Task.Delay(Timeout.InfiniteTimeSpan, ct)).WithTimeout(TimeSpan.FromHours(1));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => strategy.WaitAsync(container, cts.Token));
    }

    [Fact]
    public async Task ForAll_RunsTheStrategiesConcurrently()
    {
        await using var container = CreateContainer();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // The first strategy only completes once the second one runs: run one after the other, they would never complete.
        var strategy = Wait.ForAll(
            new FakeWaitStrategy("first", ct => secondStarted.Task.WaitAsync(ct)),
            new FakeWaitStrategy("second", _ =>
            {
                secondStarted.SetResult();
                return Task.CompletedTask;
            }));

        await strategy.WaitAsync(container, XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken);
    }

    [Fact]
    public async Task ForAll_StopsTheOtherStrategiesOnTheFirstFailure()
    {
        await using var container = CreateContainer();
        var otherCanceled = false;
        var strategy = Wait.ForAll(
            new FakeWaitStrategy("failing", _ => throw new InvalidOperationException("boom")),
            new FakeWaitStrategy("pending", async ct =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException)
                {
                    otherCanceled = true;
                    throw;
                }
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.WaitAsync(container, XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken));

        Assert.Equal("boom", exception.Message);
        Assert.True(otherCanceled);
    }

    [Fact]
    public async Task ForAny_CompletesWithTheFirstStrategyAndStopsTheOthers()
    {
        await using var container = CreateContainer();
        var otherCanceled = false;
        var strategy = Wait.ForAny(
            new FakeWaitStrategy("pending", async ct =>
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                catch (OperationCanceledException)
                {
                    otherCanceled = true;
                    throw;
                }
            }),
            new FakeWaitStrategy("ready", _ => Task.CompletedTask));

        await strategy.WaitAsync(container, XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken);

        Assert.True(otherCanceled);
    }

    [Fact]
    public async Task ForAny_KeepsWaitingAfterAFailure()
    {
        await using var container = CreateContainer();
        var firstFailed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var strategy = Wait.ForAny(
            new FakeWaitStrategy("failing", _ =>
            {
                firstFailed.SetResult();
                throw new InvalidOperationException("boom");
            }),
            new FakeWaitStrategy("ready later", ct => firstFailed.Task.WaitAsync(ct)));

        await strategy.WaitAsync(container, XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken);
    }

    [Fact]
    public async Task ForAny_FailsWhenAllTheStrategiesFail()
    {
        await using var container = CreateContainer();
        var strategy = Wait.ForAny(
            new FakeWaitStrategy("first", _ => throw new InvalidOperationException("first")),
            new FakeWaitStrategy("second", _ => throw new InvalidOperationException("second")));

        var exception = await Assert.ThrowsAsync<AggregateException>(() => strategy.WaitAsync(container, XunitCancellationToken).WaitAsync(HangTimeout, XunitCancellationToken));

        Assert.HasCount(2, exception.InnerExceptions);
    }

    [Fact]
    public async Task ForAny_ReportsTheFailureOfASingleStrategyAsIs()
    {
        await using var container = CreateContainer();
        var strategy = Wait.ForAny(new FakeWaitStrategy("failing", _ => throw new InvalidOperationException("boom")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => strategy.WaitAsync(container, XunitCancellationToken));

        Assert.Equal("boom", exception.Message);
    }

    private static TemporaryContainer CreateContainer() => new(new ContainerDefinition("busybox"));

    private static ContainerInfo CreateInfo(ContainerState state, ContainerHealthStatus health, string? output = null)
        => new() { Id = "id", Name = "name", State = state, Health = health, HealthCheckOutput = output };

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        // SslStream cannot use the ephemeral key of a certificate created in memory on every platform: round-trip it through a PFX.
        return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), password: null);
    }

    private sealed class FakeWaitStrategy(string name, Func<CancellationToken, Task> wait) : IWaitStrategy
    {
        public Task WaitAsync(TemporaryContainer container, CancellationToken cancellationToken) => wait(cancellationToken);

        public override string ToString() => name;
    }

    /// <summary>A minimal HTTP/1.1 server that answers each request from a callback, so the response can change over time.</summary>
    private sealed class TestHttpServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Func<int, (int StatusCode, string Body)> _respond;
        private readonly X509Certificate2? _certificate;
        private readonly Task _acceptLoop;
        private int _connectionCount;
        private int _requestCount;

        public TestHttpServer(Func<int, (int StatusCode, string Body)> respond, X509Certificate2? certificate = null)
        {
            _respond = respond;
            _certificate = certificate;
            _listener.Start();
            _acceptLoop = AcceptLoopAsync();
        }

        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        public int RequestCount => Volatile.Read(ref _requestCount);

        public string? LastRequestMethod { get; private set; }

        public string? LastRequestPath { get; private set; }

        public Uri GetUri(string scheme, string path) => new($"{scheme}://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}");

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (true)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                    _ = HandleAsync(client);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            {
                Interlocked.Increment(ref _connectionCount);
                try
                {
                    Stream stream = client.GetStream();
                    if (_certificate is not null)
                    {
                        var sslStream = new SslStream(stream);
                        await sslStream.AuthenticateAsServerAsync(_certificate);
                        stream = sslStream;
                    }

                    await using (stream)
                    {
                        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                        var requestLine = await reader.ReadLineAsync(_cts.Token);
                        if (requestLine is null)
                            return; // The client closed the connection, for instance after rejecting the certificate

                        var contentLength = 0;
                        while (await reader.ReadLineAsync(_cts.Token) is { Length: > 0 } header)
                        {
                            if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                contentLength = int.Parse(header["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                        }

                        // ReadBlockAsync waits for data even with an empty buffer.
                        if (contentLength > 0)
                        {
                            await reader.ReadBlockAsync(new char[contentLength], _cts.Token);
                        }

                        var parts = requestLine.Split(' ');
                        LastRequestMethod = parts.Length > 0 ? parts[0] : null;
                        LastRequestPath = parts.Length > 1 ? parts[1] : null;

                        var (statusCode, body) = _respond(Interlocked.Increment(ref _requestCount) - 1);
                        var bodyBytes = Encoding.UTF8.GetBytes(body);
                        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {statusCode} Status\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(head, _cts.Token);
                        await stream.WriteAsync(bodyBytes, _cts.Token);
                    }
                }
                catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException or SocketException)
                {
                    // The client rejected the certificate or went away.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _cts.CancelAsync();
            await _acceptLoop;
            _listener.Dispose();
            _cts.Dispose();
        }
    }
}
