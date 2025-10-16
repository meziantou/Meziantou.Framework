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

    private sealed class MockHttpMessageHandler(string? headerResponse) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
            };

            if (headerResponse != null)
            {
                response.Headers.Add("Strict-Transport-Security", headerResponse);
            }

            return Task.FromResult(response);
        }
    }
}
