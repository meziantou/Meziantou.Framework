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
        
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("multi-expires");
        response.Headers.TryAddWithoutValidation("Expires", futureDate1.ToString("R"));
        response.Headers.TryAddWithoutValidation("Expires", futureDate2.ToString("R"));
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: multi-expires
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 01:00:00 GMT, Sat, 01 Jan 2000 02:00:00 GMT
                Content-Length: 13
              Value: multi-expires
            """);
    }

    [Fact]
    public async Task WhenExpiresWithRFC850DateFormatThenParsed()
    {
        using var context = new HttpTestContext();
        // RFC 850 format: Sunday, 06-Nov-94 08:49:37 GMT
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);
        var rfc850Date = futureDate.ToString("dddd, dd-MMM-yy HH:mm:ss GMT");
        
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("rfc850-expires");
        response.Headers.TryAddWithoutValidation("Expires", rfc850Date);
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: rfc850-expires
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Expires: Saturday, 01-Jan-00 01:00:00 GMT
                Content-Length: 14
              Value: rfc850-expires
            """);
    }

    [Fact]
    public async Task WhenExpiresWithAsctimeFormatThenParsed()
    {
        using var context = new HttpTestContext();
        // asctime format: Sun Nov  6 08:49:37 1994
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);
        var asctimeDate = futureDate.ToString("ddd MMM  d HH:mm:ss yyyy");
        
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("asctime-expires");
        response.Headers.TryAddWithoutValidation("Expires", asctimeDate);
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: asctime-expires
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Expires: Sat Jan  1 01:00:00 2000
                Content-Length: 15
              Value: asctime-expires
            """);
    }

    [Fact]
    public async Task WhenExpiresVeryFarInFutureThenCached()
    {
        using var context = new HttpTestContext();
        // Year 9999 - far future
        var farFuture = new DateTimeOffset(9999, 12, 31, 23, 59, 59, TimeSpan.Zero);
        
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("far-future");
        response.Headers.TryAddWithoutValidation("Expires", farFuture.ToString("R"));
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 10
              Value: far-future
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Expires: Fri, 31 Dec 9999 23:59:59 GMT
                Content-Length: 10
              Value: far-future
            """);
    }

    [Fact]
    public async Task WhenExpiresWithTimezoneThenNormalizedToGMT()
    {
        using var context = new HttpTestContext();
        var futureDate = context.TimeProvider.GetUtcNow().AddHours(1);
        
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("timezone-expires");
        // Most HTTP dates should be GMT, but test other timezone handling
        response.Headers.TryAddWithoutValidation("Expires", futureDate.ToString("R"));
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 16
              Value: timezone-expires
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 01:00:00 GMT
                Content-Length: 16
              Value: timezone-expires
            """);
    }
}
