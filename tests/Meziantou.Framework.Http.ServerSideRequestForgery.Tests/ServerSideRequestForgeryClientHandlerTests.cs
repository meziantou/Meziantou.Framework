using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.Http.ServerSideRequestForgery.Tests;

public sealed class ServerSideRequestForgeryClientHandlerTests
{
    [Theory]
    [InlineData("3.0", HttpVersionPolicy.RequestVersionOrLower)]
    [InlineData("3.0", HttpVersionPolicy.RequestVersionExact)]
    [InlineData("3.0", HttpVersionPolicy.RequestVersionOrHigher)]
    [InlineData("1.1", HttpVersionPolicy.RequestVersionOrHigher)]
    [InlineData("2.0", HttpVersionPolicy.RequestVersionOrHigher)]
    public void EnsureRequestCannotUseHttp3_RejectsRequestThatAllowsHttp3(string version, HttpVersionPolicy versionPolicy)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = Version.Parse(version),
            VersionPolicy = versionPolicy,
        };

        Assert.Throws<ServerSideRequestForgeryException>(() => ServerSideRequestForgeryClientHandler.EnsureRequestCannotUseHttp3(request));
    }

    [Theory]
    [InlineData("1.0", HttpVersionPolicy.RequestVersionOrLower)]
    [InlineData("1.1", HttpVersionPolicy.RequestVersionOrLower)]
    [InlineData("1.1", HttpVersionPolicy.RequestVersionExact)]
    [InlineData("2.0", HttpVersionPolicy.RequestVersionOrLower)]
    [InlineData("2.0", HttpVersionPolicy.RequestVersionExact)]
    public void EnsureRequestCannotUseHttp3_AllowsRequestThatCannotReachHttp3(string version, HttpVersionPolicy versionPolicy)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/")
        {
            Version = Version.Parse(version),
            VersionPolicy = versionPolicy,
        };

        ServerSideRequestForgeryClientHandler.EnsureRequestCannotUseHttp3(request);
    }

    [Fact]
    public async Task SendAsync_RejectsAnHttp3RequestWithoutConnecting()
    {
        using var server = new LoopbackHttpServer();
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Add(Uri.UriSchemeHttp);
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        using var innerHandler = new SocketsHttpHandler();
        using var handler = new ServerSideRequestForgeryClientHandler(innerHandler, options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"http://example.invalid:{server.Port}/"))
        {
            Version = HttpVersion.Version30,
            VersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => httpClient.SendAsync(request, TestContext.Current.CancellationToken));

        Assert.Equal(0, server.AcceptedConnectionCount);
    }

    [Fact]
    public async Task SendAsync_RejectsARequestThatOnlyAllowsAnUpgrade()
    {
        using var server = new LoopbackHttpServer();
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Add(Uri.UriSchemeHttp);
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        using var innerHandler = new SocketsHttpHandler();
        using var handler = new ServerSideRequestForgeryClientHandler(innerHandler, options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler)
        {
            // The Alt-Svc route needs no explicit HTTP/3 request: this policy alone is enough for the pool to
            // switch to QUIC once the server advertises an h3 alternative service.
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };

        await Assert.ThrowsAsync<ServerSideRequestForgeryException>(() => httpClient.GetAsync(new Uri($"http://example.invalid:{server.Port}/"), TestContext.Current.CancellationToken));

        Assert.Equal(0, server.AcceptedConnectionCount);
    }

    [Fact]
    public async Task SendAsync_SendsARequestThatCannotUseHttp3()
    {
        using var server = new LoopbackHttpServer();
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Add(Uri.UriSchemeHttp);
        options.SafeIpNetworks.Add(IPNetwork.Parse("127.0.0.0/8"));

        using var innerHandler = new SocketsHttpHandler();
        using var handler = new ServerSideRequestForgeryClientHandler(innerHandler, options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);

        using var response = await httpClient.GetAsync(new Uri($"http://example.invalid:{server.Port}/"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, server.AcceptedConnectionCount);
    }

    [Fact]
    public async Task SendAsync_StillAppliesTheSsrfPolicyOfTheInnerHandler()
    {
        var options = new ServerSideRequestForgeryOptions();
        options.SafeSchemes.Add(Uri.UriSchemeHttp);

        using var innerHandler = new SocketsHttpHandler();
        using var handler = new ServerSideRequestForgeryClientHandler(innerHandler, options, new FakeDnsIpAddressResolver([IPAddress.Loopback]));
        using var httpClient = new HttpClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => httpClient.GetAsync(new Uri("http://example.invalid/"), TestContext.Current.CancellationToken));

        Assert.IsType<ServerSideRequestForgeryException>(exception.InnerException);
    }

    [Fact]
    public void Constructor_ThrowsWhenTheInnerHandlerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ServerSideRequestForgeryClientHandler(innerHandler: null!, new ServerSideRequestForgeryOptions()));
    }

    [Fact]
    public void Constructor_ThrowsWhenTheOptionsAreNull()
    {
        using var innerHandler = new SocketsHttpHandler();
        Assert.Throws<ArgumentNullException>(() => new ServerSideRequestForgeryClientHandler(innerHandler, options: null!));
    }
}
