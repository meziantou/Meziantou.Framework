using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Meziantou.Xunit;

namespace Meziantou.Framework.Ntp.Tests;

public sealed class NtpClientTests(ITestOutputHelper testOutputHelper)
{
    private const int RetryCount = 3;
    private const int PacketSize = 48;
    private const int OriginateTimestampOffset = 24;
    private const int TransmitTimestampOffset = 40;

    private static readonly DateTimeOffset Era0 = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Era1 = Era0.AddSeconds(0x1_0000_0000L);

    private static NtpClientOptions CreateTestOptions(NtpVersion version = NtpVersion.V4, TimeSpan? timeout = null)
    {
        return new NtpClientOptions { Version = version, Timeout = timeout ?? TimeSpan.FromSeconds(2) };
    }

    private async Task<NtpResponse> QueryWithRetryAsync(string server, NtpVersion version = NtpVersion.V4, int retryCount = RetryCount, TimeSpan? timeout = null, TimeSpan? delayBetweenAttempts = null)
    {
        var delay = delayBetweenAttempts ?? TimeSpan.FromMilliseconds(50);
        for (var i = retryCount; i >= 0; i--)
        {
            try
            {
                var client = new NtpClient(server, CreateTestOptions(version, timeout));
                return await client.QueryAsync(XunitCancellationToken);
            }
            catch (Exception ex) when (i > 0)
            {
                var attempt = retryCount - i + 1;
                testOutputHelper.WriteLine($"Attempt {attempt} for {server} failed: {ex.GetType().Name}: {ex.Message}");
                await Task.Delay(TimeSpan.FromMilliseconds(delay.TotalMilliseconds * attempt), XunitCancellationToken);
            }
        }

        throw new InvalidOperationException("unreachable");
    }

    private async Task<NtpResponse> QueryWithFallbackAsync(NtpVersion version = NtpVersion.V4)
    {
        string[] servers = ["time.google.com", "pool.ntp.org", "time.cloudflare.com"];
        await LogDnsResolutionAsync(servers);

        List<Exception> exceptions = [];

        foreach (var server in servers)
        {
            try
            {
                var response = await QueryWithRetryAsync(server, version);
                testOutputHelper.WriteLine($"Successfully queried {server}");
                return response;
            }
            catch (Exception ex)
            {
                testOutputHelper.WriteLine($"Server {server} failed after retries: {ex.GetType().Name}: {ex.Message}");
                exceptions.Add(ex);
            }
        }

        throw new AggregateException("All NTP servers are unreachable", exceptions);
    }

