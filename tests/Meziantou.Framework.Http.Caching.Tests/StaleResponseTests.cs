using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class StaleResponseTests
{
    [Fact]
    public async Task WhenMaxStaleAllowsAnyStalenessThenStaleResponseServed()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-content", ("Cache-Control", "max-age=1"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: stale-content
            """);

        // Advance time to make response stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with max-stale (no limit) should get stale response
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxStale = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: stale-content
            """);
    }

    [Fact]
    public async Task WhenMaxStaleLimitExceededThenRevalidationRequired()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original-content", ("Cache-Control", "max-age=1"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 16
              Value: original-content
            """);

        // Advance time beyond max-stale limit
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with max-stale=5 (staleness is 9, exceeds limit)
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue 
        { 
            MaxStale = true,
            MaxStaleLimit = TimeSpan.FromSeconds(5),
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
              ETag: "v1"
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 16
              Value: original-content
            """);
    }

    [Fact]
    public async Task WhenMaxStaleWithinLimitThenStaleResponseServed()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-content", ("Cache-Control", "max-age=1"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: stale-content
            """);

        // Advance time to make response stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));

        // Request with max-stale=10 (staleness is 4, within limit)
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue 
        { 
            MaxStale = true,
            MaxStaleLimit = TimeSpan.FromSeconds(10),
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
              Age: 5
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: stale-content
            """);
    }

    [Fact]
    public async Task WhenMustRevalidateThenMaxStaleIgnored()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "must-revalidate", ("Cache-Control", "max-age=1, must-revalidate"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: must-revalidate, max-age=1
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: must-revalidate
            """);

        // Advance time to make response stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with max-stale should still trigger revalidation due to must-revalidate
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxStale = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: must-revalidate, max-age=1
              ETag: "v1"
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: must-revalidate
            """);
    }

    [Fact]
    public async Task WhenResponseStaleWithoutValidatorThenFetchNew()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-no-validator", ("Cache-Control", "max-age=1"));
        context.AddResponse(HttpStatusCode.OK, "fresh-content");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 18
              Value: stale-no-validator
            """);

        // Advance time to make response stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Without max-stale, should fetch new (but no validator to revalidate)
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: fresh-content
            """);
    }
}
