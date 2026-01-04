using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

public class UrlHandlingTests
{
    [Fact]
    public async Task WhenSchemeIsDifferentThenCacheDoesNotMatch()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "http-content", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "https-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: http-content
            """);

        await context.SnapshotResponse("https://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: https-content
            """);
    }

    [Fact]
    public async Task WhenSchemeCasingDiffersThenCacheMatches()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: cached-content
            """);

        await context.SnapshotResponse("HTTP://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: cached-content
            """);

        await context.SnapshotResponse("HtTp://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: cached-content
            """);
    }

    [Fact]
    public async Task WhenHostCasingDiffersThenCacheMatches()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://Example.COM/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: cached-content
            """);

        await context.SnapshotResponse("http://EXAMPLE.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: cached-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: cached-content
            """);
    }

    [Fact]
    public async Task WhenPathCasingDiffersThenCacheDoesNotMatch()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "lowercase-path", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "uppercase-path", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: lowercase-path
            """);

        await context.SnapshotResponse("http://example.com/Resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: uppercase-path
            """);

        // Second request with same casing should be cached
        await context.SnapshotResponse("http://example.com/Resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: uppercase-path
            """);
    }

    [Fact]
    public async Task WhenQueryStringCasingDiffersThenCacheDoesNotMatch()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "query-lower", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "query-upper", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/api?key=value", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: query-lower
            """);

        await context.SnapshotResponse("http://example.com/api?KEY=VALUE", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: query-upper
            """);
    }

    [Fact]
    public async Task WhenFragmentDiffersThenCacheMatches()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-resource", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/page#section1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: cached-resource
            """);

        await context.SnapshotResponse("http://example.com/page#section2", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: cached-resource
            """);

        await context.SnapshotResponse("http://example.com/page", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: cached-resource
            """);
    }

    [Fact]
    public async Task WhenPortIsDifferentThenCacheDoesNotMatch()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "port-80", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "port-8080", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com:80/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: port-80
            """);

        await context.SnapshotResponse("http://example.com:8080/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: port-8080
            """);
    }

    [Fact]
    public async Task WhenDefaultPortIsExplicitThenCacheMatches()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "default-port", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: default-port
            """);

        await context.SnapshotResponse("http://example.com:80/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: default-port
            """);
    }

    [Fact]
    public async Task WhenUserInfoDiffersThenCacheDoesNotMatch()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "user1-content", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "user2-content", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "no-user-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://user1:pass@example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: user1-content
            """);

        await context.SnapshotResponse("http://user2:pass@example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: user2-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: no-user-content
            """);
    }

    [Fact]
    public async Task WhenQueryParameterOrderDiffersThenCacheDoesNotMatch()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "order-1", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "order-2", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/api?a=1&b=2", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: order-1
            """);

        await context.SnapshotResponse("http://example.com/api?b=2&a=1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: order-2
            """);
    }

    [Fact]
    public async Task WhenTrailingSlashDiffersThenCacheDoesNotMatch()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "no-slash", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "with-slash", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/path", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: no-slash
            """);

        await context.SnapshotResponse("http://example.com/path/", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: with-slash
            """);
    }

    [Fact]
    public async Task WhenUrlHasPercentEncodingThenCacheMatchesExactly()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "encoded-space", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "plus-sign", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/hello%20world", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: encoded-space
            """);

        await context.SnapshotResponse("http://example.com/hello+world", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: plus-sign
            """);
    }

    [Fact]
    public async Task WhenUrlHasDotSegmentsThenCacheUsesNormalizedPath()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "normalized-path", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/a/b/c/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: normalized-path
            """);

        await context.SnapshotResponse("http://example.com/a/./b/../b/c/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: normalized-path
            """);
    }

    [Fact]
    public async Task WhenUrlHasEmptyQueryStringThenCacheDoesNotMatchWithoutQueryString()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "no-query", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "empty-query", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: no-query
            """);

        await context.SnapshotResponse("http://example.com/resource?", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: empty-query
            """);
    }

    [Fact]
    public async Task WhenMultiplePathSegmentsWithSameCasingThenCacheMatches()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "deep-path", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/api/v1/users/123", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: deep-path
            """);

        await context.SnapshotResponse("http://example.com/api/v1/users/123", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: deep-path
            """);
    }

    [Fact]
    public async Task WhenIpAddressUsedInsteadOfDomainThenCacheDoesNotMatch()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "domain-content", ("Cache-Control", "max-age=600"));
        context.AddResponse(HttpStatusCode.OK, "ip-content", ("Cache-Control", "max-age=600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: domain-content
            """);

        await context.SnapshotResponse("http://192.168.1.1/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=600
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: ip-content
            """);
    }
}
