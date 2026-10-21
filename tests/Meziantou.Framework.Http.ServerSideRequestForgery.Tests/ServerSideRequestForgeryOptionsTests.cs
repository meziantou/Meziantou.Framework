using System.Net;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

public sealed class ServerSideRequestForgeryOptionsTests
{
    [Fact]
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The HttpClient disposes the handlers")]
    public void ReadmeExample_Usage()
    {
        #region ServerSideRequestForgery_Usage
        var options = new ServerSideRequestForgeryOptions
        {
            ResolutionStrategy = IpAddressResolutionStrategy.PreferIpv4,
            DisallowMixedSafeAndUnsafeIpAddresses = true,
        };

        options.SafeSchemes.Add("https");
        options.SafeSchemes.Add("wss");
        options.UnsafeIpNetworks.Add(IPNetwork.Parse("203.0.113.0/24"));
        options.SafeIpNetworks.Add(IPNetwork.Parse("198.51.100.10/32"));

        var handler = new ServerSideRequestForgeryClientHandler(new SocketsHttpHandler { UseProxy = false }, options);

        using var httpClient = new HttpClient(handler, disposeHandler: true);
        #endregion
    }

    [Fact]
    public void ReadmeExample_ClearDefaults()
    {
        #region ServerSideRequestForgery_ClearDefaults
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Clear();
        options.SafeSchemes.Add("https");
        #endregion

        Assert.Equal(["https"], options.SafeSchemes);
    }

    [Fact]
    public void Constructor_UsesExpectedDefaults()
    {
        var options = new ServerSideRequestForgeryOptions();

        Assert.Contains(options.SafeSchemes, scheme => string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(options.SafeSchemes, scheme => string.Equals(scheme, "wss", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(options.UnsafeIpNetworks, network => network.Contains(IPAddress.Loopback));
        Assert.Same(IpAddressResolutionStrategy.PreferIpv4, options.ResolutionStrategy);
        Assert.True(options.DisallowMixedSafeAndUnsafeIpAddresses);
        Assert.Same(NullLogger.Instance, options.Logger);
    }

    [Fact]
    public void UnsafeIpNetworks_ContainsLoopback()
    {
        var options = new ServerSideRequestForgeryOptions();
        Assert.Contains(options.UnsafeIpNetworks, network => network.Contains(IPAddress.Loopback));
    }
}
