using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class Response304UpdateTests
{
    [Fact]
    public async Task When304ResponseHasNewETagThenCachedETagUpdated()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""));

        using var response304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        response304.Headers.TryAddWithoutValidation("ETag", "\"v2\"");
        response304.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        context.AddResponse(req =>
        {
            return response304;
        });

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

    [Fact]
    public async Task When304ResponseHasNewLastModifiedThenCachedLastModifiedUpdated()
    {
        using var context = new HttpTestContext();
        var oldLastModified = context.TimeProvider.GetUtcNow().AddDays(-2);
        var newLastModified = context.TimeProvider.GetUtcNow().AddDays(-1);

        using var response1 = new HttpResponseMessage(HttpStatusCode.OK);
        response1.Content = new StringContent("content");
        response1.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
        response1.Content.Headers.LastModified = oldLastModified;
        context.AddResponse(response1);

        using var response304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        response304.Content = new StringContent("");
        response304.Content.Headers.LastModified = newLastModified;
        response304.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        context.AddResponse(req => response304);

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
              Cache-Control: max-age=0
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
        using var context = new HttpTestContext();
        var oldExpires = context.TimeProvider.GetUtcNow().AddMinutes(1);
        var newExpires = context.TimeProvider.GetUtcNow().AddHours(1);

        context.AddResponse(HttpStatusCode.OK, "content", ("Expires", oldExpires.ToString("R")));

        using var response304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        response304.Headers.TryAddWithoutValidation("Expires", newExpires.ToString("R"));
        context.AddResponse(req => response304);

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
                Content-Length: 0
              Value:
            """);
    }

    [Fact]
    public async Task When304ResponseHasDateHeaderThenResponseDateUpdated()
    {
        using var context = new HttpTestContext();
        var oldDate = context.TimeProvider.GetUtcNow();
        
        using var response1 = new HttpResponseMessage(HttpStatusCode.OK);
        response1.Content = new StringContent("content");
        response1.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
        response1.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
        response1.Headers.Date = oldDate;
        context.AddResponse(response1);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));
        var newDate = context.TimeProvider.GetUtcNow();

        using var response304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        response304.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        response304.Headers.Date = newDate;
        context.AddResponse(req => response304);

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

    [Fact]
    public async Task When304ResponseHasAgeHeaderThenAgeValueUpdated()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""), ("Age", "5"));

        using var response304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        response304.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600");
        response304.Headers.Age = TimeSpan.FromSeconds(20);
        context.AddResponse(req => response304);

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
              Cache-Control: max-age=0
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
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=0"), ("ETag", "\"v1\""));

        using var response304 = new HttpResponseMessage(HttpStatusCode.NotModified);
        // No Cache-Control in 304 response
        context.AddResponse(req => response304);

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
