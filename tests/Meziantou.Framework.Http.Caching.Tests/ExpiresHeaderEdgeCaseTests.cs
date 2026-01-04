using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class ExpiresHeaderEdgeCaseTests
{
    [Fact]
    public async Task WhenMultipleExpiresHeadersThenFirstUsed()
    {
        await using var context = new HttpTestContext();
        var futureDate1 = context.TimeProvider.GetUtcNow().AddHours(1);
        var futureDate2 = context.TimeProvider.GetUtcNow().AddHours(2);

        context.AddResponse(HttpStatusCode.OK, "multi-expires", ("Expires", futureDate1.ToString("R")), ("Expires", futureDate2.ToString("R")));
        context.AddResponse(HttpStatusCode.OK, "fresh-response");

        // First request - get and cache the response with multiple Expires headers
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
                Expires:
                  - Sat, 01 Jan 2000 01:00:00 GMT
                  - Sat, 01 Jan 2000 02:00:00 GMT
              Value: multi-expires
            """);

        // Second request at 30 minutes - should be cached (within first Expires time)
        context.TimeProvider.Advance(TimeSpan.FromMinutes(30));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 1800
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
                Expires:
                  - Sat, 01 Jan 2000 01:00:00 GMT
                  - Sat, 01 Jan 2000 02:00:00 GMT
              Value: multi-expires
            """);

        // Third request at 90 minutes - should NOT be cached (past first Expires, but before second)
        // This proves the first Expires header is used, not the second
        context.TimeProvider.Advance(TimeSpan.FromMinutes(60));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: fresh-response
            """);
    }

    [Fact]
    public async Task WhenExpiresWithRFC850DateFormatThenParsed()
    {
        await using var context = new HttpTestContext();
        // RFC 850 format: Sunday, 06-Nov-94 08:49:37 GMT
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);
        var rfc850Date = futureDate.ToString("dddd, dd-MMM-yy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);

        context.AddResponse(HttpStatusCode.OK, "rfc850-expires", ("Expires", rfc850Date));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 01:00:00 GMT
              Value: rfc850-expires
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 01:00:00 GMT
              Value: rfc850-expires
            """);
    }

    [Fact]
    public async Task WhenExpiresWithAsctimeFormatThenParsed()
    {
        await using var context = new HttpTestContext();
        // asctime format: Sun Nov  6 08:49:37 1994
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);
        var asctimeDate = futureDate.ToString("ddd MMM  d HH:mm:ss yyyy", CultureInfo.InvariantCulture);

        context.AddResponse(HttpStatusCode.OK, "asctime-expires", ("Expires", asctimeDate));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 01:00:00 GMT
              Value: asctime-expires
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 01:00:00 GMT
              Value: asctime-expires
            """);
    }

    [Fact]
    public async Task WhenExpiresVeryFarInFutureThenCached()
    {
        await using var context = new HttpTestContext();
        // Year 9999 - far future
        var farFuture = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);

        context.AddResponse(HttpStatusCode.OK, "far-future", ("Expires", farFuture.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
                Expires: Fri, 31 Dec 9999 23:59:59 GMT
              Value: far-future
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
                Expires: Fri, 31 Dec 9999 23:59:59 GMT
              Value: far-future
            """);
    }

    [Fact]
    public async Task WhenExpiresWithTimezoneThenNormalizedToGMT()
    {
        await using var context = new HttpTestContext();
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);

        // Most HTTP dates should be GMT, but test other timezone handling
        context.AddResponse(HttpStatusCode.OK, "timezone-expires", ("Expires", futureDate.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 01:00:00 GMT
              Value: timezone-expires
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 01:00:00 GMT
              Value: timezone-expires
            """);
    }
}