    private async Task LogDnsResolutionAsync(string[] servers)
    {
        foreach (var server in servers)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(server, XunitCancellationToken);
                var formatted = string.Join(", ", addresses.Select(a => $"{a} ({a.AddressFamily})"));
                testOutputHelper.WriteLine($"DNS resolution for {server}: [{formatted}]");
            }
            catch (Exception ex)
            {
                testOutputHelper.WriteLine($"DNS resolution for {server} failed: {ex.Message}");
            }
        }
    }

    private static NtpClient CreateClient(FakeNtpServer server, Action<NtpClientOptions>? configure = null)
    {
        var options = new NtpClientOptions { Port = server.Port, Timeout = TimeSpan.FromMilliseconds(500) };
        configure?.Invoke(options);

        return new NtpClient("127.0.0.1", options);
    }

    private static void WriteTimestamp(DateTimeOffset? value, Span<byte> destination)
    {
        if (value is not { } instant)
        {
            destination[..8].Clear();
            return;
        }

        var epoch = instant < Era1 ? Era0 : Era1;
        var ticks = (instant - epoch).Ticks;

        BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)(ticks / TimeSpan.TicksPerSecond));
        BinaryPrimitives.WriteUInt32BigEndian(destination[4..], (uint)(ticks % TimeSpan.TicksPerSecond * 0x1_0000_0000L / TimeSpan.TicksPerSecond));
    }

    private static void WriteShortFormat(TimeSpan value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)(value.Ticks * 0x1_0000L / TimeSpan.TicksPerSecond));
    }

    /// <summary>
    /// Builds a server reply, deliberately using its own encoder rather than the library's so that a
    /// bug in the shared codec cannot hide by being applied symmetrically on both sides of the wire.
    /// </summary>
    private static byte[] BuildResponse(
        byte[] request,
        DateTimeOffset? serverTime = null,
        byte stratum = 2,
        int version = 4,
        int mode = 4,
        int leapIndicator = 0,
        bool echoOriginate = true,
        uint referenceIdentifier = 0,
        TimeSpan rootDelay = default,
        TimeSpan rootDispersion = default,
        DateTimeOffset? referenceTimestamp = null)
    {
        var now = serverTime ?? DateTimeOffset.UtcNow;
        var buffer = new byte[PacketSize];

        buffer[0] = (byte)((leapIndicator << 6) | (version << 3) | mode);
        buffer[1] = stratum;
        buffer[2] = request[2];
        buffer[3] = unchecked((byte)-20);
        WriteShortFormat(rootDelay, buffer.AsSpan(4, 4));
        WriteShortFormat(rootDispersion, buffer.AsSpan(8, 4));
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(12, 4), referenceIdentifier);
        WriteTimestamp(referenceTimestamp, buffer.AsSpan(16, 8));

        if (echoOriginate)
        {
            request.AsSpan(TransmitTimestampOffset, 8).CopyTo(buffer.AsSpan(OriginateTimestampOffset, 8));
        }
        else
        {
            // A well-formed timestamp that simply is not the one we sent, so the echo check is what
            // rejects this and not the "timestamp is missing" check.
            WriteTimestamp(now, buffer.AsSpan(OriginateTimestampOffset, 8));
        }

        WriteTimestamp(now, buffer.AsSpan(32, 8));
        WriteTimestamp(now, buffer.AsSpan(TransmitTimestampOffset, 8));

        return buffer;
    }

    [Fact]
    public async Task Query_ComputesClockOffsetFromServerTime()
    {
        var skew = TimeSpan.FromHours(1);
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, DateTimeOffset.UtcNow + skew)]);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.Equal(2, response.Stratum);
        Assert.Equal(NtpVersion.V4, response.Version);
        Assert.True(Math.Abs((response.ClockOffset - skew).TotalSeconds) < 1, $"Clock offset was {response.ClockOffset}, expected about {skew}");
        Assert.True(response.RoundTripDelay >= TimeSpan.Zero && response.RoundTripDelay < TimeSpan.FromSeconds(1), $"Round-trip delay was {response.RoundTripDelay}");
    }

    [Fact]
    public async Task Query_ExposesRootDelayDispersionAndReferenceIdentifier()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(
            request,
            stratum: 1,
            referenceIdentifier: 0x47505300, // "GPS"
            rootDelay: TimeSpan.FromMilliseconds(250),
            rootDispersion: TimeSpan.FromMilliseconds(50))]);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.Equal("GPS", response.ReferenceIdentifierText);
        Assert.False(response.IsKissOfDeath);
        Assert.Null(response.KissCode);
        Assert.True(Math.Abs((response.RootDelay - TimeSpan.FromMilliseconds(250)).TotalMilliseconds) < 1, $"RootDelay was {response.RootDelay}");
        Assert.True(Math.Abs((response.RootDispersion - TimeSpan.FromMilliseconds(50)).TotalMilliseconds) < 1, $"RootDispersion was {response.RootDispersion}");
    }

    [Fact]
    public async Task Query_ReferenceTimestamp_IsNullWhenTheServerLeavesItUnset()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, referenceTimestamp: null)]);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.Null(response.ReferenceTimestamp);
    }

    [Fact]
    public async Task Query_ReferenceTimestamp_IsReturnedWhenTheServerSetsIt()
    {
        var reference = new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero);
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, referenceTimestamp: reference)]);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.NotNull(response.ReferenceTimestamp);
        Assert.True(Math.Abs((response.ReferenceTimestamp.Value - reference).TotalMilliseconds) < 1, $"ReferenceTimestamp was {response.ReferenceTimestamp}");
    }

    [Fact]
    public async Task Query_DecodesTimestampsAfterThe2036EraRollover()
    {
        // The 32-bit seconds field wraps on 2036-02-07; era 1 timestamps must not decode back to 1900.
        var era1Time = new DateTimeOffset(2050, 3, 1, 0, 0, 0, TimeSpan.Zero);
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, referenceTimestamp: era1Time)]);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.NotNull(response.ReferenceTimestamp);
        Assert.Equal(era1Time, response.ReferenceTimestamp.Value);
    }

    [Fact]
    public async Task Query_RejectsResponseFromAnotherSourceEndpoint()
    {
        // The reply is well-formed and echoes the request, but comes from a different socket. The
        // client's socket is connected, so the operating system must drop it.
        using var server = FakeNtpServer.Start(request => [BuildResponse(request)], replyFromDifferentSocket: true);

        await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Query_RejectsResponseThatDoesNotEchoTheTransmitTimestamp()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, echoOriginate: false)]);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
        Assert.Contains("originate timestamp", exception.Message);
    }

    [Fact]
    public async Task Query_RejectsResponseWithAForgedOriginateTimestamp()
    {
        // A plausible but wrong originate timestamp: close enough that a lossy comparison might accept it.
        using var server = FakeNtpServer.Start(request =>
        {
            var response = BuildResponse(request);
            response[OriginateTimestampOffset + 7] ^= 0x01;
            return [response];
        });

        await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Query_RejectsKissOfDeathPacket()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, stratum: 0, referenceIdentifier: 0x52415445)]);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
        Assert.Contains("RATE", exception.Message);
    }

    [Fact]
    public async Task Query_RejectsServerReportingAnAlarmCondition()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, leapIndicator: 3)]);

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
        Assert.Contains("alarm condition", exception.Message);
    }

    [Fact]
    public async Task Query_RejectsResponseThatIsNotInServerMode()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, mode: 3)]);

        await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Query_RejectsResponseWithAnUndefinedVersion()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, version: 7)]);

        await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Query_RejectsTruncatedPacket()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(request)[..20]]);

        await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Query_RejectsResponseMissingTheTransmitTimestamp()
    {
        using var server = FakeNtpServer.Start(request =>
        {
            var response = BuildResponse(request);
            response.AsSpan(TransmitTimestampOffset, 8).Clear();
            return [response];
        });

        await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Query_IgnoresInvalidPacketsAndAcceptsTheValidOne()
    {
        // A single forged or stray datagram must not deny service for the whole timeout.
        using var server = FakeNtpServer.Start(request =>
        [
            BuildResponse(request, echoOriginate: false),
            BuildResponse(request, stratum: 0),
            BuildResponse(request)[..10],
            BuildResponse(request, stratum: 4),
        ]);

        var response = await CreateClient(server).QueryAsync(XunitCancellationToken);

        Assert.Equal(4, response.Stratum);
    }

    [Fact]
    public async Task Query_ValidationDisabled_ReturnsKissOfDeathPacket()
    {
        using var server = FakeNtpServer.Start(request => [BuildResponse(request, stratum: 0, referenceIdentifier: 0x44454E59)]);

        var response = await CreateClient(server, options => options.ValidateResponse = false).QueryAsync(XunitCancellationToken);

        Assert.True(response.IsKissOfDeath);
        Assert.Equal("DENY", response.KissCode);
    }

    [Fact]
    public async Task Query_ValidationDisabled_StillRejectsAPacketWithNoTimestamps()
    {
        // Disabling validation opts out of trusting the peer, not out of arithmetic: a response with no
        // timestamps cannot produce an offset, and must never yield one measured from the year 1.
        using var server = FakeNtpServer.Start(request =>
        {
            var response = BuildResponse(request);
            response.AsSpan(OriginateTimestampOffset, 8).Clear();
            return [response];
        });

        await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server, options => options.ValidateResponse = false).QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Query_TimeoutThrowsTimeoutException()
    {
        using var server = FakeNtpServer.Blackhole();

        var exception = await Assert.ThrowsAsync<TimeoutException>(() => CreateClient(server).QueryAsync(XunitCancellationToken));
        Assert.Contains("did not complete within", exception.Message);
    }

    [Fact]
    public async Task Query_CallerCancellationThrowsOperationCanceledException()
    {
        using var server = FakeNtpServer.Blackhole();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var client = new NtpClient("127.0.0.1", new NtpClientOptions { Port = server.Port, Timeout = TimeSpan.FromSeconds(30) });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.QueryAsync(cts.Token));
    }

    [Fact]
    public async Task Query_EachResolvedAddressGetsItsOwnTimeout()
    {
        // "localhost" resolves to both ::1 and 127.0.0.1 on most machines. Both are black holes here, so
        // the query must spend the timeout on each rather than letting the first address consume it all.
        using var blackholes = DualStackBlackhole.Start();

        var addresses = await Dns.GetHostAddressesAsync("localhost", XunitCancellationToken);
        if (addresses.Length < 2)
        {
            global::Xunit.Assert.Skip($"'localhost' resolved to {addresses.Length} address(es), need 2 to test per-address timeouts");
        }

        var timeout = TimeSpan.FromMilliseconds(400);
        var client = new NtpClient("localhost", new NtpClientOptions { Port = blackholes.Port, Timeout = timeout });

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(() => client.QueryAsync(XunitCancellationToken));
        stopwatch.Stop();

        testOutputHelper.WriteLine($"{addresses.Length} addresses, {timeout.TotalMilliseconds}ms each, elapsed {stopwatch.Elapsed.TotalMilliseconds:N0}ms");
        Assert.True(stopwatch.Elapsed >= timeout * 1.5, $"Elapsed {stopwatch.Elapsed} suggests the addresses shared a single timeout budget");
    }

    [Fact]
    public async Task Query_UnresolvableHostThrows()
    {
        var client = new NtpClient("nonexistent.invalid", new NtpClientOptions { Timeout = TimeSpan.FromSeconds(5) });

        await Assert.ThrowsAnyAsync<Exception>(() => client.QueryAsync(XunitCancellationToken));
    }

    [Fact]
    public void Constructor_NullServerThrows()
    {
        Assert.Throws<ArgumentNullException>(() => new NtpClient(server: null!));
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipIf(TestOperatingSystems.MacOS, ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public async Task Query_V4_ReturnsValidResponse()
    {
        var response = await QueryWithFallbackAsync(NtpVersion.V4);

        Assert.Equal(NtpVersion.V4, response.Version);
        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
        Assert.True(response.ReceiveTimestamp > DateTimeOffset.UnixEpoch);
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipIf(TestOperatingSystems.MacOS, ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public async Task Query_V3_ReturnsValidResponse()
    {
        var response = await QueryWithFallbackAsync(NtpVersion.V3);

        Assert.Equal(NtpVersion.V3, response.Version);
        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipIf(TestOperatingSystems.MacOS, ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public async Task Query_ClockOffset_IsReasonable()
    {
        var response = await QueryWithFallbackAsync();

        // Clock offset should be within 1 minute for a properly synchronized machine
        Assert.True(Math.Abs(response.ClockOffset.TotalMinutes) < 1, $"Clock offset was {response.ClockOffset}");
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipIf(TestOperatingSystems.MacOS, ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public async Task Query_RoundTripDelay_IsPositive()
    {
        var response = await QueryWithFallbackAsync();

        Assert.True(response.RoundTripDelay >= TimeSpan.Zero, $"Round-trip delay was {response.RoundTripDelay}");
        // Round-trip delay should be less than 5 seconds for a normal network
        Assert.True(response.RoundTripDelay < TimeSpan.FromSeconds(5), $"Round-trip delay was {response.RoundTripDelay}");
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipIf(TestOperatingSystems.MacOS, ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public async Task Query_LeapIndicator_IsValid()
    {
        var response = await QueryWithFallbackAsync();

        Assert.True(Enum.IsDefined(response.LeapIndicator));
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipIf(TestOperatingSystems.MacOS, ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public async Task Query_ReferenceTimestamp_IsRecent()
    {
        var response = await QueryWithFallbackAsync();

        // Reference timestamp should be within the last 24 hours for an active server
        Assert.NotNull(response.ReferenceTimestamp);
        var age = DateTimeOffset.UtcNow - response.ReferenceTimestamp.Value;
        Assert.True(age < TimeSpan.FromDays(1), $"Reference timestamp age was {age}");
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipIf(TestOperatingSystems.MacOS, ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public async Task Query_TimeGoogle_ReturnsValidResponse()
    {
        await LogDnsResolutionAsync(["time.google.com"]);
        var response = await QueryWithRetryAsync("time.google.com");

        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipIf(TestOperatingSystems.MacOS, ContinuousIntegration = ContinuousIntegrationEnvironments.GitHubActions)]
    public async Task Query_PoolNtpOrg_ReturnsValidResponse()
    {
        await LogDnsResolutionAsync(["pool.ntp.org"]);
        var response = await QueryWithRetryAsync("pool.ntp.org", retryCount: 8, timeout: TimeSpan.FromSeconds(5), delayBetweenAttempts: TimeSpan.FromMilliseconds(200));

        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }

    /// <summary>A UDP server that answers with whatever bytes the test tells it to.</summary>
    private sealed class FakeNtpServer : IDisposable
    {
        private readonly UdpClient _udpClient;
        private readonly Func<byte[], byte[][]> _responder;
        private readonly bool _replyFromDifferentSocket;
        private readonly CancellationTokenSource _cts = new();

        private FakeNtpServer(Func<byte[], byte[][]> responder, bool replyFromDifferentSocket)
        {
            _responder = responder;
            _replyFromDifferentSocket = replyFromDifferentSocket;
            _udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            Port = ((IPEndPoint)_udpClient.Client.LocalEndPoint!).Port;

            _ = RunAsync();
        }

        public int Port { get; }

        public static FakeNtpServer Start(Func<byte[], byte[][]> responder, bool replyFromDifferentSocket = false)
            => new(responder, replyFromDifferentSocket);

        public static FakeNtpServer Blackhole() => new(_ => [], replyFromDifferentSocket: false);

        private async Task RunAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                UdpReceiveResult result;
                try
                {
                    result = await _udpClient.ReceiveAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    break;
                }

                foreach (var response in _responder(result.Buffer))
                {
                    try
                    {
                        if (_replyFromDifferentSocket)
                        {
                            using var other = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                            await other.SendAsync(response, response.Length, result.RemoteEndPoint).ConfigureAwait(false);
                        }
                        else
                        {
                            await _udpClient.SendAsync(response, response.Length, result.RemoteEndPoint).ConfigureAwait(false);
                        }
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _udpClient.Dispose();
            _cts.Dispose();
        }
    }

    /// <summary>Binds the same port on both loopback addresses, and never answers on either.</summary>
    private sealed class DualStackBlackhole : IDisposable
    {
        private readonly UdpClient _v4;
        private readonly UdpClient _v6;

        private DualStackBlackhole(UdpClient v4, UdpClient v6, int port)
        {
            _v4 = v4;
            _v6 = v6;
            Port = port;
        }

        public int Port { get; }

        public static DualStackBlackhole Start()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var v4 = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
                var port = ((IPEndPoint)v4.Client.LocalEndPoint!).Port;
                try
                {
                    var v6 = new UdpClient(new IPEndPoint(IPAddress.IPv6Loopback, port));
                    return new DualStackBlackhole(v4, v6, port);
                }
                catch (SocketException)
                {
                    v4.Dispose();
                }
            }

            throw new InvalidOperationException("Could not bind the same port on both loopback addresses");
        }

        public void Dispose()
        {
            _v4.Dispose();
            _v6.Dispose();
        }
    }
}
