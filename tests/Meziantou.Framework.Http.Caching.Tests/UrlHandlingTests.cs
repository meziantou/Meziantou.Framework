using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class UrlHandlingTests
{
    [Fact]
    public async Task WhenSchemeIsDifferentThenCacheDoesNotMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "http-content", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "https-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: http-content
            """);

        await context.SnapshotResponse("https://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: https-content
            """);
    }

    [Fact]
    public async Task WhenSchemeCasingDiffersThenCacheMatches()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: cached-content
            """);

        await context.SnapshotResponse("HTTP://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: cached-content
            """);

        await context.SnapshotResponse("HtTp://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: cached-content
            """);
    }

    [Fact]
    public async Task WhenHostCasingDiffersThenCacheMatches()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://Example.COM/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: cached-content
            """);

        await context.SnapshotResponse("http://EXAMPLE.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: cached-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: cached-content
            """);
    }

    [Fact]
    public async Task WhenPathCasingDiffersThenCacheDoesNotMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "lowercase-path", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "uppercase-path", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: lowercase-path
            """);

        await context.SnapshotResponse("http://example.com/Resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: uppercase-path
            """);

        // Second request with same casing should be cached
        await context.SnapshotResponse("http://example.com/Resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: uppercase-path
            """);
    }

    [Fact]
    public async Task WhenQueryStringCasingDiffersThenCacheDoesNotMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "query-lower", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "query-upper", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/api?key=value", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: query-lower
            """);

        await context.SnapshotResponse("http://example.com/api?KEY=VALUE", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: query-upper
            """);
    }

    [Fact]
    public async Task WhenFragmentDiffersThenCacheMatches()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-resource", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/page#section1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: cached-resource
            """);

        await context.SnapshotResponse("http://example.com/page#section2", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: cached-resource
            """);

        await context.SnapshotResponse("http://example.com/page", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: cached-resource
            """);
    }

    [Fact]
    public async Task WhenPortIsDifferentThenCacheDoesNotMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "port-80", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "port-8080", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com:80/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: port-80
            """);

        await context.SnapshotResponse("http://example.com:8080/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: port-8080
            """);
    }

    [Fact]
    public async Task WhenDefaultPortIsExplicitThenCacheMatches()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "default-port", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: default-port
            """);

        await context.SnapshotResponse("http://example.com:80/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: default-port
            """);
    }

    [Fact]
    public async Task WhenUserInfoDiffersThenCacheDoesNotMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "user1-content", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "user2-content", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "no-user-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://user1:pass@example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: user1-content
            """);

        await context.SnapshotResponse("http://user2:pass@example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: user2-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: no-user-content
            """);
    }

    [Fact]
    public async Task WhenQueryParameterOrderDiffersThenCacheDoesNotMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "order-1", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "order-2", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/api?a=1&b=2", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: order-1
            """);

        await context.SnapshotResponse("http://example.com/api?b=2&a=1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: order-2
            """);
    }

    [Fact]
    public async Task WhenTrailingSlashDiffersThenCacheDoesNotMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "no-slash", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "with-slash", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/path", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: no-slash
            """);

        await context.SnapshotResponse("http://example.com/path/", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 10
              Value: with-slash
            """);
    }

    [Fact]
    public async Task WhenUrlHasPercentEncodingThenCacheMatchesExactly()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "encoded-space", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "plus-sign", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/hello%20world", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 13
              Value: encoded-space
            """);

        await context.SnapshotResponse("http://example.com/hello+world", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: plus-sign
            """);
    }

    [Fact]
    public async Task WhenUrlHasDotSegmentsThenCacheUsesNormalizedPath()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "normalized-path", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/a/b/c/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: normalized-path
            """);

        await context.SnapshotResponse("http://example.com/a/./b/../b/c/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: normalized-path
            """);
    }

    [Fact]
    public async Task WhenUrlHasEmptyQueryStringThenCacheDoesNotMatchWithoutQueryString()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "no-query", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "empty-query", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: no-query
            """);

        await context.SnapshotResponse("http://example.com/resource?", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: empty-query
            """);
    }

    [Fact]
    public async Task WhenMultiplePathSegmentsWithSameCasingThenCacheMatches()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "deep-path", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/api/v1/users/123", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: deep-path
            """);

        await context.SnapshotResponse("http://example.com/api/v1/users/123", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: deep-path
            """);
    }

    [Fact]
    public async Task WhenIpAddressUsedInsteadOfDomainThenCacheDoesNotMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "domain-content", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "ip-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: domain-content
            """);

        await context.SnapshotResponse("http://192.168.1.1/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 10
              Value: ip-content
            """);
    }
}
