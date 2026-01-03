using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class RequestValidationTests
{
    [Fact]
    public async Task WhenMinFreshRequirementNotMetThenRevalidate()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", ("Cache-Control", "max-age=10"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=10
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
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
              Cache-Control: max-age=10
              ETag: "v1"
              Age: 5
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenMinFreshRequirementMetThenCachedResponseServed()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
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
              Cache-Control: max-age=100
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenRequestMaxAgeExceededThenRevalidate()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", ("Cache-Control", "max-age=100"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
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
              Cache-Control: max-age=100
              ETag: "v1"
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestMaxAgeWithinLimitThenCachedResponseServed()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
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
              Cache-Control: max-age=100
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenOnlyIfCachedWithFreshResponseThenServed()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
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
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);
    }

    [Fact]
    public async Task WhenOnlyIfCachedWithStaleAndMaxStaleThenServed()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-cached", ("Cache-Control", "max-age=1"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
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
              Cache-Control: max-age=1
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: stale-cached
            """);
    }

    [Fact]
    public async Task WhenOnlyIfCachedWithStaleWithoutMaxStaleThen504()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-cached", ("Cache-Control", "max-age=1"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
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
