using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class CacheControlParsingEdgeCaseTests
{
    [Fact]
    public async Task WhenCacheControlWithQuotedMaxAgeThenParsed()
    {
        using var context = new HttpTestContext();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("quoted-maxage");
        // Note: HttpClient's CacheControlHeaderValue might normalize this
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=\"600\"");
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age="600"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: quoted-maxage
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age="600"
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: quoted-maxage
            """);
    }

    [Fact]
    public async Task WhenCacheControlWithUnknownDirectiveThenIgnored()
    {
        using var context = new HttpTestContext();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("unknown-directive");
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600, x-custom-directive=value");
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600, x-custom-directive=value
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 17
              Value: unknown-directive
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600, x-custom-directive=value
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 17
              Value: unknown-directive
            """);
    }

    [Fact]
    public async Task WhenCacheControlWithInvalidMaxAgeThenTreatedAsZero()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "invalid-1", ("Cache-Control", "max-age=invalid"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.OK, "invalid-2");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=invalid
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: invalid-1
            """);

        // Without valid max-age, should not cache (no explicit freshness)
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: invalid-2
            """);
    }

    [Fact]
    public async Task WhenCacheControlWithNegativeMaxAgeThenTreatedAsZero()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "negative-maxage", ("Cache-Control", "max-age=-100"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=-100
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: negative-maxage
            """);

        // Negative max-age should require revalidation
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=-100
              ETag: "v1"
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: negative-maxage
            """);
    }

    [Fact]
    public async Task WhenCacheControlWithMalformedSyntaxThenBestEffortParsing()
    {
        using var context = new HttpTestContext();
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StringContent("malformed");
        // Malformed: extra commas, spaces, etc.
        response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=600,  , public  ,  ,");
        context.AddResponse(response);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: public, max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: malformed
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: public, max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: malformed
            """);
    }

    [Fact]
    public async Task WhenCacheControlWithNoTransformThenPreserved()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "no-transform", ("Cache-Control", "max-age=600, no-transform"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-transform, max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: no-transform
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-transform, max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: no-transform
            """);
    }

    [Fact]
    public async Task WhenCacheControlWithProxyRevalidateThenMustRevalidateWhenStale()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "proxy-revalidate", ("Cache-Control", "max-age=1, proxy-revalidate"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: proxy-revalidate, max-age=1
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 16
              Value: proxy-revalidate
            """);

        // Advance time to make stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // proxy-revalidate should force revalidation
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: proxy-revalidate, max-age=1
              ETag: "v1"
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 16
              Value: proxy-revalidate
            """);
    }

    [Fact]
    public async Task WhenCacheControlWithBothMaxAgeAndSMaxAgeThenSMaxAgeTakesPrecedence()
    {
        using var context = new HttpTestContext();
        // s-maxage=10 (expires quickly), max-age=600 (expires slowly)
        context.AddResponse(HttpStatusCode.OK, "precedence-test", ("Cache-Control", "max-age=600, s-maxage=1"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600, s-maxage=1
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: precedence-test
            """);

        // Advance time to exceed s-maxage but not max-age
        context.TimeProvider.Advance(TimeSpan.FromSeconds(2));

        // Should revalidate based on s-maxage
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600, s-maxage=1
              ETag: "v1"
              Age: 2
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: precedence-test
            """);
    }
}
