using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Meziantou.Framework.Ntp.Tests;

public sealed class NtpServerTests : IAsyncLifetime
{
    private const int PacketSize = 48;
    private const int OriginateTimestampOffset = 24;
    private const int TransmitTimestampOffset = 40;

    private NtpServer _server = null!;

    public async ValueTask InitializeAsync()
    {
        _server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await _server.StartAsync(XunitCancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _server.Dispose();
        return ValueTask.CompletedTask;
    }

    private static NtpClient CreateClient(NtpServer server, Action<NtpClientOptions>? configure = null)
    {
        var options = new NtpClientOptions { Port = server.Port, Timeout = TimeSpan.FromSeconds(2) };
        configure?.Invoke(options);

        return new NtpClient("127.0.0.1", options);
    }

    /// <summary>Builds a raw client request so a test can drive the server without going through <see cref="NtpClient"/>.</summary>
    private static byte[] BuildRequest(int version = 4, int mode = 3)
    {
        var buffer = new byte[PacketSize];
        buffer[0] = (byte)((version << 3) | mode);

        // A recognizable transmit timestamp, so the echo into the originate field can be checked exactly.
        for (var i = 0; i < 8; i++)
        {
            buffer[TransmitTimestampOffset + i] = (byte)(0xA0 + i);
        }

        return buffer;
    }

    private static async Task<byte[]?> SendRawAsync(NtpServer server, byte[] request, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        peer.Connect(new IPEndPoint(IPAddress.Loopback, server.Port));
        await peer.SendAsync(request, cancellationToken);

        try
        {
            var result = await peer.ReceiveAsync(cancellationToken).AsTask().WaitAsync(timeout ?? TimeSpan.FromSeconds(2), cancellationToken);
            return result.Buffer;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    [Fact]
    public async Task Query_ReturnsValidResponse()
    {
        var response = await CreateClient(_server).QueryAsync(XunitCancellationToken);

        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
        Assert.True(response.ReceiveTimestamp > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Query_V4_MirrorsVersion()
    {
        var response = await CreateClient(_server, options => options.Version = NtpVersion.V4).QueryAsync(XunitCancellationToken);

        Assert.Equal(NtpVersion.V4, response.Version);
    }

    [Fact]
    public async Task Query_V3_MirrorsVersion()
    {
        var response = await CreateClient(_server, options => options.Version = NtpVersion.V3).QueryAsync(XunitCancellationToken);

        Assert.Equal(NtpVersion.V3, response.Version);
    }

    [Fact]
    public async Task Query_ClockOffset_IsSmall()
    {
        var response = await CreateClient(_server).QueryAsync(XunitCancellationToken);

        // Offset to a local server should be very small
        Assert.True(Math.Abs(response.ClockOffset.TotalSeconds) < 1, $"Clock offset was {response.ClockOffset}");
    }

    [Fact]
    public async Task Query_RoundTripDelay_IsReasonable()
    {
        var response = await CreateClient(_server).QueryAsync(XunitCancellationToken);

        // Localhost should stay reasonably low, but CI runners can be noisy
        Assert.True(response.RoundTripDelay >= TimeSpan.Zero);
        Assert.True(response.RoundTripDelay < TimeSpan.FromSeconds(5), $"Round-trip delay was {response.RoundTripDelay}");
    }

    [Fact]
    public async Task Query_MultipleConcurrentClients()
    {
        var tasks = Enumerable.Range(0, 10).Select(async _ =>
        {
            var client = CreateClient(_server);
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
        using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback, Stratum = 5 });
        await server.StartAsync(XunitCancellationToken);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.Equal(5, response.Stratum);
    }

    [Fact]
    public async Task Query_DefaultsToStratum2()
    {
        // A server answering from a TimeProvider has no attached reference clock, so it must not
        // claim stratum 1.
        var response = await CreateClient(_server).QueryAsync(XunitCancellationToken);

        Assert.Equal(2, response.Stratum);
    }

    [Fact]
    public async Task Query_AdvertisesReferenceIdentifierAndRootDispersion()
    {
        var response = await CreateClient(_server).QueryAsync(XunitCancellationToken);

        Assert.Equal("LOCL", response.ReferenceIdentifierText);

        // The NTP short format is 16.16 fixed point, so the value round-trips to within ~15 microseconds.
        Assert.True(Math.Abs((response.RootDispersion - TimeSpan.FromMilliseconds(100)).TotalMilliseconds) < 1, $"RootDispersion was {response.RootDispersion}");
        Assert.Equal(NtpLeapIndicator.NoWarning, response.LeapIndicator);
    }

    [Fact]
    public async Task Query_CustomReferenceIdentifierAndRootDispersion()
    {
        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            Stratum = 1,
            ReferenceIdentifier = "GPS",
            RootDispersion = TimeSpan.FromMilliseconds(5),
        });
        await server.StartAsync(XunitCancellationToken);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.Equal("GPS", response.ReferenceIdentifierText);
        Assert.True(Math.Abs((response.RootDispersion - TimeSpan.FromMilliseconds(5)).TotalMilliseconds) < 1, $"RootDispersion was {response.RootDispersion}");
    }

    [Fact]
    public async Task Query_OriginateTimestamp_EchoesClientTransmitTimestampExactly()
    {
        var request = BuildRequest();
        var response = await SendRawAsync(_server, request, XunitCancellationToken);

        Assert.NotNull(response);
        Assert.Equal(
            request.AsSpan(TransmitTimestampOffset, 8).ToArray(),
            response.AsSpan(OriginateTimestampOffset, 8).ToArray());
    }

    [Fact]
    public async Task Server_DoesNotAnswerServerModePackets()
    {
        // Answering a mode 4 packet is what lets two servers pointed at each other loop forever.
        var response = await SendRawAsync(_server, BuildRequest(mode: 4), XunitCancellationToken, TimeSpan.FromMilliseconds(500));

        Assert.Null(response);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public async Task Server_DoesNotAnswerNonClientModes(int mode)
    {
        var response = await SendRawAsync(_server, BuildRequest(mode: mode), XunitCancellationToken, TimeSpan.FromMilliseconds(500));

        Assert.Null(response);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public async Task Server_DoesNotAnswerUnsupportedVersions(int version)
    {
        var response = await SendRawAsync(_server, BuildRequest(version: version), XunitCancellationToken, TimeSpan.FromMilliseconds(500));

        Assert.Null(response);
    }

    [Fact]
    public async Task Server_IgnoresTruncatedPackets()
    {
        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        peer.Connect(new IPEndPoint(IPAddress.Loopback, _server.Port));
        await peer.SendAsync(new byte[10], XunitCancellationToken);

        await Assert.ThrowsAsync<TimeoutException>(() => peer.ReceiveAsync(XunitCancellationToken).AsTask().WaitAsync(TimeSpan.FromMilliseconds(500), XunitCancellationToken));

        // The listener must still be running afterwards.
        var response = await CreateClient(_server).QueryAsync(XunitCancellationToken);
        Assert.True(response.Stratum > 0);
    }

    [Fact]
    public async Task Server_RateLimitsAndAnswersOneKissOfDeath()
    {
        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            MaxRequestsPerSecond = 3,
        });
        await server.StartAsync(XunitCancellationToken);

        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        peer.Connect(new IPEndPoint(IPAddress.Loopback, server.Port));

        for (var i = 0; i < 12; i++)
        {
            await peer.SendAsync(BuildRequest(), XunitCancellationToken);
        }

        var normalReplies = 0;
        var kissOfDeathReplies = 0;
        while (true)
        {
            byte[] buffer;
            try
            {
                var result = await peer.ReceiveAsync(XunitCancellationToken).AsTask().WaitAsync(TimeSpan.FromMilliseconds(500), XunitCancellationToken);
                buffer = result.Buffer;
            }
            catch (TimeoutException)
            {
                break;
            }

            if (buffer[1] is 0)
            {
                kissOfDeathReplies++;
                Assert.Equal(0x52415445u, BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(12, 4))); // "RATE"
            }
            else
            {
                normalReplies++;
            }
        }

        Assert.Equal(3, normalReplies);

        // At most one Kiss-o'-Death per window, so that replying to a throttled source cannot itself
        // be used to reflect traffic.
        Assert.Equal(1, kissOfDeathReplies);
    }

    [Fact]
    public async Task Server_RateLimitingDisabled_AnswersEverything()
    {
        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            MaxRequestsPerSecond = 0,
        });
        await server.StartAsync(XunitCancellationToken);

        using var peer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        peer.Connect(new IPEndPoint(IPAddress.Loopback, server.Port));

        const int RequestCount = 20;
        for (var i = 0; i < RequestCount; i++)
        {
            await peer.SendAsync(BuildRequest(), XunitCancellationToken);
        }

        var replies = 0;
        while (replies < RequestCount)
        {
            try
            {
                await peer.ReceiveAsync(XunitCancellationToken).AsTask().WaitAsync(TimeSpan.FromMilliseconds(500), XunitCancellationToken);
                replies++;
            }
            catch (TimeoutException)
            {
                break;
            }
        }

        Assert.Equal(RequestCount, replies);
    }

