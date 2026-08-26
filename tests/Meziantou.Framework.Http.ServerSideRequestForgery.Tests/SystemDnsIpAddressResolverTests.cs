using System.Net;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

public sealed class SystemDnsIpAddressResolverTests
{
    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("[2001:db8::1]", "2001:db8::1")]
    [InlineData("::ffff:203.0.113.10", "::ffff:203.0.113.10")]
    public async Task ResolveAsync_ReturnsLiteralWithoutQueryingDns(string host, string expectedAddress)
    {
        var addresses = await SystemDnsIpAddressResolver.Instance.ResolveAsync(host, CancellationToken.None);

        var address = Assert.Single(addresses);
        Assert.Equal(IPAddress.Parse(expectedAddress), address);
    }

    [Fact]
    public async Task ResolveAsync_ResolvesHostName()
    {
        var addresses = await SystemDnsIpAddressResolver.Instance.ResolveAsync("localhost", CancellationToken.None);

        Assert.NotEmpty(addresses);
        Assert.All(addresses, address => Assert.True(IPAddress.IsLoopback(address), $"'{address}' is not a loopback address"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ResolveAsync_ThrowsForEmptyHost(string host)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => SystemDnsIpAddressResolver.Instance.ResolveAsync(host, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task ResolveAsync_ThrowsForNullHost()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => SystemDnsIpAddressResolver.Instance.ResolveAsync(host: null!, CancellationToken.None).AsTask());
    }
}
