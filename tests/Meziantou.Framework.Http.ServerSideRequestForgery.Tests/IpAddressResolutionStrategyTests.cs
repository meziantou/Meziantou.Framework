using System.Net;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

public sealed class IpAddressResolutionStrategyTests
{
    private static readonly IPAddress Ipv4Address = IPAddress.Parse("203.0.113.10");
    private static readonly IPAddress Ipv6Address = IPAddress.Parse("2001:db8::1");

    [Fact]
    public async Task Ipv4Only_SelectsIpv4Address()
    {
        var address = await IpAddressResolutionStrategy.Ipv4Only.ResolveAsync([Ipv6Address, Ipv4Address], new ServerSideRequestForgeryOptions(), CancellationToken.None);
        Assert.Equal(Ipv4Address, address);
    }

    [Fact]
    public async Task Ipv6Only_SelectsIpv6Address()
    {
        var address = await IpAddressResolutionStrategy.Ipv6Only.ResolveAsync([Ipv4Address, Ipv6Address], new ServerSideRequestForgeryOptions(), CancellationToken.None);
        Assert.Equal(Ipv6Address, address);
    }

    [Fact]
    public async Task PreferIpv4_SelectsIpv4AddressFirst()
    {
        var address = await IpAddressResolutionStrategy.PreferIpv4.ResolveAsync([Ipv6Address, Ipv4Address], new ServerSideRequestForgeryOptions(), CancellationToken.None);
        Assert.Equal(Ipv4Address, address);
    }

    [Fact]
    public async Task Random_SelectsAddressFromInput()
    {
        var addresses = new[] { Ipv4Address, Ipv6Address };
        var selectedAddress = await IpAddressResolutionStrategy.Random.ResolveAsync(addresses, new ServerSideRequestForgeryOptions(), CancellationToken.None);
        Assert.Contains(selectedAddress, addresses);
    }

    [Fact]
    public async Task RoundRobin_RotatesBetweenAddresses()
    {
        var options = new ServerSideRequestForgeryOptions();
        var addresses = new[] { Ipv4Address, Ipv6Address };

        var first = await IpAddressResolutionStrategy.RoundRobin.ResolveAsync(addresses, options, CancellationToken.None);
        var second = await IpAddressResolutionStrategy.RoundRobin.ResolveAsync(addresses, options, CancellationToken.None);
        var third = await IpAddressResolutionStrategy.RoundRobin.ResolveAsync(addresses, options, CancellationToken.None);

        Assert.NotEqual(first, second);
        Assert.Equal(first, third);
    }

    [Fact]
    public async Task Ipv4Only_SelectsTheFirstIpv4Address()
    {
        var secondIpv4Address = IPAddress.Parse("203.0.113.11");
        var address = await IpAddressResolutionStrategy.Ipv4Only.ResolveAsync([Ipv6Address, Ipv4Address, secondIpv4Address], new ServerSideRequestForgeryOptions(), CancellationToken.None);
        Assert.Same(Ipv4Address, address);
    }

    [Fact]
    public async Task Ipv6Only_SelectsTheFirstIpv6Address()
    {
        var secondIpv6Address = IPAddress.Parse("2001:db8::2");
        var address = await IpAddressResolutionStrategy.Ipv6Only.ResolveAsync([Ipv4Address, Ipv6Address, secondIpv6Address], new ServerSideRequestForgeryOptions(), CancellationToken.None);
        Assert.Same(Ipv6Address, address);
    }

    [Fact]
    public async Task PreferIpv4_FallsBackToTheFirstAddressWhenNoIpv4AddressIsAvailable()
    {
        var secondIpv6Address = IPAddress.Parse("2001:db8::2");
        var address = await IpAddressResolutionStrategy.PreferIpv4.ResolveAsync([Ipv6Address, secondIpv6Address], new ServerSideRequestForgeryOptions(), CancellationToken.None);
        Assert.Same(Ipv6Address, address);
    }

    [Fact]
    public async Task PreferIpv4_ThrowsWhenNoAddressIsAvailable()
    {
        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => IpAddressResolutionStrategy.PreferIpv4.ResolveAsync([], new ServerSideRequestForgeryOptions(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Ipv6Only_ThrowsWhenNoIpv6AddressIsAvailable()
    {
        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => IpAddressResolutionStrategy.Ipv6Only.ResolveAsync([Ipv4Address], new ServerSideRequestForgeryOptions(), CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Ipv4Only_ThrowsWhenNoIpv4AddressIsAvailable()
    {
        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => IpAddressResolutionStrategy.Ipv4Only.ResolveAsync([Ipv6Address], new ServerSideRequestForgeryOptions(), CancellationToken.None).AsTask());
    }
}
