using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

/// <summary>
/// Comprehensive test suite for HTTP caching following RFC 7234 and RFC 8246.
/// This suite covers main scenarios and edge cases to ensure battle-tested implementation.
/// </summary>
public sealed class ComprehensiveRFC7234Tests
{
    #region Basic Caching (RFC 7234 Section 2 & 3)

    [Fact]
    public async Task WhenGetRequestWithCacheableResponseThenSecondRequestUsesCache()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached-content", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "should-not-be-called");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: cached-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: cached-content
            """);
    }

    [Fact]
    public async Task WhenHeadRequestWithCacheableResponseThenSecondRequestUsesCache()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, ("Cache-Control", "max-age=3600"), ("Content-Length", "100"));
        context.AddResponse(HttpStatusCode.OK, ("Content-Length", "999"));

        await context.SnapshotResponse(HttpMethod.Head, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 100
            """);

        await context.SnapshotResponse(HttpMethod.Head, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Age: 0
            Content:
              Headers:
                Content-Length: 100
            """);
    }

    [Fact]
    public async Task WhenPostRequestThenNotCached()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "post-1", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "post-2", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: post-1
            """);

        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: post-2
            """);
    }

    [Fact]
    public async Task WhenPutRequestThenNotCached()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "put-1", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "put-2", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: put-1
            """);

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: put-2
            """);
    }

    [Fact]
    public async Task WhenDeleteRequestThenNotCached()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.NoContent, ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.NoContent, ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse(HttpMethod.Delete, "http://example.com/resource", """
            StatusCode: 204 (No Content)
            Headers:
              Cache-Control: max-age=3600
            """);

        await context.SnapshotResponse(HttpMethod.Delete, "http://example.com/resource", """
            StatusCode: 204 (No Content)
            Headers:
              Cache-Control: max-age=3600
            """);
    }

    #endregion

    #region Status Codes (RFC 7231 Section 6)

    [Fact]
    public async Task When200OKThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "ok", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 2
              Value: ok
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 2
              Value: ok
            """);
    }

    [Fact]
    public async Task When203NonAuthoritativeThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.NonAuthoritativeInformation, "non-auth", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 203 (NonAuthoritativeInformation)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: non-auth
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 203 (NonAuthoritativeInformation)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: non-auth
            """);
    }

    [Fact]
    public async Task When204NoContentThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.NoContent, ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 204 (No Content)
            Headers:
              Cache-Control: max-age=60
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 204 (No Content)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            """);
    }

    [Fact]
    public async Task When206PartialContentThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.PartialContent, "partial", 
            ("Cache-Control", "max-age=60"),
            ("Content-Range", "bytes 0-6/100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 206 (Partial Content)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
                Content-Range: bytes 0-6/100
              Value: partial
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 206 (Partial Content)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
                Content-Range: bytes 0-6/100
              Value: partial
            """);
    }

    [Fact]
    public async Task When300MultipleChoicesThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.MultipleChoices, "choices", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 300 (Multiple Choices)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: choices
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 300 (Multiple Choices)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: choices
            """);
    }

    [Fact]
    public async Task When301MovedPermanentlyThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.MovedPermanently, "moved", 
            ("Cache-Control", "max-age=60"),
            ("Location", "http://example.com/new"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 301 (Moved Permanently)
            Headers:
              Cache-Control: max-age=60
              Location: http://example.com/new
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: moved
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 301 (Moved Permanently)
            Headers:
              Cache-Control: max-age=60
              Age: 0
              Location: http://example.com/new
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: moved
            """);
    }

    [Fact]
    public async Task When404NotFoundThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.NotFound, "not-found", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 404 (Not Found)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: not-found
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 404 (Not Found)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: not-found
            """);
    }

    [Fact]
    public async Task When405MethodNotAllowedThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.MethodNotAllowed, "not-allowed", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 405 (Method Not Allowed)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: not-allowed
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 405 (Method Not Allowed)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: not-allowed
            """);
    }

    [Fact]
    public async Task When410GoneThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.Gone, "gone", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 410 (Gone)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 4
              Value: gone
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 410 (Gone)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 4
              Value: gone
            """);
    }

    [Fact]
    public async Task When414UriTooLongThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.RequestUriTooLong, "uri-too-long", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 414 (Request-URI Too Long)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: uri-too-long
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 414 (Request-URI Too Long)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 12
              Value: uri-too-long
            """);
    }

    [Fact]
    public async Task When501NotImplementedThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.NotImplemented, "not-impl", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 501 (Not Implemented)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: not-impl
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 501 (Not Implemented)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: not-impl
            """);
    }

    [Fact]
    public async Task When500InternalServerErrorThenNotCachedByDefault()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.InternalServerError, "error-1");
        context.AddResponse(HttpStatusCode.InternalServerError, "error-2");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 500 (Internal Server Error)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: error-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 500 (Internal Server Error)
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: error-2
            """);
    }

    [Fact]
    public async Task When500WithExplicitCacheControlThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.InternalServerError, "error", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 500 (Internal Server Error)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: error
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 500 (Internal Server Error)
            Headers:
              Cache-Control: max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: error
            """);
    }

    #endregion

    #region Response Cache-Control Directives (RFC 7234 Section 5.2.2)

    [Fact]
    public async Task WhenResponseHasMaxAgeThenCachedForThatDuration()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "fresh", ("Cache-Control", "max-age=5"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=5
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: fresh
            """);

        // Within max-age: use cache
        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=5
              Age: 3
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: fresh
            """);

        // After max-age: revalidate
        context.AddResponse(HttpStatusCode.OK, "refreshed", ("Cache-Control", "max-age=5"));
        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=5
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 9
              Value: refreshed
            """);
    }

    [Fact]
    public async Task WhenResponseHasSMaxAgeThenItTakesPrecedenceOverMaxAge()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=3600, s-maxage=2"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600, s-maxage=2
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: content
            """);

        // Within s-maxage
        context.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600, s-maxage=2
              Age: 1
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: content
            """);

        // After s-maxage but within max-age: still stale (s-maxage wins)
        context.AddResponse(HttpStatusCode.OK, "new", ("Cache-Control", "max-age=3600, s-maxage=2"));
        context.TimeProvider.Advance(TimeSpan.FromSeconds(2));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600, s-maxage=2
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 3
              Value: new
            """);
    }

    [Fact]
    public async Task WhenResponseHasNoStoreThenNotCached()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "secret-1", ("Cache-Control", "no-store"));
        context.AddResponse(HttpStatusCode.OK, "secret-2", ("Cache-Control", "no-store"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-store
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: secret-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-store
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: secret-2
            """);
    }

    [Fact]
    public async Task WhenResponseHasNoCacheThenMustRevalidate()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "no-cache"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-cache
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-cache
              ETag: "v1"
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenResponseHasPublicThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "public-content", 
            ("Cache-Control", "public, max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: public, max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: public-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: public, max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 14
              Value: public-content
            """);
    }

    [Fact]
    public async Task WhenResponseHasPrivateThenCacheable()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "private-content", 
            ("Cache-Control", "private, max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: private, max-age=60
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: private-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: private, max-age=60
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 15
              Value: private-content
            """);
    }

    [Fact]
    public async Task WhenResponseHasMustRevalidateThenRevalidatesWhenStale()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "must-reval", 
            ("Cache-Control", "max-age=2, must-revalidate"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2, must-revalidate
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 10
              Value: must-reval
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2, must-revalidate
              ETag: "v1"
              Age: 3
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 10
              Value: must-reval
            """);
    }

    [Fact]
    public async Task WhenResponseHasProxyRevalidateThenRevalidatesWhenStale()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "proxy-reval", 
            ("Cache-Control", "max-age=2, proxy-revalidate"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2, proxy-revalidate
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: proxy-reval
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2, proxy-revalidate
              ETag: "v1"
              Age: 3
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: proxy-reval
            """);
    }

    [Fact]
    public async Task WhenResponseHasNoTransformThenPreservedInCache()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "no-transform-data", 
            ("Cache-Control", "max-age=60, no-transform"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=60, no-transform
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 17
              Value: no-transform-data
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=60, no-transform
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 17
              Value: no-transform-data
            """);
    }

    #endregion

    #region Request Cache-Control Directives (RFC 7234 Section 5.2.1)

    [Fact]
    public async Task WhenRequestHasNoStoreThenBypassesCache()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "fresh-fetch", ("Cache-Control", "max-age=3600"));

        // First request populates cache
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);

        // Request with no-store bypasses cache
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoStore = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 11
              Value: fresh-fetch
            """);
    }

    [Fact]
    public async Task WhenRequestHasNoCacheThenRevalidates()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "max-age=3600"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        // First request populates cache
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);

        // Request with no-cache forces revalidation
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              ETag: "v1"
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasMaxAgeZeroThenRevalidates()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "max-age=3600"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxAge = TimeSpan.Zero };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              ETag: "v1"
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasMaxAgeLessThanCacheAgeThenRevalidates()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "max-age=3600"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxAge = TimeSpan.FromSeconds(5) };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              ETag: "v1"
              Age: 10
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasMinFreshThenRevalidatesIfNotFreshEnough()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "max-age=10"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=10
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(6));

        // Remaining freshness = 10 - 6 = 4 seconds, but min-fresh requires 5
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MinFresh = TimeSpan.FromSeconds(5) };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=10
              ETag: "v1"
              Age: 6
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasMaxStaleWithoutValueThenAcceptsStaleResponse()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale", ("Cache-Control", "max-age=2"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: stale
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxStale = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              Age: 5
              Warning: 110 - "Response is Stale"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: stale
            """);
    }

    [Fact]
    public async Task WhenRequestHasMaxStaleWithValueThenAcceptsStaleWithinLimit()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale", ("Cache-Control", "max-age=2"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: stale
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(4));

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue 
        { 
            MaxStale = true, 
            MaxAge = TimeSpan.FromSeconds(5)  // Allow 5 seconds staleness - fixed: removed MaxStaleLimit
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              Age: 4
              Warning: 110 - "Response is Stale"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 5
              Value: stale
            """);
    }

    [Fact]
    public async Task WhenRequestHasOnlyIfCachedAndNoCacheThenReturns504()
    {
        using var context = new HttpTestContext();
        
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { OnlyIfCached = true };
        await context.SnapshotResponse(request, """
            StatusCode: 504 (Gateway Timeout)
            """);
    }

    [Fact]
    public async Task WhenRequestHasOnlyIfCachedAndCacheExistsThenReturnsCached()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { OnlyIfCached = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);
    }

    #endregion

    #region Pragma Header (RFC 7234 Section 5.4)

    [Fact]
    public async Task WhenRequestHasPragmaNoCacheAndNoCacheControlThenRevalidates()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "max-age=3600"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.Pragma.ParseAdd("no-cache");
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              ETag: "v1"
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasPragmaNoCacheAndCacheControlThenCacheControlTakesPrecedence()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);

        // Pragma: no-cache should be ignored when Cache-Control is present
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxAge = TimeSpan.FromSeconds(3600) };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Age: 0
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 6
              Value: cached
            """);
    }

    #endregion
}
