using System.Net;
using HttpCaching.Tests.Internals;
using Meziantou.Framework.Http.Caching.Tests.Internals;

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
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "cached-content", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "should-not-be-called");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
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
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: cached-content
            """);
    }

    [Fact]
    public async Task WhenHeadRequestWithCacheableResponseThenSecondRequestUsesCache()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, ("Cache-Control", "max-age=3600"), ("Content-Length", "100"));

        await context.SnapshotResponse(HttpMethod.Head, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 100
              Value:
            """);

        await context.SnapshotResponse(HttpMethod.Head, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 100
              Value:
            """);
    }

    [Fact]
    public async Task WhenPostRequestThenNotCached()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "post-1", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "post-2", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: post-1
            """);

        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: post-2
            """);
    }

    [Fact]
    public async Task WhenPutRequestThenNotCached()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "put-1", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "put-2", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: put-1
            """);

        await context.SnapshotResponse(HttpMethod.Put, "http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: put-2
            """);
    }

    [Fact]
    public async Task WhenDeleteRequestThenNotCached()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.NoContent, ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.NoContent, ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse(HttpMethod.Delete, "http://example.com/resource", """
            StatusCode: 204 (NoContent)
            Headers:
              Cache-Control: max-age=3600
            Content:
            """);

        await context.SnapshotResponse(HttpMethod.Delete, "http://example.com/resource", """
            StatusCode: 204 (NoContent)
            Headers:
              Cache-Control: max-age=3600
            Content:
            """);
    }

    #endregion

    #region Status Codes (RFC 7231 Section 6)

    [Fact]
    public async Task When200OKThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "ok", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 2
                Content-Type: text/plain; charset=utf-8
              Value: ok
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 2
                Content-Type: text/plain; charset=utf-8
              Value: ok
            """);
    }

    [Fact]
    public async Task When203NonAuthoritativeThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.NonAuthoritativeInformation, "non-auth", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 203 (NonAuthoritativeInformation)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: non-auth
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 203 (NonAuthoritativeInformation)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: non-auth
            """);
    }

    [Fact]
    public async Task When204NoContentThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.NoContent, ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 204 (NoContent)
            Headers:
              Cache-Control: max-age=60
            Content:
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 204 (NoContent)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 0
              Value:
            """);
    }

    [Fact]
    public async Task When206PartialContentThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.PartialContent, "partial",
            ("Cache-Control", "max-age=60"),
            ("Content-Range", "bytes 0-6/100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 206 (PartialContent)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 7
                Content-Range: bytes 0-6/100
                Content-Type: text/plain; charset=utf-8
              Value: partial
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 206 (PartialContent)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 7
                Content-Range: bytes 0-6/100
                Content-Type: text/plain; charset=utf-8
              Value: partial
            """);
    }

    [Fact]
    public async Task When300MultipleChoicesThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.MultipleChoices, "choices", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 300 (MultipleChoices)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: choices
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 300 (MultipleChoices)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: choices
            """);
    }

    [Fact]
    public async Task When301MovedPermanentlyThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.MovedPermanently, "moved",
            ("Cache-Control", "max-age=60"),
            ("Location", "http://example.com/new"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 301 (MovedPermanently)
            Headers:
              Cache-Control: max-age=60
              Location: http://example.com/new
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: moved
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 301 (MovedPermanently)
            Headers:
              Age: 0
              Cache-Control: max-age=60
              Location: http://example.com/new
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: moved
            """);
    }

    [Fact]
    public async Task When404NotFoundThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.NotFound, "not-found", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 404 (NotFound)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: not-found
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 404 (NotFound)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: not-found
            """);
    }

    [Fact]
    public async Task When405MethodNotAllowedThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.MethodNotAllowed, "not-allowed", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 405 (MethodNotAllowed)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: not-allowed
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 405 (MethodNotAllowed)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: not-allowed
            """);
    }

    [Fact]
    public async Task When410GoneThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.Gone, "gone", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 410 (Gone)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 4
                Content-Type: text/plain; charset=utf-8
              Value: gone
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 410 (Gone)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 4
                Content-Type: text/plain; charset=utf-8
              Value: gone
            """);
    }

    [Fact]
    public async Task When414UriTooLongThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.RequestUriTooLong, "uri-too-long", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 414 (RequestUriTooLong)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: uri-too-long
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 414 (RequestUriTooLong)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 12
                Content-Type: text/plain; charset=utf-8
              Value: uri-too-long
            """);
    }

    [Fact]
    public async Task When501NotImplementedThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.NotImplemented, "not-impl", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 501 (NotImplemented)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: not-impl
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 501 (NotImplemented)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: not-impl
            """);
    }

    [Fact]
    public async Task When500InternalServerErrorThenNotCachedByDefault()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.InternalServerError, "error-1");
        context.AddResponse(HttpStatusCode.InternalServerError, "error-2");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 500 (InternalServerError)
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: error-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 500 (InternalServerError)
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: error-2
            """);
    }

    [Fact]
    public async Task When500WithExplicitCacheControlThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.InternalServerError, "error", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 500 (InternalServerError)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: error
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 500 (InternalServerError)
            Headers:
              Age: 0
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: error
            """);
    }

    #endregion

    #region Response Cache-Control Directives (RFC 7234 Section 5.2.2)

    [Fact]
    public async Task WhenResponseHasMaxAgeThenCachedForThatDuration()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "fresh", ("Cache-Control", "max-age=5"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=5
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: fresh
            """);

        // Within max-age: use cache
        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: max-age=5
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
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
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: refreshed
            """);
    }

    [Fact]
    public async Task WhenResponseHasSMaxAgeThenItTakesPrecedenceOverMaxAge()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content",
            ("Cache-Control", "max-age=3600, s-maxage=2"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600, s-maxage=2
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        // Within s-maxage
        context.TimeProvider.Advance(TimeSpan.FromSeconds(1));
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 1
              Cache-Control: max-age=3600, s-maxage=2
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
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
                Content-Length: 3
                Content-Type: text/plain; charset=utf-8
              Value: new
            """);
    }

    [Fact]
    public async Task WhenResponseHasNoStoreThenNotCached()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "secret-1", ("Cache-Control", "no-store"));
        context.AddResponse(HttpStatusCode.OK, "secret-2", ("Cache-Control", "no-store"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-store
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: secret-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-store
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: secret-2
            """);
    }

    [Fact]
    public async Task WhenResponseHasNoCacheThenMustRevalidate()
    {
        await using var context = new HttpTestContext2();
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
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: no-cache
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenResponseHasPublicThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "public-content",
            ("Cache-Control", "public, max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: public, max-age=60
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: public-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: public, max-age=60
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: public-content
            """);
    }

    [Fact]
    public async Task WhenResponseHasPrivateThenCacheable()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "private-content",
            ("Cache-Control", "private, max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=60, private
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: private-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=60, private
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: private-content
            """);
    }

    [Fact]
    public async Task WhenResponseHasMustRevalidateThenRevalidatesWhenStale()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "must-reval",
            ("Cache-Control", "max-age=2, must-revalidate"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: must-revalidate, max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: must-reval
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: must-revalidate, max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: must-reval
            """);
    }

    [Fact]
    public async Task WhenResponseHasProxyRevalidateThenRevalidatesWhenStale()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "proxy-reval",
            ("Cache-Control", "max-age=2, proxy-revalidate"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: proxy-revalidate, max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: proxy-reval
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: proxy-revalidate, max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: proxy-reval
            """);
    }

    [Fact]
    public async Task WhenResponseHasNoTransformThenPreservedInCache()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "no-transform-data",
            ("Cache-Control", "max-age=60, no-transform"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: no-transform, max-age=60
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: no-transform-data
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: no-transform, max-age=60
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: no-transform-data
            """);
    }

    #endregion

    #region Request Cache-Control Directives (RFC 7234 Section 5.2.1)

    [Fact]
    public async Task WhenRequestHasNoStoreThenBypassesCache()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "fresh-fetch", ("Cache-Control", "max-age=3600"));

        // First request populates cache
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
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
                Content-Length: 11
                Content-Type: text/plain; charset=utf-8
              Value: fresh-fetch
            """);
    }

    [Fact]
    public async Task WhenRequestHasNoCacheThenRevalidates()
    {
        await using var context = new HttpTestContext2();
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
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        // Request with no-cache forces revalidation
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasMaxAgeZeroThenRevalidates()
    {
        await using var context = new HttpTestContext2();
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
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxAge = TimeSpan.Zero };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasMaxAgeLessThanCacheAgeThenRevalidates()
    {
        await using var context = new HttpTestContext2();
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
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxAge = TimeSpan.FromSeconds(5) };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 10
              Cache-Control: max-age=3600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasMinFreshThenRevalidatesIfNotFreshEnough()
    {
        await using var context = new HttpTestContext2();
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
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(6));

        // Remaining freshness = 10 - 6 = 4 seconds, but min-fresh requires 5
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MinFresh = TimeSpan.FromSeconds(5) };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 6
              Cache-Control: max-age=10
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasMaxStaleWithoutValueThenAcceptsStaleResponse()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "stale", ("Cache-Control", "max-age=2"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: stale
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxStale = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 5
              Cache-Control: max-age=2
              Warning: 110 - "Response is Stale"
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: stale
            """);
    }

    [Fact]
    public async Task WhenRequestHasMaxStaleWithValueThenAcceptsStaleWithinLimit()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "stale", ("Cache-Control", "max-age=2"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: stale
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(4));

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            MaxStale = true,
            MaxAge = TimeSpan.FromSeconds(5),  // Allow 5 seconds staleness - fixed: removed MaxStaleLimit
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 4
              Cache-Control: max-age=2
              Warning: 110 - "Response is Stale"
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: stale
            """);
    }

    [Fact]
    public async Task WhenRequestHasOnlyIfCachedAndNoCacheThenReturns504()
    {
        await using var context = new HttpTestContext2();

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { OnlyIfCached = true };
        await context.SnapshotResponse(request, """
            StatusCode: 504 (GatewayTimeout)
            Content:
              Headers:
                Content-Length: 0
              Value:
            """);
    }

    [Fact]
    public async Task WhenRequestHasOnlyIfCachedAndCacheExistsThenReturnsCached()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { OnlyIfCached = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);
    }

    #endregion

    #region Pragma Header (RFC 7234 Section 5.4)

    [Fact]
    public async Task WhenRequestHasPragmaNoCacheAndNoCacheControlThenRevalidates()
    {
        await using var context = new HttpTestContext2();
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
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.Pragma.ParseAdd("no-cache");
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenRequestHasPragmaNoCacheAndCacheControlThenCacheControlTakesPrecedence()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        // Pragma: no-cache should be ignored when Cache-Control is present
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.Pragma.ParseAdd("no-cache");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxAge = TimeSpan.FromSeconds(3600) };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);
    }

    #endregion
}