    [Fact]
    public async Task StartAsync_CalledTwiceThrows()
    {
        using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync(XunitCancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => server.StartAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task StartAsync_AfterDisposeThrows()
    {
        var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync(XunitCancellationToken);
        server.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.StartAsync(XunitCancellationToken));
    }

    [Fact]
    public void Completion_BeforeStartThrows()
    {
        using var server = new NtpServer(new NtpServerOptions { Port = 0 });

        Assert.Throws<InvalidOperationException>(() => { _ = server.Completion; });
    }

    [Fact]
    public async Task Completion_CompletesWhenDisposed()
    {
        var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync(XunitCancellationToken);
        var completion = server.Completion;

        Assert.False(completion.IsCompleted);

        server.Dispose();

        await completion.WaitAsync(TimeSpan.FromSeconds(5), XunitCancellationToken);
        Assert.True(completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Completion_CompletesWhenTheStartTokenIsCancelled()
    {
        using var cts = new CancellationTokenSource();
        using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync(cts.Token);

        await cts.CancelAsync();

        await server.Completion.WaitAsync(TimeSpan.FromSeconds(5), XunitCancellationToken);
    }

    [Fact]
    public async Task Dispose_StopsListening()
    {
        var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync(XunitCancellationToken);
        var port = server.Port;
        server.Dispose();

        var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = port, Timeout = TimeSpan.FromSeconds(1) });

        await Assert.ThrowsAnyAsync<Exception>(() => client.QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        server.Dispose();
        server.Dispose();
    }

    [Fact]
    public async Task Query_WithCustomTimeProvider()
    {
        var fixedTime = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedTime);

        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            TimeProvider = timeProvider,
        });
        await server.StartAsync(XunitCancellationToken);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        // Server timestamps should be close to the fixed time
        Assert.True(Math.Abs((response.ReceiveTimestamp - fixedTime).TotalMilliseconds) < 1,
            $"ReceiveTimestamp {response.ReceiveTimestamp} was not close to {fixedTime}");
        Assert.True(Math.Abs((response.TransmitTimestamp - fixedTime).TotalMilliseconds) < 1,
            $"TransmitTimestamp {response.TransmitTimestamp} was not close to {fixedTime}");
    }

    [Fact]
    public async Task Query_WithTimeProviderAfterThe2036EraRollover()
    {
        var fixedTime = new DateTimeOffset(2060, 1, 2, 3, 4, 5, TimeSpan.Zero);

        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            TimeProvider = new FixedTimeProvider(fixedTime),
        });
        await server.StartAsync(XunitCancellationToken);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.True(Math.Abs((response.TransmitTimestamp - fixedTime).TotalMilliseconds) < 1,
            $"TransmitTimestamp {response.TransmitTimestamp} was not close to {fixedTime}");
    }

    [Fact]
    public async Task Query_WithTimeProviderOutsideTheRepresentableRange_DoesNotAnswerAndKeepsRunning()
    {
        // 1850 predates the NTP epoch. The old encoder silently wrapped it to a plausible-looking
        // 20th century date; it must now refuse rather than serve a wrong time.
        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            TimeProvider = new FixedTimeProvider(new DateTimeOffset(1850, 1, 1, 0, 0, 0, TimeSpan.Zero)),
        });
        await server.StartAsync(XunitCancellationToken);

        var response = await SendRawAsync(server, BuildRequest(), XunitCancellationToken, TimeSpan.FromMilliseconds(500));

        Assert.Null(response);
        Assert.False(server.Completion.IsCompleted);
    }

    [Fact]
    public async Task Server_BindsToTheConfiguredAddress()
    {
        using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.IPv6Loopback });
        await server.StartAsync(XunitCancellationToken);

        var client = new NtpClient("::1", new NtpClientOptions { Port = server.Port, Timeout = TimeSpan.FromSeconds(2) });
        var response = await client.QueryAsync(XunitCancellationToken);

        Assert.True(response.Stratum > 0);
    }

    [Fact]
    public void Constructor_InvalidReferenceIdentifierThrows()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NtpServer(new NtpServerOptions { ReferenceIdentifier = "TOOLONG" }));
        Assert.Throws<ArgumentException>(() => new NtpServer(new NtpServerOptions { ReferenceIdentifier = "é" }));
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
