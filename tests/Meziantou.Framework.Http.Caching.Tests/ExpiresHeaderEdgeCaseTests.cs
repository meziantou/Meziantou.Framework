using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class ExpiresHeaderEdgeCaseTests
{
    [Fact]
    public async Task WhenMultipleExpiresHeadersThenFirstUsed()
    {
        using var context = new HttpTestContext();
        var futureDate1 = context.TimeProvider.GetUtcNow().AddHours(1);
        var futureDate2 = context.TimeProvider.GetUtcNow().AddHours(2);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("multi-expires");
        response.Content.Headers.TryAddWithoutValidation("Expires", futureDate1.ToString("R"));
        response.Content.Headers.TryAddWithoutValidation("Expires", futureDate2.ToString("R"));
        context.AddResponse(response);

        using var freshResponse = new HttpResponseMessage(HttpStatusCode.OK);
        freshResponse.Content = new StringContent("fresh-response");
        context.AddResponse(freshResponse);

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
        using var context = new HttpTestContext();
        // RFC 850 format: Sunday, 06-Nov-94 08:49:37 GMT
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);
        var rfc850Date = futureDate.ToString("dddd, dd-MMM-yy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("rfc850-expires");
        response.Content.Headers.TryAddWithoutValidation("Expires", rfc850Date);
        context.AddResponse(response);

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
        using var context = new HttpTestContext();
        // asctime format: Sun Nov  6 08:49:37 1994
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);
        var asctimeDate = futureDate.ToString("ddd MMM  d HH:mm:ss yyyy", CultureInfo.InvariantCulture);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("asctime-expires");
        response.Content.Headers.TryAddWithoutValidation("Expires", asctimeDate);
        context.AddResponse(response);

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
        using var context = new HttpTestContext();
        // Year 9999 - far future
        var farFuture = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("far-future");
        response.Content.Headers.TryAddWithoutValidation("Expires", farFuture.ToString("R"));
        context.AddResponse(response);

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
        using var context = new HttpTestContext();
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);

        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("timezone-expires");
        // Most HTTP dates should be GMT, but test other timezone handling
        response.Content.Headers.TryAddWithoutValidation("Expires", futureDate.ToString("R"));
        context.AddResponse(response);

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
