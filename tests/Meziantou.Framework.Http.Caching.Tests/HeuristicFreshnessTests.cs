using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class HeuristicFreshnessTests
{
    [Fact]
    public async Task WhenLastModifiedPresentWithoutExplicitExpirationThenHeuristicUsed()
    {
        using var context = new HttpTestContext();
        var lastModified = context.TimeProvider.GetUtcNow().AddDays(-10);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("heuristic-content");
        response.Content.Headers.LastModified = lastModified;
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Wed, 22 Dec 1999 00:00:00 GMT
              Value: heuristic-content
            """);

        // Should be cached with heuristic freshness (10% of 10 days = 1 day)
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Wed, 22 Dec 1999 00:00:00 GMT
              Value: heuristic-content
            """);
    }

    [Fact]
    public async Task WhenHeuristicFreshnessExpiredThenRevalidate()
    {
        using var context = new HttpTestContext();
        // Last-Modified 10 seconds ago, heuristic = 1 second
        var lastModified = context.TimeProvider.GetUtcNow().AddSeconds(-10);

        using var response1 = new HttpResponseMessage(HttpStatusCode.OK);
        response1.Content = new StringContent("original");
        response1.Content.Headers.LastModified = lastModified;
        response1.Headers.TryAddWithoutValidation("ETag", "\"v1\"");
        context.AddResponse(response1);
        
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Fri, 31 Dec 1999 23:59:50 GMT
              Value: original
            """);

        // Advance beyond heuristic freshness
        context.TimeProvider.Advance(TimeSpan.FromSeconds(2));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Fri, 31 Dec 1999 23:59:50 GMT
              Value: original
            """);
    }

    [Fact]
    public async Task WhenNoLastModifiedAndNoExplicitExpirationThenNotCached()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "no-freshness-1");
        context.AddResponse(HttpStatusCode.OK, "no-freshness-2");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: no-freshness-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: no-freshness-2
            """);
    }

    [Fact]
    public async Task WhenLastModifiedInFutureThenNoHeuristicFreshness()
    {
        using var context = new HttpTestContext();
        var futureModified = context.TimeProvider.GetUtcNow().AddDays(1);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("future-modified");
        response.Content.Headers.LastModified = futureModified;
        context.AddResponse(response);
        
        context.AddResponse(HttpStatusCode.OK, "second-request");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Sun, 02 Jan 2000 00:00:00 GMT
              Value: future-modified
            """);

        // Should not cache with negative age
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: second-request
            """);
    }

    [Fact]
    public async Task WhenExplicitExpirationPresentThenHeuristicNotUsed()
    {
        using var context = new HttpTestContext();
        var lastModified = context.TimeProvider.GetUtcNow().AddDays(-100);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("explicit-wins");
        response.Content.Headers.LastModified = lastModified;
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=10");
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=10
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Thu, 23 Sep 1999 00:00:00 GMT
              Value: explicit-wins
            """);

        // Should use explicit max-age, not heuristic
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=10
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Thu, 23 Sep 1999 00:00:00 GMT
              Value: explicit-wins
            """);
    }
}
