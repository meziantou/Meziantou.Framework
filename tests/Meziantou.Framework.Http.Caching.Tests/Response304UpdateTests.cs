using System.Net;

namespace Meziantou.Framework.Http.Caching.Tests;

public class Response304UpdateTests
{
    [Fact]
    public async Task When304ResponseHasMismatchedETagThenFetchesFullResponse()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=600"), ("ETag", "\"v2\""));
        context.AddResponse(HttpStatusCode.OK, "replacement", ("Cache-Control", "max-age=600"), ("ETag", "\"v2\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              ETag: "v2"
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: replacement
            """);
    }

    [Fact]
    public async Task When304ResponseHasMismatchedLastModifiedThenFetchesFullResponse()
    {
        await using var context = new HttpTestContext();
        var oldLastModified = context.TimeProvider.GetUtcNow().AddDays(-2);
        var newLastModified = context.TimeProvider.GetUtcNow().AddDays(-1);

        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("Last-Modified", oldLastModified.ToString("R")));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=600"), ("Last-Modified", newLastModified.ToString("R")));
        context.AddResponse(HttpStatusCode.OK, "replacement", ("Cache-Control", "max-age=600"), ("Last-Modified", newLastModified.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=0
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Thu, 30 Dec 1999 00:00:00 GMT
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Fri, 31 Dec 1999 00:00:00 GMT
              Value: replacement
            """);
    }

    [Fact]
    public async Task When304ResponseHasNewExpiresThenCachedExpiresUpdated()
    {
        await using var context = new HttpTestContext();
        var oldExpires = context.TimeProvider.GetUtcNow().AddMinutes(1);
        var newExpires = context.TimeProvider.GetUtcNow().AddHours(1);

        context.AddResponse(HttpStatusCode.OK, "content", ("Expires", oldExpires.ToString("R")));
        context.AddResponse(HttpStatusCode.NotModified, ("Expires", newExpires.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 00:01:00 GMT
              Value: content
            """);

        // Advance time to make original expires stale
        context.TimeProvider.Advance(TimeSpan.FromMinutes(2));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 304 (NotModified)
            Content:
              Headers:
                Expires: Sat, 01 Jan 2000 01:00:00 GMT
              Value:
            """);
    }

    [Fact]
    public async Task When304ResponseHasMatchingETagThenReplacesStoredHeaders()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""), ("X-Version", "old"));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=600"), ("ETag", "\"v1\""), ("X-Version", "new"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=0
              ETag: "v1"
              X-Version: old
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
              ETag: "v1"
              X-Version: new
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task When304ResponseHasDateHeaderThenResponseDateUpdated()
    {
        await using var context = new HttpTestContext();
        var oldDate = context.TimeProvider.GetUtcNow();


        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task When304ResponseHasAgeHeaderThenAgeValueUpdated()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""), ("Age", "5"));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=600"), ("Age", "20"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 5
              Cache-Control: max-age=0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 20
              Cache-Control: max-age=600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task When304ResponseDropsImmutableThenTheEntryIsRevalidatedAgain()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=1, immutable"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=600"), ("ETag", "\"v1\""));
        context.AddNotModifiedResponse([("If-None-Match", "\"v1\"")], ("Cache-Control", "max-age=600"), ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1, immutable
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        // Stale, so it revalidates; the 304 replaces the stored Cache-Control, which no longer has immutable.
        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        // RFC 8246: immutable suppresses revalidation for a fresh entry. It is gone now, so a no-cache
        // request revalidates instead of being served straight from the cache.
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task When304ResponseDropsStaleIfErrorThenTheWindowIsGone()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=1, stale-if-error=600"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=1"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.ServiceUnavailable, "unavailable");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1, stale-if-error=600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        // The 304 repeats the same max-age and carries no Date, so the entry keeps accumulating age.
        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 5
              Cache-Control: max-age=1
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        // The stale-if-error window was dropped by the 304, so a failing origin is no longer masked.
        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 503 (ServiceUnavailable)
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: unavailable
            """);
    }

    [Fact]
    public async Task When304ResponseDoesNotHaveCacheControlThenOriginalPreserved()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""));
        // No Cache-Control in 304 response
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }
}
