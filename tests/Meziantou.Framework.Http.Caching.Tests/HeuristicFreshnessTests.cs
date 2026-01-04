using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class HeuristicFreshnessTests
{
    [Fact]
    public async Task WhenLastModifiedPresentWithoutExplicitExpirationThenHeuristicUsed()
    {
        await using var context = new HttpTestContext2();
        var lastModified = context.TimeProvider.GetUtcNow().AddDays(-10);
        context.AddResponse(HttpStatusCode.OK, "heuristic-content", ("Last-Modified", lastModified.ToString("R")));

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
              Warning: 113 - "Heuristic Expiration"
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
        await using var context = new HttpTestContext2();
        // Last-Modified 10 seconds ago, heuristic = 1 second
        // TODO Validate with 1 second later request
        var lastModified = context.TimeProvider.GetUtcNow().AddSeconds(-10);
        context.AddResponse(HttpStatusCode.OK, "original", ("Last-Modified", lastModified.ToString("R")), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified); // TODO validate the header sent by the client

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
        await using var context = new HttpTestContext2();
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
        await using var context = new HttpTestContext2();
        var futureModified = context.TimeProvider.GetUtcNow().AddDays(1);
        context.AddResponse(HttpStatusCode.OK, "future-modified", ("Last-Modified", futureModified.ToString("R")));
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
        await using var context = new HttpTestContext2();
        var lastModified = context.TimeProvider.GetUtcNow().AddDays(-100);
        context.AddResponse(HttpStatusCode.OK, "explicit-wins", ("Last-Modified", lastModified.ToString("R")), ("Cache-Control", "max-age=10"));

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
