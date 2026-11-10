using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Time.Testing;

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

    [Fact]
    public async Task ReadmeExample_Start()
    {
        #region NtpServer_Start
        // Start an NTP server on a random port, serving every network interface
        using var server = new NtpServer(new NtpServerOptions { Port = 0 });
        await server.StartAsync();

        Assert.NotEqual(0, server.Port); // The port the operating system assigned
        #endregion

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);
        Assert.Equal(2, response.Stratum);
    }

    [Fact]
    public async Task ReadmeExample_Loopback()
    {
        #region NtpServer_Loopback
        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
        });
        await server.StartAsync();
        #endregion

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);
        Assert.Equal(2, response.Stratum);
    }

    [Fact]
    public async Task ReadmeExample_CustomTimeProvider()
    {
        #region NtpServer_CustomTimeProvider
        var myCustomTimeProvider = new FakeTimeProvider(new DateTimeOffset(2024, 01, 01, 00, 00, 00, TimeSpan.Zero));

        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            TimeProvider = myCustomTimeProvider,
            Stratum = 2,
        });
        await server.StartAsync();
        #endregion

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);
        Assert.Equal(2, response.Stratum);
        Assert.Equal(new DateTimeOffset(2024, 01, 01, 00, 00, 00, TimeSpan.Zero), response.TransmitTimestamp, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ReadmeExample_Completion()
    {
        #region NtpServer_Completion
        using var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        using var server = new NtpServer(new NtpServerOptions { Port = 0 });
        await server.StartAsync(cancellationToken);

        await cts.CancelAsync();
        await server.Completion; // completes on Dispose or cancellation, faults on an unrecoverable error
        #endregion
    }

    [Fact]
    public async Task Documentation_QueryLocalServer()
    {
        #region NtpClient_GettingStarted
        using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync();

        var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port });
        var response = await client.QueryAsync();

        // The difference between the clock of the server and the local clock
        var localTimeCorrected = DateTimeOffset.UtcNow + response.ClockOffset;

        Assert.Equal(NtpVersion.V4, response.Version);
        Assert.Equal(2, response.Stratum);
        Assert.True(response.RoundTripDelay >= TimeSpan.Zero);
        #endregion

        Assert.True(Math.Abs((localTimeCorrected - DateTimeOffset.UtcNow).TotalSeconds) < 60);
    }

    [Fact]
    public async Task Documentation_ClockOffset()
    {
        #region NtpClient_ClockOffset
        // A server whose clock is one hour ahead of the local clock
        var serverClock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1));
        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            TimeProvider = serverClock,
        });
        await server.StartAsync();

        var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port });
        var response = await client.QueryAsync();

        Assert.InRange(response.ClockOffset, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(61));
        Assert.Equal(serverClock.GetUtcNow(), response.TransmitTimestamp, TimeSpan.FromSeconds(1));
        #endregion
    }

    [Fact]
    public async Task Documentation_ClientOptions()
    {
        using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync(XunitCancellationToken);
        var port = server.Port;

        #region NtpClient_Options
        var client = new NtpClient("127.0.0.1", new NtpClientOptions
        {
            Port = port,                         // 123 by default
            Version = NtpVersion.V3,             // V4 by default
            Timeout = TimeSpan.FromSeconds(10),  // per resolved address, 5 seconds by default
        });

        // Bounds the total duration, whatever the number of addresses the host name resolves to
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var response = await client.QueryAsync(cts.Token);

        // The server answers with the version of the request
        Assert.Equal(NtpVersion.V3, response.Version);
        #endregion
    }

    [Fact]
    public async Task Documentation_ServerOptions()
    {
        #region NtpServer_Options
        using var server = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,

            // Only when the machine has a reference clock attached, such as a GPS receiver
            Stratum = 1,
            ReferenceIdentifier = "GPS",
            RootDispersion = TimeSpan.FromMilliseconds(5),

            MaxRequestsPerSecond = 10, // per source address, 0 to disable
        });
        await server.StartAsync();

        var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port });
        var response = await client.QueryAsync();

        Assert.Equal(1, response.Stratum);
        Assert.Equal("GPS", response.ReferenceIdentifierText);
        #endregion

        Assert.True(Math.Abs((response.RootDispersion - TimeSpan.FromMilliseconds(5)).TotalMilliseconds) < 1);
    }

    [Fact]
    public async Task Documentation_Timeout()
    {
        // A socket that receives the query and never answers
        using var silentServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)silentServer.Client.LocalEndPoint!).Port;

        #region NtpClient_Timeout
        var client = new NtpClient("127.0.0.1", new NtpClientOptions
        {
            Port = port,
            Timeout = TimeSpan.FromSeconds(1),
        });

        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync());
        #endregion
    }

    [Fact]
    public async Task Documentation_Retry()
    {
        using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync(XunitCancellationToken);
        var port = server.Port;

        #region NtpClient_Retry
        var client = new NtpClient("127.0.0.1", new NtpClientOptions
        {
            Port = port,
            Timeout = TimeSpan.FromSeconds(2),
            RetryCount = 2, // up to 3 requests per address, 2 seconds each
        });

        var response = await client.QueryAsync();
        #endregion

        Assert.Equal(2, response.Stratum);
    }

    [Fact]
    public async Task Documentation_MeasureServer()
    {
        using var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        await server.StartAsync(XunitCancellationToken);
        var port = server.Port;

        #region NtpClient_Measure
        var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = port });

        // 4 queries, 2 seconds apart, by default
        var measurement = await client.MeasureAsync(new NtpMeasurementOptions
        {
            SampleCount = 4,
            SampleInterval = TimeSpan.FromMilliseconds(100),
        });

        // The offset of the sample with the shortest round-trip delay
        var offset = measurement.ClockOffset;

        // The maximum error of the offset, and how much the samples disagree
        var maximumError = measurement.RootDistance;
        var jitter = measurement.Jitter;

        Assert.Equal(4, measurement.Samples.Count + measurement.Errors.Count);
        #endregion

        Assert.True(Math.Abs(offset.TotalSeconds) < 1, $"Offset was {offset}");
        Assert.True(maximumError > TimeSpan.Zero);
        Assert.True(jitter >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Documentation_MeasureServers()
    {
        #region NtpClient_MeasureServers
        // Three servers, one of which is one hour ahead
        using var server1 = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        using var server2 = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        using var wrongServer = new NtpServer(new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            TimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1)),
        });
        await server1.StartAsync();
        await server2.StartAsync();
        await wrongServer.StartAsync();

        NtpClient[] clients =
        [
            new NtpClient("127.0.0.1", new NtpClientOptions { Port = server1.Port }),
            new NtpClient("127.0.0.1", new NtpClientOptions { Port = server2.Port }),
            new NtpClient("127.0.0.1", new NtpClientOptions { Port = wrongServer.Port }),
        ];
        var measurement = await NtpClient.MeasureAsync(clients, new NtpMeasurementOptions { SampleCount = 2, SampleInterval = TimeSpan.Zero });

        // The server that is one hour ahead is outvoted
        Assert.Equal(2, measurement.SelectedServers.Count);
        Assert.Same(clients[2], Assert.Single(measurement.RejectedServers).Client);

        // The offset to add to the local clock, and its bounds
        Assert.InRange(measurement.ClockOffset, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1));
        Assert.InRange(measurement.ClockOffset, measurement.MinimumClockOffset, measurement.MaximumClockOffset);
        #endregion
    }

    [Fact]
    public async Task Documentation_Authentication()
    {
        #region NtpServer_Authentication
        // The same key on both sides: a line "1 AES128CMAC 000102030405060708090a0b0c0d0e0f" of an ntpd or chrony keys file
        var key = new NtpAuthenticationKey(
            keyId: 1,
            NtpAuthenticationAlgorithm.AesCmac,
            Convert.FromHexString("000102030405060708090a0b0c0d0e0f"));

        var serverOptions = new NtpServerOptions
        {
            Port = 0,
            BindAddress = IPAddress.Loopback,
            RequireAuthentication = true, // ignore the requests without a valid MAC
        };
        serverOptions.AuthenticationKeys.Add(key);

        using var server = new NtpServer(serverOptions);
        await server.StartAsync();

        var client = new NtpClient("127.0.0.1", new NtpClientOptions
        {
            Port = server.Port,
            AuthenticationKey = key, // ignore the replies without a valid MAC
        });
        var response = await client.QueryAsync();
        #endregion

        Assert.Equal(2, response.Stratum);
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

    private static byte[] BuildAuthenticatedRequest(NtpAuthenticationKey key)
    {
        var buffer = new byte[PacketSize + NtpMac.GetMacSize(key)];
        BuildRequest().CopyTo(buffer, 0);
        NtpMac.Write(key, buffer, PacketSize);
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
    public void RateLimiter_CollidingAddressesShareTheBucketBudget()
    {
        var (first, second) = FindAddressesInTheSameBucket();
        var limiter = new NtpRateLimiter(maxRequestsPerWindow: 1, new FakeTimeProvider());

        Assert.True(limiter.TryAcquire(first, out _));

        // Rotating through addresses that land in the same bucket must not restart the bucket window:
        // the budget is spent for the window, whichever colliding address spent it.
        for (var i = 0; i < 100; i++)
        {
            Assert.False(limiter.TryAcquire(second, out _));
            Assert.False(limiter.TryAcquire(first, out _));
        }
    }

    [Fact]
    public void RateLimiter_CollidingAddressesShareTheKissOfDeathBudget()
    {
        var (first, second) = FindAddressesInTheSameBucket();
        var limiter = new NtpRateLimiter(maxRequestsPerWindow: 1, new FakeTimeProvider());

        Assert.True(limiter.TryAcquire(first, out var isFirstRejection));
        Assert.False(isFirstRejection);

        Assert.False(limiter.TryAcquire(second, out isFirstRejection));
        Assert.True(isFirstRejection);

        Assert.False(limiter.TryAcquire(first, out isFirstRejection));
        Assert.False(isFirstRejection);
    }

    [Fact]
    public void RateLimiter_AddressesInDistinctBucketsHaveTheirOwnBudget()
    {
        var (first, second) = FindAddressesInDistinctBuckets();
        var limiter = new NtpRateLimiter(maxRequestsPerWindow: 1, new FakeTimeProvider());

        Assert.True(limiter.TryAcquire(first, out _));
        Assert.False(limiter.TryAcquire(first, out _));

        Assert.True(limiter.TryAcquire(second, out _));
        Assert.False(limiter.TryAcquire(second, out _));
    }

    [Fact]
    public void RateLimiter_RejectedRequestsDoNotExtendTheWindow()
    {
        var timeProvider = new FakeTimeProvider();
        var address = IPAddress.Parse("192.0.2.1");
        var limiter = new NtpRateLimiter(maxRequestsPerWindow: 1, timeProvider);

        Assert.True(limiter.TryAcquire(address, out _));

        // A source that keeps sending while throttled must not push its own window forever.
        for (var i = 0; i < 10; i++)
        {
            timeProvider.Advance(TimeSpan.FromMilliseconds(90));
            Assert.False(limiter.TryAcquire(address, out _));
        }

        timeProvider.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(limiter.TryAcquire(address, out _));
    }

    /// <summary>Returns two distinct addresses that <see cref="NtpRateLimiter"/> maps onto the same bucket.</summary>
    private static (IPAddress First, IPAddress Second) FindAddressesInTheSameBucket()
    {
        var seen = new Dictionary<int, IPAddress>();
        foreach (var address in EnumerateAddresses())
        {
            if (seen.TryGetValue(GetBucketIndex(address), out var previous))
                return (previous, address);

            seen[GetBucketIndex(address)] = address;
        }

        throw new InvalidOperationException("The address space is larger than the bucket count, so a collision must exist");
    }

    private static (IPAddress First, IPAddress Second) FindAddressesInDistinctBuckets()
    {
        IPAddress? first = null;
        foreach (var address in EnumerateAddresses())
        {
            if (first is null)
            {
                first = address;
                continue;
            }

            if (GetBucketIndex(first) != GetBucketIndex(address))
                return (first, address);
        }

        throw new InvalidOperationException("The buckets cannot all hold the whole address space");
    }

    private static IEnumerable<IPAddress> EnumerateAddresses()
    {
        for (var high = 0; high <= byte.MaxValue; high++)
        {
            for (var low = 0; low <= byte.MaxValue; low++)
            {
                yield return new IPAddress([203, 0, (byte)high, (byte)low]);
            }
        }
    }

    /// <summary>Mirrors the bucket mapping of <see cref="NtpRateLimiter"/>.</summary>
    private static int GetBucketIndex(IPAddress address) => (int)((uint)address.GetHashCode() % 1024);

    [Theory]
    [InlineData(NtpAuthenticationAlgorithm.AesCmac, 16)]
    [InlineData(NtpAuthenticationAlgorithm.Sha1, 20)]
    [InlineData(NtpAuthenticationAlgorithm.Md5, 16)]
    public async Task Authentication_ClientAndServerAgree(NtpAuthenticationAlgorithm algorithm, int digestSize)
    {
        var key = new NtpAuthenticationKey(42, algorithm, Enumerable.Range(1, 16).Select(i => (byte)i).ToArray());
        using var server = await StartAuthenticatedServerAsync(requireAuthentication: true, key);

        var response = await CreateClient(server, options => options.AuthenticationKey = key).QueryAsync(XunitCancellationToken);
        Assert.Equal(2, response.Stratum);

        // The reply ends with the key identifier and the digest
        var request = BuildAuthenticatedRequest(key);
        var reply = await SendRawAsync(server, request, XunitCancellationToken);
        Assert.NotNull(reply);
        Assert.HasCount(PacketSize + 4 + digestSize, reply);
        Assert.Equal(42u, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(PacketSize)));
    }

    [Fact]
    public async Task Authentication_UnknownKeyGetsACryptoNak()
    {
        using var server = await StartAuthenticatedServerAsync(requireAuthentication: false, new NtpAuthenticationKey(1, NtpAuthenticationAlgorithm.Sha1, "server"u8));
        var clientKey = new NtpAuthenticationKey(2, NtpAuthenticationAlgorithm.Sha1, "client"u8);

        var reply = await SendRawAsync(server, BuildAuthenticatedRequest(clientKey), XunitCancellationToken);
        Assert.NotNull(reply);
        Assert.HasCount(PacketSize + 4, reply);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32BigEndian(reply.AsSpan(PacketSize)));
        Assert.True(reply.AsSpan(32, 16).IndexOfAnyExcept((byte)0) < 0, "A crypto-NAK must not carry the time");

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server, options =>
        {
            options.AuthenticationKey = clientKey;
            options.Timeout = TimeSpan.FromMilliseconds(500);
        }).QueryAsync(XunitCancellationToken));
        Assert.Contains("crypto-NAK", exception.Message);
    }

    [Fact]
    public async Task Authentication_InvalidMacGetsACryptoNak()
    {
        var key = new NtpAuthenticationKey(1, NtpAuthenticationAlgorithm.AesCmac, new byte[16]);
        using var server = await StartAuthenticatedServerAsync(requireAuthentication: false, key);

        var request = BuildAuthenticatedRequest(key);
        request[^1] ^= 0x01;
        var reply = await SendRawAsync(server, request, XunitCancellationToken);

        Assert.NotNull(reply);
        Assert.HasCount(PacketSize + 4, reply);
    }

    [Fact]
    public async Task Authentication_Required_IgnoresUnauthenticatedRequests()
    {
        using var server = await StartAuthenticatedServerAsync(requireAuthentication: true, new NtpAuthenticationKey(1, NtpAuthenticationAlgorithm.AesCmac, new byte[16]));

        var reply = await SendRawAsync(server, BuildRequest(), XunitCancellationToken, TimeSpan.FromMilliseconds(500));

        Assert.Null(reply);
    }

    [Fact]
    public async Task Authentication_Optional_AnswersUnauthenticatedRequestsWithoutAMac()
    {
        using var server = await StartAuthenticatedServerAsync(requireAuthentication: false, new NtpAuthenticationKey(1, NtpAuthenticationAlgorithm.AesCmac, new byte[16]));

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);
        var reply = await SendRawAsync(server, BuildRequest(), XunitCancellationToken);

        Assert.Equal(2, response.Stratum);
        Assert.NotNull(reply);
        Assert.HasCount(PacketSize, reply);
    }

    [Fact]
    public async Task Server_SkipsExtensionFields()
    {
        // An NTS request without a MAC: a 28-byte extension field after the header
        var request = new byte[PacketSize + 28];
        BuildRequest().CopyTo(request, 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(PacketSize), 0x0104);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(PacketSize + 2), 28);

        var reply = await SendRawAsync(_server, request, XunitCancellationToken);

        Assert.NotNull(reply);
        Assert.HasCount(PacketSize, reply);
    }

    [Fact]
    public async Task Server_IgnoresMalformedExtensionFields()
    {
        var request = new byte[PacketSize + 28];
        BuildRequest().CopyTo(request, 0);
        BinaryPrimitives.WriteUInt16BigEndian(request.AsSpan(PacketSize + 2), 30);

        var reply = await SendRawAsync(_server, request, XunitCancellationToken, TimeSpan.FromMilliseconds(500));

        Assert.Null(reply);
    }

    [Fact]
    public void Constructor_RejectsInvalidAuthenticationOptions()
    {
        var duplicates = new NtpServerOptions();
        duplicates.AuthenticationKeys.Add(new NtpAuthenticationKey(1, NtpAuthenticationAlgorithm.Md5, "a"u8));
        duplicates.AuthenticationKeys.Add(new NtpAuthenticationKey(1, NtpAuthenticationAlgorithm.Sha1, "b"u8));
        Assert.Throws<ArgumentException>(() => new NtpServer(duplicates));

        Assert.Throws<ArgumentException>(() => new NtpServer(new NtpServerOptions { RequireAuthentication = true }));
    }

    [Fact]
    public async Task MeasureServers_FailsWithoutAMajority()
    {
        using var server1 = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
        using var server2 = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback, TimeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow.AddHours(1)) });
        await server1.StartAsync(XunitCancellationToken);
        await server2.StartAsync(XunitCancellationToken);

        var exception = await Assert.ThrowsAsync<NtpClockSelectionException>(() => NtpClient.MeasureAsync(
            [CreateClient(server1), CreateClient(server2)],
            new NtpMeasurementOptions { SampleCount = 1 },
            XunitCancellationToken));

        Assert.HasCount(2, exception.Measurements);
        Assert.Empty(exception.FailedServers);
    }

    [Fact]
    public async Task MeasureServers_ReportsTheServersThatDoNotAnswer()
    {
        using var silentServer = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var silentPort = ((IPEndPoint)silentServer.Client.LocalEndPoint!).Port;

        var silentClient = new NtpClient("127.0.0.1", new NtpClientOptions { Port = silentPort, Timeout = TimeSpan.FromMilliseconds(300) });
        var measurement = await NtpClient.MeasureAsync(
            [CreateClient(_server), silentClient],
            new NtpMeasurementOptions { SampleCount = 1 },
            XunitCancellationToken);

        Assert.Single(measurement.SelectedServers);
        Assert.Empty(measurement.RejectedServers);
        Assert.IsType<TimeoutException>(measurement.FailedServers[silentClient]);
    }

    private static async Task<NtpServer> StartAuthenticatedServerAsync(bool requireAuthentication, params NtpAuthenticationKey[] keys)
    {
        var options = new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback, RequireAuthentication = requireAuthentication };
        foreach (var key in keys)
        {
            options.AuthenticationKeys.Add(key);
        }

        var server = new NtpServer(options);
        await server.StartAsync(XunitCancellationToken);
        return server;
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
        // Every test here binds an ephemeral port, so the port the server just released can be handed to
        // another server started in parallel -- in this process or in the sibling process running the other
        // target framework -- which then answers the query and the assertion never fires. Holding the port
        // with a socket that never replies keeps it out of that pool; the bind also fails if Dispose stopped
        // releasing the socket, which is the behavior under test, so only retry it a few times before letting
        // the failure through.
        for (var attempt = 1; ; attempt++)
        {
            var server = new NtpServer(new NtpServerOptions { Port = 0, BindAddress = IPAddress.Loopback });
            await server.StartAsync(XunitCancellationToken);
            var port = server.Port;
            server.Dispose();

            using var placeholder = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { ExclusiveAddressUse = true };
            try
            {
                placeholder.Bind(new IPEndPoint(IPAddress.Loopback, port));
            }
            catch (SocketException) when (attempt < 5)
            {
                // Somebody else won the race for the port before the placeholder could take it over
                continue;
            }

            var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = port, Timeout = TimeSpan.FromSeconds(1) });

            await Assert.ThrowsAnyAsync<Exception>(() => client.QueryAsync(XunitCancellationToken));
            return;
        }
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
