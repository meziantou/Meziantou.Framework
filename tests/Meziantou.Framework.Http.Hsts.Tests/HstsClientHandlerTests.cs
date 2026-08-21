#pragma warning disable CA2000 // Dispose objects before losing scope
using System.Net;

namespace Meziantou.Framework.Http.Hsts.Tests;
public sealed class HstsClientHandlerTests
{
    [Fact]
    public async Task DoNotUpgradeRequest()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(headerResponse: null), hsts), disposeHandler: true);

        using var response = await client.GetAsync("http://google.com", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttp, response.RequestMessage!.RequestUri!.Scheme);
    }

    [Fact]
    public async Task UpgradeRequest()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("google.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(headerResponse: null), hsts), disposeHandler: true);

        using var response = await client.GetAsync("http://sample.google.com", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttps, response.RequestMessage!.RequestUri!.Scheme);
    }

    [Fact]
    public async Task UpgradeRequest_AfterReadingHeader()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(headerResponse: "max-age=31536000; includeSubDomains; preload"), hsts), disposeHandler: true);

        using var response1 = await client.GetAsync("https://sample.google.com", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttps, response1.RequestMessage!.RequestUri!.Scheme);

        using var response2 = await client.GetAsync("http://sample.google.com", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttps, response2.RequestMessage!.RequestUri!.Scheme);
    }

    [Fact]
    public async Task UpgradeRequest_InternationalizedDomain()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(headerResponse: null), hsts), disposeHandler: true);

        // 跳.jp (xn--vt3a.jp) is in the HSTS preload list, which stores Punycode names
        using var response = await client.GetAsync("http://跳.jp", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttps, response.RequestMessage!.RequestUri!.Scheme);
    }

    [Theory]
    [InlineData("max-age=abc")]
    [InlineData("max-age=")]
    [InlineData("max-age=-1")]
    [InlineData("max-age=99999999999999999999999")]
    [InlineData("includeSubDomains")]
    [InlineData("")]
    public async Task MalformedHeader_IsIgnored(string headerResponse)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(headerResponse), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(hsts.MustUpgradeRequest("example.com"));
    }

    [Fact]
    public async Task QuotedMaxAge_IsSupported()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=\"31536000\"; includeSubDomains"), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);
        Assert.True(hsts.MustUpgradeRequest("example.com"));
        Assert.True(hsts.MustUpgradeRequest("foo.example.com"));
    }

    [Fact]
    public async Task VeryLargeMaxAge_IsClamped()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=9999999999"), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);
        Assert.True(hsts.MustUpgradeRequest("example.com"));
    }

    private sealed class MockHttpMessageHandler(string? headerResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
            };

            if (headerResponse is not null)
            {
                response.Headers.TryAddWithoutValidation("Strict-Transport-Security", headerResponse);
            }

            return Task.FromResult(response);
        }
    }
}
