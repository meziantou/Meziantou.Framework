using System.Net;
using Meziantou.Framework.Http.ServerSideRequestForgery;
using Xunit;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

public sealed class ServerSideRequestForgeryOptionsTests
{
    [Fact]
    public void Constructor_UsesExpectedDefaults()
    {
        var options = new ServerSideRequestForgeryOptions();

        Assert.Contains(options.SafeSchemes, scheme => string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(options.SafeSchemes, scheme => string.Equals(scheme, "wss", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(options.UnsafeIpNetworks, network => network.Contains(IPAddress.Loopback));
        Assert.Same(IpAddressResolutionStrategy.PreferIpv4, options.ResolutionStrategy);
        Assert.True(options.DisallowMixedSafeAndUnsafeIpAddresses);
    }

    [Fact]
    public void UnsafeIpNetworks_ContainsLoopback()
    {
        var options = new ServerSideRequestForgeryOptions();
        Assert.Contains(options.UnsafeIpNetworks, network => network.Contains(IPAddress.Loopback));
    }
}
