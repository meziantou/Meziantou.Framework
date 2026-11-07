using System.Net;
using System.Net.Http.Headers;

namespace Meziantou.Framework.Http.Caching.Tests;

public class RequestValidationTests
{
    [Fact]
    public async Task ReadmeExample_ForceRevalidation()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=3600"), ("ETag", "\"v1\""));
        context.AddNotModifiedResponse([("If-None-Match", "\"v1\"")], ("Cache-Control", "max-age=3600"), ("ETag", "\"v1\""));
        var httpClient = context.HttpClient;
        using var initialResponse = await httpClient.GetAsync("https://api.example.com/data");

        #region HttpCachingDelegateHandler_ForceRevalidation
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await httpClient.SendAsync(request);
        // Note: If the response has Cache-Control: immutable and is fresh, it will still be served from cache
        #endregion

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("content", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadmeExample_AcceptStaleResponses()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=60"));
        var httpClient = context.HttpClient;
        using var initialResponse = await httpClient.GetAsync("https://api.example.com/data");
        context.TimeProvider.Advance(TimeSpan.FromMinutes(3)); // The cached response is stale for 2 minutes

        #region HttpCachingDelegateHandler_AcceptStaleResponses
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.CacheControl = new CacheControlHeaderValue
        {
            MaxStale = true,
            MaxStaleLimit = TimeSpan.FromMinutes(5), // Accept up to 5 minutes stale
        };
        using var response = await httpClient.SendAsync(request);
        #endregion

        Assert.NotNull(response.Headers.Age);
    }

    [Fact]
    public async Task ReadmeExample_OnlyIfCached()
    {
        await using var context = new HttpTestContext();
        var httpClient = context.HttpClient;

        #region HttpCachingDelegateHandler_OnlyIfCached
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        request.Headers.CacheControl = new CacheControlHeaderValue { OnlyIfCached = true };
        using var response = await httpClient.SendAsync(request);
        // Returns cached response or 504 Gateway Timeout
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        #endregion
    }

    [Fact]
    public async Task WhenMinFreshRequirementNotMetThenRevalidate()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", ("Cache-Control", "max-age=10"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=10
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        // Advance time so remaining freshness is 5 seconds
        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));

        // Request requires min-fresh=8 seconds, but only 5 remain
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            MinFresh = TimeSpan.FromSeconds(8),
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 5
              Cache-Control: max-age=10
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenMinFreshRequirementMetThenCachedResponseServed()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        // Advance time by 10 seconds, leaving 90 seconds of freshness
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request requires min-fresh=50 seconds (still have 90, so OK)
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            MinFresh = TimeSpan.FromSeconds(50),
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 10
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenRequestMaxAgeExceededThenRevalidate()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", ("Cache-Control", "max-age=100"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        // Advance time by 10 seconds
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with max-age=5 (current age is 10, exceeds limit)
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            MaxAge = TimeSpan.FromSeconds(5),
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 10
              Cache-Control: max-age=100
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestMaxAgeWithinLimitThenCachedResponseServed()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        // Advance time by 10 seconds
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with max-age=50 (current age is 10, within limit)
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            MaxAge = TimeSpan.FromSeconds(50),
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 10
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenOnlyIfCachedWithFreshResponseThenServed()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        // Request with only-if-cached
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            OnlyIfCached = true,
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenOnlyIfCachedResponseRequiresValidationThen504()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=600, no-cache"), ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-cache, max-age=600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { OnlyIfCached = true };
        await context.SnapshotResponse(request, """
            StatusCode: 504 (GatewayTimeout)
            Content:
              Headers:
                Content-Length: 0
              Value:
            """);
    }

    [Fact]
    public async Task WhenOnlyIfCachedWithStaleAndMaxStaleThenServed()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-cached", ("Cache-Control", "max-age=1"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: stale-cached
            """);

        // Advance time to make stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with only-if-cached and max-stale
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            OnlyIfCached = true,
            MaxStale = true,
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 10
              Cache-Control: max-age=1
              Warning: 110 - "Response is Stale"
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: stale-cached
            """);
    }

    [Fact]
    public async Task WhenOnlyIfCachedWithStaleWithoutMaxStaleThen504()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-cached", ("Cache-Control", "max-age=1"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: stale-cached
            """);

        // Advance time to make stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with only-if-cached but no max-stale
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            OnlyIfCached = true,
        };
        await context.SnapshotResponse(request, """
            StatusCode: 504 (GatewayTimeout)
            Content:
              Headers:
                Content-Length: 0
              Value:
            """);
    }
}
