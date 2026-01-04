using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class Response304UpdateTests
{
    [Fact]
    public async Task When304ResponseHasNewETagThenCachedETagUpdated()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=600"), ("ETag", "\"v2\""));

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
    public async Task When304ResponseHasNewLastModifiedThenCachedLastModifiedUpdated()
    {
        await using var context = new HttpTestContext2();
        var oldLastModified = context.TimeProvider.GetUtcNow().AddDays(-2);
        var newLastModified = context.TimeProvider.GetUtcNow().AddDays(-1);

        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("Last-Modified", oldLastModified.ToString("R")));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=600"), ("Last-Modified", newLastModified.ToString("R")));

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
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Thu, 30 Dec 1999 00:00:00 GMT
              Value: content
            """);
    }

    [Fact]
    public async Task When304ResponseHasNewExpiresThenCachedExpiresUpdated()
    {
        await using var context = new HttpTestContext2();
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
    public async Task When304ResponseHasDateHeaderThenResponseDateUpdated()
    {
        await using var context = new HttpTestContext2();
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
        await using var context = new HttpTestContext2();
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
    public async Task When304ResponseDoesNotHaveCacheControlThenOriginalPreserved()
    {
        await using var context = new HttpTestContext2();
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
