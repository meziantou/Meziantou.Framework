#nullable enable
using System.Net;
using Meziantou.Framework.Ntp;
using TestUtilities;

namespace Meziantou.Framework.Ntp.Tests;

public sealed class NtpClientTests(ITestOutputHelper testOutputHelper)
{
    private const int RetryCount = 3;

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
                using var client = new NtpClient(server, CreateTestOptions(version, timeout));
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

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipOnGitHubActions(FactOperatingSystem.OSX)]
    public async Task Query_V4_ReturnsValidResponse()
    {
        var response = await QueryWithFallbackAsync(NtpVersion.V4);

        Assert.Equal(NtpVersion.V4, response.Version);
        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
        Assert.True(response.ReceiveTimestamp > DateTimeOffset.UnixEpoch);
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipOnGitHubActions(FactOperatingSystem.OSX)]
    public async Task Query_V3_ReturnsValidResponse()
    {
        var response = await QueryWithFallbackAsync(NtpVersion.V3);

        Assert.Equal(NtpVersion.V3, response.Version);
        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipOnGitHubActions(FactOperatingSystem.OSX)]
    public async Task Query_ClockOffset_IsReasonable()
    {
        var response = await QueryWithFallbackAsync();

        // Clock offset should be within 1 minute for a properly synchronized machine
        Assert.True(Math.Abs(response.ClockOffset.TotalMinutes) < 1, $"Clock offset was {response.ClockOffset}");
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipOnGitHubActions(FactOperatingSystem.OSX)]
    public async Task Query_RoundTripDelay_IsPositive()
    {
        var response = await QueryWithFallbackAsync();

        Assert.True(response.RoundTripDelay >= TimeSpan.Zero, $"Round-trip delay was {response.RoundTripDelay}");
        // Round-trip delay should be less than 5 seconds for a normal network
        Assert.True(response.RoundTripDelay < TimeSpan.FromSeconds(5), $"Round-trip delay was {response.RoundTripDelay}");
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipOnGitHubActions(FactOperatingSystem.OSX)]
    public async Task Query_LeapIndicator_IsValid()
    {
        var response = await QueryWithFallbackAsync();

        Assert.True(Enum.IsDefined(response.LeapIndicator));
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipOnGitHubActions(FactOperatingSystem.OSX)]
    public async Task Query_ReferenceTimestamp_IsRecent()
    {
        var response = await QueryWithFallbackAsync();

        // Reference timestamp should be within the last 24 hours for an active server
        var age = DateTimeOffset.UtcNow - response.ReferenceTimestamp;
        Assert.True(age < TimeSpan.FromDays(1), $"Reference timestamp age was {age}");
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipOnGitHubActions(FactOperatingSystem.OSX)]
    public async Task Query_TimeGoogle_ReturnsValidResponse()
    {
        await LogDnsResolutionAsync(["time.google.com"]);
        var response = await QueryWithRetryAsync("time.google.com");

        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }

    // https://github.com/actions/runner-images/issues/11939
    [Fact, SkipOnGitHubActions(FactOperatingSystem.OSX)]
    public async Task Query_PoolNtpOrg_ReturnsValidResponse()
    {
        await LogDnsResolutionAsync(["pool.ntp.org"]);
        var response = await QueryWithRetryAsync("pool.ntp.org", retryCount: 8, timeout: TimeSpan.FromSeconds(5), delayBetweenAttempts: TimeSpan.FromMilliseconds(200));

        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }
}
