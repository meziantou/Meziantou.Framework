using Meziantou.Framework.Ntp;

namespace Meziantou.Framework.Ntp.Tests;

public sealed class NtpClientTests
{
    private static Task<NtpResponse> QueryWithRetryAsync(NtpClient client)
    {
        return Retry(() => client.QueryAsync(XunitCancellationToken));
    }

    private static async Task<NtpResponse> QueryWithFallbackAsync(NtpVersion version = NtpVersion.V4)
    {
        string[] servers = ["time.google.com", "pool.ntp.org", "time.cloudflare.com"];

        foreach (var server in servers)
        {
            try
            {
                using var client = new NtpClient(server, new NtpClientOptions { Version = version });
                return await QueryWithRetryAsync(client);
            }
            catch when (server != servers[^1])
            {
            }
        }

        using var lastClient = new NtpClient(servers[^1], new NtpClientOptions { Version = version });
        return await QueryWithRetryAsync(lastClient);
    }

    [Fact]
    public async Task Query_V4_ReturnsValidResponse()
    {
        var response = await QueryWithFallbackAsync(NtpVersion.V4);

        Assert.Equal(NtpVersion.V4, response.Version);
        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
        Assert.True(response.ReceiveTimestamp > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Query_V3_ReturnsValidResponse()
    {
        var response = await QueryWithFallbackAsync(NtpVersion.V3);

        Assert.Equal(NtpVersion.V3, response.Version);
        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Query_ClockOffset_IsReasonable()
    {
        var response = await QueryWithFallbackAsync();

        // Clock offset should be within 1 minute for a properly synchronized machine
        Assert.True(Math.Abs(response.ClockOffset.TotalMinutes) < 1, $"Clock offset was {response.ClockOffset}");
    }

    [Fact]
    public async Task Query_RoundTripDelay_IsPositive()
    {
        var response = await QueryWithFallbackAsync();

        Assert.True(response.RoundTripDelay >= TimeSpan.Zero, $"Round-trip delay was {response.RoundTripDelay}");
        // Round-trip delay should be less than 5 seconds for a normal network
        Assert.True(response.RoundTripDelay < TimeSpan.FromSeconds(5), $"Round-trip delay was {response.RoundTripDelay}");
    }

    [Fact]
    public async Task Query_LeapIndicator_IsValid()
    {
        var response = await QueryWithFallbackAsync();

        Assert.True(Enum.IsDefined(response.LeapIndicator));
    }

    [Fact]
    public async Task Query_ReferenceTimestamp_IsRecent()
    {
        var response = await QueryWithFallbackAsync();

        // Reference timestamp should be within the last 24 hours for an active server
        var age = DateTimeOffset.UtcNow - response.ReferenceTimestamp;
        Assert.True(age < TimeSpan.FromDays(1), $"Reference timestamp age was {age}");
    }

    [Fact]
    public async Task Query_TimeGoogle_ReturnsValidResponse()
    {
        using var client = new NtpClient("time.google.com");
        var response = await QueryWithRetryAsync(client);

        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Query_PoolNtpOrg_ReturnsValidResponse()
    {
        using var client = new NtpClient("pool.ntp.org");
        var response = await QueryWithRetryAsync(client);

        Assert.True(response.Stratum > 0);
        Assert.True(response.TransmitTimestamp > DateTimeOffset.UnixEpoch);
    }
}
