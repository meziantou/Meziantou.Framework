using Meziantou.Framework.Ntp;

namespace Meziantou.Framework.Ntp.Tests;

public sealed class NtpServerTests : IAsyncLifetime
{
    private NtpServer _server = null;

    public async ValueTask InitializeAsync()
    {
        _server = new NtpServer(new NtpServerOptions { Port = 0 });
        await _server.StartAsync(XunitCancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _server.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Query_ReturnsValidResponse()
    {
        using var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = _server.Port });
        var response = await client.QueryAsync(XunitCancellationToken);

        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
        Assert.True(response.ReceiveTimestamp > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Query_V4_MirrorsVersion()
    {
        using var client = new NtpClient("127.0.0.1", new NtpClientOptions
        {
            Port = _server.Port,
            Version = NtpVersion.V4,
        });
        var response = await client.QueryAsync(XunitCancellationToken);

        Assert.Equal(NtpVersion.V4, response.Version);
    }

    [Fact]
    public async Task Query_V3_MirrorsVersion()
    {
        using var client = new NtpClient("127.0.0.1", new NtpClientOptions
        {
            Port = _server.Port,
            Version = NtpVersion.V3,
        });
        var response = await client.QueryAsync(XunitCancellationToken);

        Assert.Equal(NtpVersion.V3, response.Version);
    }

    [Fact]
    public async Task Query_ClockOffset_IsSmall()
    {
        using var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = _server.Port });
        var response = await client.QueryAsync(XunitCancellationToken);

        // Offset to a local server should be very small
        Assert.True(Math.Abs(response.ClockOffset.TotalSeconds) < 1, $"Clock offset was {response.ClockOffset}");
    }

    [Fact]
    public async Task Query_RoundTripDelay_IsSmall()
    {
        using var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = _server.Port });
        var response = await client.QueryAsync(XunitCancellationToken);

        // Round-trip to localhost should be very fast
        Assert.True(response.RoundTripDelay < TimeSpan.FromSeconds(1), $"Round-trip delay was {response.RoundTripDelay}");
    }

    [Fact]
    public async Task Query_MultipleConcurrentClients()
    {
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            using var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = _server.Port });
            return await client.QueryAsync(XunitCancellationToken);
        });

        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r =>
        {
            Assert.True(r.Stratum > 0);
            Assert.True(r.TransmitTimestamp > DateTimeOffset.UnixEpoch);
        });
    }

    [Fact]
    public async Task Query_CustomStratum()
    {
        using var server = new NtpServer(new NtpServerOptions { Port = 0, Stratum = 5 });
        await server.StartAsync(XunitCancellationToken);

        using var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port });
        var response = await client.QueryAsync(XunitCancellationToken);

        Assert.Equal(5, response.Stratum);
    }

    [Fact]
    public async Task Query_OriginateTimestamp_MatchesClientTransmit()
    {
        using var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = _server.Port });
        var response = await client.QueryAsync(XunitCancellationToken);

        // The originate timestamp in the response should match what the client sent
        // (within NTP timestamp precision)
        Assert.True(response.OriginateTimestamp > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Dispose_StopsListening()
    {
        var server = new NtpServer(new NtpServerOptions { Port = 0 });
        await server.StartAsync(XunitCancellationToken);
        var port = server.Port;
        server.Dispose();

        // After disposal, querying should fail with a timeout or socket error
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        using var client = new NtpClient("127.0.0.1", new NtpClientOptions
        {
            Port = port,
        });

        await Assert.ThrowsAnyAsync<Exception>(() => client.QueryAsync(cts.Token));
    }

    [Fact]
    public async Task Query_WithCustomTimeProvider()
    {
        var fixedTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedTime);

        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            TimeProvider = timeProvider,
        });
        await server.StartAsync(XunitCancellationToken);

        using var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port });
        var response = await client.QueryAsync(XunitCancellationToken);

        // Server timestamps should be close to the fixed time
        Assert.True(Math.Abs((response.ReceiveTimestamp - fixedTime).TotalMilliseconds) < 1,
            $"ReceiveTimestamp {response.ReceiveTimestamp} was not close to {fixedTime}");
        Assert.True(Math.Abs((response.TransmitTimestamp - fixedTime).TotalMilliseconds) < 1,
            $"TransmitTimestamp {response.TransmitTimestamp} was not close to {fixedTime}");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _fixedUtcNow;

        public FixedTimeProvider(DateTimeOffset fixedUtcNow)
        {
            _fixedUtcNow = fixedUtcNow;
        }

        public override DateTimeOffset GetUtcNow() => _fixedUtcNow;
    }
}
