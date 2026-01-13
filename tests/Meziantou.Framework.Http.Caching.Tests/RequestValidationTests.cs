using System.Net;

namespace Meziantou.Framework.Http.Caching.Tests;

public class RequestValidationTests
{
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
