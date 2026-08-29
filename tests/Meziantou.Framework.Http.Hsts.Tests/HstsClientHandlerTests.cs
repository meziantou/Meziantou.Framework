#pragma warning disable CA2000 // Dispose objects before losing scope
using System.Net;

namespace Meziantou.Framework.Http.Hsts.Tests;
public sealed class HstsClientHandlerTests
{
    [Fact]
    public async Task DoNotUpgradeRequest()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(), hsts), disposeHandler: true);

        using var response = await client.GetAsync("http://google.com", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttp, response.RequestMessage!.RequestUri!.Scheme);
    }

    [Fact]
    public async Task UpgradeRequest()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("google.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(), hsts), disposeHandler: true);

        using var response = await client.GetAsync("http://sample.google.com", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttps, response.RequestMessage!.RequestUri!.Scheme);
    }

    [Fact]
    public async Task UpgradeRequest_AfterReadingHeader()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=31536000; includeSubDomains; preload"), hsts), disposeHandler: true);

        using var response1 = await client.GetAsync("https://sample.google.com", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttps, response1.RequestMessage!.RequestUri!.Scheme);

        using var response2 = await client.GetAsync("http://sample.google.com", XunitCancellationToken);
        Assert.Equal(Uri.UriSchemeHttps, response2.RequestMessage!.RequestUri!.Scheme);
    }

    [Fact]
    public async Task UpgradeRequest_InternationalizedDomain()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(), hsts), disposeHandler: true);

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

    [Fact]
    public async Task UpgradeRequest_PreservesExplicitPort()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(), hsts), disposeHandler: true);

        // https://datatracker.ietf.org/doc/html/rfc6797#section-8.3
        using var response = await client.GetAsync("http://example.com:8080/path", XunitCancellationToken);
        Assert.Equal(new Uri("https://example.com:8080/path"), response.RequestMessage!.RequestUri);
    }

    [Fact]
    public async Task UpgradeRequest_DefaultPortBecomes443()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(), hsts), disposeHandler: true);

        using var response = await client.GetAsync("http://example.com:80/path", XunitCancellationToken);
        Assert.Equal(new Uri("https://example.com/path"), response.RequestMessage!.RequestUri);
    }

    [Fact]
    public async Task MaxAgeZero_RemovesLearnedPolicy()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=0"), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);
        Assert.False(hsts.MustUpgradeRequest("example.com"));
    }

    [Fact]
    public async Task MaxAgeZero_DoesNotRemovePreloadedPolicy()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=0"), hsts), disposeHandler: true);

        // Like browsers, the built-in preload list cannot be turned off by a response header
        using var response = await client.GetAsync("https://github.com", XunitCancellationToken);
        Assert.True(hsts.MustUpgradeRequest("github.com"));
    }

    [Fact]
    public async Task Header_IsIgnoredForIPAddress()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=31536000"), hsts), disposeHandler: true);

        // https://datatracker.ietf.org/doc/html/rfc6797#section-8.1
        using var response = await client.GetAsync("https://127.0.0.1", XunitCancellationToken);
        Assert.False(hsts.MustUpgradeRequest("127.0.0.1"));
    }

    [Fact]
    public async Task OnlyFirstHeaderIsProcessed()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=31536000", "max-age=0"), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);
        Assert.True(hsts.MustUpgradeRequest("example.com"));
    }

    [Fact]
    public async Task MalformedFirstHeader_DoesNotFallBackToTheNextOne()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=abc", "max-age=31536000"), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);
        Assert.False(hsts.MustUpgradeRequest("example.com"));
    }

    [Fact]
    public void Constructor_ThrowsWhenConfigurationIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new HstsClientHandler(new MockHttpMessageHandler(), configuration: null!));
    }

    [Fact]
    public async Task Redirect_ToHstsHost_IsUpgraded()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.GetAsync("http://other.example/start", XunitCancellationToken);

        // The inner handler follows redirects below this one, so the hop has to be upgraded here or it goes out in cleartext
        Assert.Equal(new Uri("http://other.example/start"), inner.Requests[0].Uri);
        Assert.Equal(new Uri("https://example.com/final"), inner.Requests[1].Uri);
        Assert.Equal(new Uri("https://example.com/final"), response.RequestMessage!.RequestUri);
    }

    [Fact]
    public async Task Redirect_RelativeLocation_IsResolvedAgainstTheCurrentUri()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "/other"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.GetAsync("http://example.com/start", XunitCancellationToken);

        Assert.Equal(new Uri("http://example.com/other"), inner.Requests[1].Uri);
    }

    [Fact]
    public async Task Redirect_FromHttpsToHttp_IsNotFollowed()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com/start", XunitCancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Redirect_StopsAtTheRedirectionLimit()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(
            Redirect(HttpStatusCode.Found, "http://example.com/1"),
            Redirect(HttpStatusCode.Found, "http://example.com/2"),
            Redirect(HttpStatusCode.Found, "http://example.com/3"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 2), disposeHandler: true);

        using var response = await client.GetAsync("http://example.com/start", XunitCancellationToken);

        // The last redirect response is returned instead of being followed
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.HasCount(3, inner.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.SeeOther)]
    public async Task Redirect_TurnsPostIntoGetAndDropsTheBody(HttpStatusCode statusCode)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(statusCode, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.PostAsync("http://example.com/start", new StringContent("body"), XunitCancellationToken);

        Assert.Equal(HttpMethod.Get, inner.Requests[1].Method);
        Assert.Null(inner.Requests[1].Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task Redirect_KeepsTheMethodAndBody(HttpStatusCode statusCode)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(statusCode, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.PostAsync("http://example.com/start", new StringContent("body"), XunitCancellationToken);

        Assert.Equal(HttpMethod.Post, inner.Requests[1].Method);
        Assert.Equal("body", inner.Requests[1].Body);
    }

    [Fact]
    public async Task Redirect_ClearsTheAuthorizationHeader()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "http://other.example/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/start");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "secret");

        using var response = await client.SendAsync(request, XunitCancellationToken);

        Assert.Equal("Bearer secret", inner.Requests[0].Authorization);
        Assert.Null(inner.Requests[1].Authorization);
    }

    [Fact]
    public async Task Redirect_ReadsTheHeaderOfEveryHop()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var redirect = Redirect(HttpStatusCode.Found, "https://second.example/final");
        redirect.Headers.TryAddWithoutValidation("Strict-Transport-Security", "max-age=31536000");
        var final = new HttpResponseMessage(HttpStatusCode.OK);
        final.Headers.TryAddWithoutValidation("Strict-Transport-Security", "max-age=31536000");
        var inner = new RecordingHttpMessageHandler(redirect, final);
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.GetAsync("https://first.example/start", XunitCancellationToken);

        Assert.True(hsts.MustUpgradeRequest("first.example"));
        Assert.True(hsts.MustUpgradeRequest("second.example"));
    }

    [Fact]
    public async Task Redirect_IsNotFollowedWhenTheInnerHandlerDoesNotRedirect()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "http://example.com/final"));

        // A handler this one cannot take the redirects over from keeps returning the redirect responses as is
        using var client = new HttpClient(new HstsClientHandler(inner, hsts), disposeHandler: true);

        using var response = await client.GetAsync("http://example.com/start", XunitCancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public void Constructor_TakesTheRedirectsOverFromTheInnerHandler()
    {
        var inner = new SocketsHttpHandler();
        Assert.True(inner.AllowAutoRedirect);

        using var handler = new HstsClientHandler(inner, new HstsDomainPolicyCollection(includePreloadDomains: false));

        // Redirects followed by the inner handler would bypass the HSTS upgrade
        Assert.False(inner.AllowAutoRedirect);
    }

    [Fact]
    public void Constructor_KeepsAnInnerHandlerConfiguredNotToRedirect()
    {
        var inner = new SocketsHttpHandler { AllowAutoRedirect = false };

        using var handler = new HstsClientHandler(inner, new HstsDomainPolicyCollection(includePreloadDomains: false));

        Assert.False(inner.AllowAutoRedirect);
    }

    private static HttpResponseMessage Redirect(HttpStatusCode statusCode, string location)
    {
        var response = new HttpResponseMessage(statusCode);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    private sealed class RecordingHttpMessageHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<(Uri Uri, HttpMethod Method, string? Authorization, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!, request.Method, request.Headers.Authorization?.ToString(), body));

            var response = _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
            response.RequestMessage = request;
            return response;
        }
    }

    private sealed class MockHttpMessageHandler(params string[] headerResponses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
            };

            foreach (var headerResponse in headerResponses)
            {
                response.Headers.TryAddWithoutValidation("Strict-Transport-Security", headerResponse);
            }

            return Task.FromResult(response);
        }
    }
}
