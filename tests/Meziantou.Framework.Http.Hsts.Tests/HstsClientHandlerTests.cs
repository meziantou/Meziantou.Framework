#pragma warning disable CA2000 // Dispose objects before losing scope
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

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

    [Theory]
    [InlineData("max-age = 31536000")]
    [InlineData("max-age =31536000")]
    [InlineData("max-age= 31536000")]
    public async Task WhitespaceAroundTheDirectiveSeparator_IsAccepted(string headerResponse)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(headerResponse), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);
        Assert.True(hsts.MustUpgradeRequest("example.com"));
    }

    [Theory]
    [InlineData("max-age=31536000; max-age=0")]
    [InlineData("max-age=0; max-age=31536000")]
    [InlineData("max-age=31536000; includeSubDomains; includeSubDomains")]
    public async Task RepeatedDirective_IsIgnored(string headerResponse)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(headerResponse), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);

        // https://datatracker.ietf.org/doc/html/rfc6797#section-6.1
        // The whole header field is ignored, so the policy already in the collection is left alone
        Assert.True(hsts.MustUpgradeRequest("example.com"));
        Assert.False(hsts.MustUpgradeRequest("sub.example.com"));
    }

    [Fact]
    public async Task IncludeSubDomainsWithAValue_IsIgnored()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=31536000; includeSubDomains="), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);

        // includeSubDomains is valueless; the malformed directive is skipped but the header stays usable
        Assert.True(hsts.MustUpgradeRequest("example.com"));
        Assert.False(hsts.MustUpgradeRequest("sub.example.com"));
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
    public async Task Header_IsReadWhenTheInnerHandlerDoesNotSetRequestMessage()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new NoRequestMessageHttpMessageHandler("max-age=31536000"), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);

        // A handler that builds its own response, such as a cache or a test double, may leave RequestMessage unset
        Assert.Null(response.RequestMessage);
        Assert.True(hsts.MustUpgradeRequest("example.com"));
    }

    [Fact]
    public async Task Header_IsIgnoredForIPAddress_WhenTheInnerHandlerDoesNotSetRequestMessage()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new NoRequestMessageHttpMessageHandler("max-age=31536000"), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://127.0.0.1", XunitCancellationToken);

        Assert.False(hsts.MustUpgradeRequest("127.0.0.1"));
    }

    private sealed class NoRequestMessageHttpMessageHandler(string header) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("Strict-Transport-Security", header);
            return Task.FromResult(response);
        }
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
    public async Task RedirectTakeOver_IsDeferredToTheFirstRequestAndUnwrapsThePipeline()
    {
        var primary = new SocketsHttpHandler();
        var pipeline = new ShortCircuitHandler { InnerHandler = new PassThroughHandler { InnerHandler = primary } };
        using var handler = new HstsClientHandler(pipeline, new HstsDomainPolicyCollection(includePreloadDomains: false));

        // The inner handler belongs to the caller, so the constructor does not reconfigure it
        Assert.True(primary.AllowAutoRedirect);

        using var client = new HttpClient(handler, disposeHandler: false);
        using var response = await client.GetAsync("https://example.com/", XunitCancellationToken);

        // AddHttpClient builds exactly this shape, so the primary handler has to be found through the pipeline.
        // Redirects it followed itself would bypass the HSTS upgrade.
        Assert.False(primary.AllowAutoRedirect);
    }

    [Fact]
    public async Task RedirectTakeOver_HandlesAnHttpClientHandler()
    {
        var primary = new HttpClientHandler();
        var pipeline = new ShortCircuitHandler { InnerHandler = primary };
        using var handler = new HstsClientHandler(pipeline, new HstsDomainPolicyCollection(includePreloadDomains: false));

        using var client = new HttpClient(handler, disposeHandler: false);
        using var response = await client.GetAsync("https://example.com/", XunitCancellationToken);

        Assert.False(primary.AllowAutoRedirect);
    }

    [Fact]
    public async Task RedirectTakeOver_FollowsRedirectsThroughAPublicConstructor()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        // The count the take-over returns has to be load-bearing: an inner handler that would have followed
        // redirects means this handler follows them now, upgrading every hop
        var pipeline = new ShortCircuitHandler(Redirect(HttpStatusCode.Found, "http://example.com/final")) { InnerHandler = new SocketsHttpHandler() };
        using var client = new HttpClient(new HstsClientHandler(pipeline, hsts), disposeHandler: true);

        using var response = await client.GetAsync("http://other.example/start", XunitCancellationToken);

        Assert.Equal(new Uri("https://example.com/final"), pipeline.Requests[1]);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RedirectTakeOver_LeavesAnInnerHandlerConfiguredNotToRedirect()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var pipeline = new ShortCircuitHandler(Redirect(HttpStatusCode.Found, "http://example.com/final")) { InnerHandler = new SocketsHttpHandler { AllowAutoRedirect = false } };
        using var client = new HttpClient(new HstsClientHandler(pipeline, hsts), disposeHandler: true);

        using var response = await client.GetAsync("http://example.com/start", XunitCancellationToken);

        // The caller opted out of redirects, so neither handler follows them
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(pipeline.Requests);
    }

    [Fact]
    public async Task RedirectTakeOver_ThrowsWhenTheInnerHandlerHasAlreadySentRequests()
    {
        var shared = new SocketsHttpHandler();
        using (var warmup = new HttpClient(shared, disposeHandler: false))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => warmup.GetAsync("http://localhost:1/", XunitCancellationToken));
        }

        var pipeline = new ShortCircuitHandler { InnerHandler = shared };
        using var client = new HttpClient(new HstsClientHandler(pipeline, new HstsDomainPolicyCollection(includePreloadDomains: false)), disposeHandler: false);

        // Carrying on silently would leave the inner handler resolving redirects below the HSTS upgrade
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync("https://example.com/", XunitCancellationToken));
        Assert.Contains("AllowAutoRedirect", exception.Message);
    }

    [Fact]
    public async Task RedirectTakeOver_ExplicitCountDoesNotTouchTheInnerHandler()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var shared = new SocketsHttpHandler();
        using (var warmup = new HttpClient(shared, disposeHandler: false))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => warmup.GetAsync("http://localhost:1/", XunitCancellationToken));
        }

        // An explicit budget is how HSTS is added to a handler that is shared or already in use
        var pipeline = new ShortCircuitHandler(Redirect(HttpStatusCode.Found, "http://example.com/final")) { InnerHandler = shared };
        using var client = new HttpClient(new HstsClientHandler(pipeline, hsts, maxAutomaticRedirections: 5), disposeHandler: false);

        using var response = await client.GetAsync("http://example.com/start", XunitCancellationToken);

        Assert.True(shared.AllowAutoRedirect);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.HasCount(2, pipeline.Requests);
    }

    private sealed class PassThroughHandler : DelegatingHandler
    {
    }

    // Answers from a queue without ever calling the inner handler, so the take-over has a real
    // SocketsHttpHandler or HttpClientHandler to find at the bottom of the pipeline but nothing hits the network
    private sealed class ShortCircuitHandler(params HttpResponseMessage[] responses) : DelegatingHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var response = _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task Header_CannotWeakenAPreloadedPolicy()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: true);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=60"), hsts), disposeHandler: true);

        // A short max-age without includeSubDomains used to replace the preload entry and then delete it once
        // it lapsed, which is the downgrade the max-age=0 guard exists to prevent
        using var response = await client.GetAsync("https://github.com", XunitCancellationToken);

        Assert.True(hsts.MustUpgradeRequest("sub.github.com"));

        timeProvider.Advance(TimeSpan.FromDays(1));

        Assert.True(hsts.MustUpgradeRequest("github.com"));
        Assert.True(hsts.MustUpgradeRequest("sub.github.com"));
        Assert.Contains(hsts, policy => policy.Host == "github.com");
    }

    [Fact]
    public async Task Header_IsIgnoredOverPlaintextHttp()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=31536000; includeSubDomains"), hsts), disposeHandler: true);

        // https://datatracker.ietf.org/doc/html/rfc6797#section-8.1
        // The header must be ignored over an unauthenticated connection, or anyone on the path can pin a host
        using var response = await client.GetAsync("http://example.com", XunitCancellationToken);

        Assert.Equal(Uri.UriSchemeHttp, response.RequestMessage!.RequestUri!.Scheme);
        Assert.False(hsts.MustUpgradeRequest("example.com"));
        Assert.Empty(hsts);
    }

    [Fact]
    public async Task Header_IsIgnoredForIPv6Address()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=31536000"), hsts), disposeHandler: true);

        // https://datatracker.ietf.org/doc/html/rfc6797#section-8.1
        using var response = await client.GetAsync("https://[::1]", XunitCancellationToken);

        Assert.False(hsts.MustUpgradeRequest("[::1]"));
        Assert.Empty(hsts);
    }

    [Fact]
    public async Task QuotedDirectiveValueContainingASemicolon_DoesNotBreakTheHeader()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);

        // https://datatracker.ietf.org/doc/html/rfc6797#section-6.1
        // A directive value may be a quoted-string, so a ';' inside quotes is not a separator: splitting on it
        // would either invalidate the header or smuggle in an includeSubDomains the server never sent
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=31536000; extension=\"a; includeSubDomains; b\""), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);

        Assert.True(hsts.MustUpgradeRequest("example.com"));
        Assert.False(hsts.MustUpgradeRequest("sub.example.com"));
    }

    [Fact]
    public async Task VeryLargeMaxAge_IsClampedToOneHundredYears()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2024-01-01T00:00:00Z", CultureInfo.InvariantCulture));
        var hsts = new HstsDomainPolicyCollection(timeProvider, includePreloadDomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler("max-age=9223372036854775807"), hsts), disposeHandler: true);

        using var response = await client.GetAsync("https://example.com", XunitCancellationToken);

        // The expiry is asserted, not just the boolean: without the clamp the TimeSpan conversion overflows
        Assert.True(hsts.TryGetPolicy("example.com", out var policy));
        Assert.Equal(timeProvider.GetUtcNow().AddDays(365 * 100), policy.ExpiresAt);
    }

    [Fact]
    public async Task UpgradeRequest_KeepsUserInfoWithAnEmptyUserName()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(), hsts), disposeHandler: true);

        // System.Uri accepts this, but UriBuilder cannot rebuild it
        using var response = await client.GetAsync("http://:password@example.com/path", XunitCancellationToken);

        Assert.Equal(new Uri("https://:password@example.com/path"), response.RequestMessage!.RequestUri);
    }

    [Theory]
    [InlineData("http://example.com/a%2Fb/c?x=1&y=a%20b")]
    [InlineData("http://example.com/%E2%82%AC/caf%C3%A9?s=%2B")]
    [InlineData("http://example.com/?q=100%25")]
    public async Task UpgradeRequest_PreservesTheEscapedPathAndQuery(string url)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: false);
        using var client = new HttpClient(new HstsClientHandler(new MockHttpMessageHandler(), hsts), disposeHandler: true);

        using var response = await client.GetAsync(url, XunitCancellationToken);

        Assert.Equal(new Uri(url).PathAndQuery, response.RequestMessage!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Redirect_ClearsTheChunkedTransferEncodingWithTheBody()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/start") { Content = new StringContent("body") };
        request.Headers.TransferEncodingChunked = true;

        using var response = await client.SendAsync(request, XunitCancellationToken);

        // A GET with no content that still advertises a chunked body cannot be sent at all
        Assert.Equal(HttpMethod.Get, inner.Requests[1].Method);
        Assert.NotEqual(true, inner.Requests[1].Chunked);
    }

    [Fact]
    public async Task Redirect_KeepsTheChunkedTransferEncodingWithTheBody()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.TemporaryRedirect, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/start") { Content = new StringContent("body") };
        request.Headers.TransferEncodingChunked = true;

        using var response = await client.SendAsync(request, XunitCancellationToken);

        // 307 keeps the method and the body, so the transfer encoding still describes the request
        Assert.Equal(HttpMethod.Post, inner.Requests[1].Method);
        Assert.Equal(true, inner.Requests[1].Chunked);
    }

    [Fact]
    public async Task Redirect_DoesNotDisposeTheCallerContent()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);
        using var content = new StringContent("body");
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://example.com/start") { Content = content };

        using var response = await client.SendAsync(request, XunitCancellationToken);

        // The content belongs to the caller, who may still hold a reference to it
        Assert.Equal("body", await content.ReadAsStringAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task Redirect_CarriesTheFragmentOverToALocationWithoutOne()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.GetAsync("http://example.com/start#section", XunitCancellationToken);

        // Matches SocketsHttpHandler, so a caller resolving a permalink keeps the fragment
        Assert.Equal("#section", inner.Requests[1].Uri.Fragment);
    }

    [Fact]
    public async Task Redirect_KeepsTheFragmentOfTheLocation()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "http://example.com/final#other"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.GetAsync("http://example.com/start#section", XunitCancellationToken);

        Assert.Equal("#other", inner.Requests[1].Uri.Fragment);
    }

    [Fact]
    public async Task Redirect_MultipleChoicesTurnsPostIntoGet()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.MultipleChoices, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.PostAsync("http://example.com/start", new StringContent("body"), XunitCancellationToken);

        Assert.Equal(HttpMethod.Get, inner.Requests[1].Method);
        Assert.Null(inner.Requests[1].Body);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    public async Task Redirect_SeeOtherTurnsAnyMethodIntoGet(string method)
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.SeeOther, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);
        using var request = new HttpRequestMessage(new HttpMethod(method), "http://example.com/start") { Content = new StringContent("body") };

        using var response = await client.SendAsync(request, XunitCancellationToken);

        // 303 turns anything other than GET and HEAD into a GET, unlike 301 and 302 which only convert POST
        Assert.Equal(HttpMethod.Get, inner.Requests[1].Method);
        Assert.Null(inner.Requests[1].Body);
    }

    [Fact]
    public async Task Redirect_MovedPermanentlyKeepsANonPostMethodAndBody()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.MovedPermanently, "http://example.com/final"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);
        using var request = new HttpRequestMessage(HttpMethod.Put, "http://example.com/start") { Content = new StringContent("body") };

        using var response = await client.SendAsync(request, XunitCancellationToken);

        Assert.Equal(HttpMethod.Put, inner.Requests[1].Method);
        Assert.Equal("body", inner.Requests[1].Body);
    }

    [Fact]
    public async Task Redirect_ToANonHttpLocation_IsNotFollowed()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        var inner = new RecordingHttpMessageHandler(Redirect(HttpStatusCode.Found, "mailto:someone@example.com"));
        using var client = new HttpClient(new HstsClientHandler(inner, hsts, maxAutomaticRedirections: 50), disposeHandler: true);

        using var response = await client.GetAsync("http://example.com/start", XunitCancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Constructor_WithoutAnInnerHandler_UsesTheDefaultCollection()
    {
        // The shape IHttpClientFactory needs: the pipeline supplies the inner handler after construction
        using var handler = new HstsClientHandler() { InnerHandler = new ShortCircuitHandler() };
        using var client = new HttpClient(handler, disposeHandler: false);

        using var response = await client.GetAsync("http://github.com/", XunitCancellationToken);

        Assert.Equal(Uri.UriSchemeHttps, response.RequestMessage!.RequestUri!.Scheme);
    }

    [Fact]
    public void Constructor_ThrowsWhenTheInnerHandlerIsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new HstsClientHandler(innerHandler: null!));
    }

    [Fact]
    public void Constructor_ThrowsWhenTheRedirectionCountIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HstsClientHandler(new MockHttpMessageHandler(), new HstsDomainPolicyCollection(includePreloadDomains: false), maxAutomaticRedirections: -1));
    }

    [Fact]
    public async Task CanBeRegisteredWithAddHttpMessageHandler()
    {
        var hsts = new HstsDomainPolicyCollection(includePreloadDomains: false);
        hsts.Add("example.com", DateTimeOffset.UtcNow.AddYears(1), includeSubdomains: true);

        var services = new ServiceCollection();
        services.AddTransient(_ => new HstsClientHandler(hsts));
        services.AddHttpClient("api")
            .AddHttpMessageHandler<HstsClientHandler>()
            .ConfigurePrimaryHttpMessageHandler(() => new ShortCircuitHandler { InnerHandler = new SocketsHttpHandler() });

        using var provider = services.BuildServiceProvider();

        // AddHttpMessageHandler requires InnerHandler to be null, so the handler has to allow being built
        // without one and resolve the pipeline later
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("api");
        using var response = await client.GetAsync("http://example.com/", XunitCancellationToken);

        Assert.Equal(Uri.UriSchemeHttps, response.RequestMessage!.RequestUri!.Scheme);
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

        public List<(Uri Uri, HttpMethod Method, string? Authorization, string? Body, bool? Chunked)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!, request.Method, request.Headers.Authorization?.ToString(), body, request.Headers.TransferEncodingChunked));

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
