using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

/// <summary>
/// Tests for RFC 8246 immutable directive, Expires header, heuristic freshness, and edge cases.
/// </summary>
public sealed class AdvancedCachingTests
{
    #region RFC 8246: Immutable Responses

    [Fact]
    public async Task WhenImmutableAndFreshThenBypassesRevalidationEvenWithNoCache()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "immutable-content", 
            ("Cache-Control", "max-age=3600, immutable"),
            ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600, immutable
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: immutable-content
            """);

        // Request with no-cache but resource is immutable and fresh
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600, immutable
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: immutable-content
            """);
    }

    [Fact]
    public async Task WhenImmutableButStaleThenRevalidates()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "immutable-content", 
            ("Cache-Control", "max-age=2, immutable"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2, immutable
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: immutable-content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: max-age=2, immutable
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: immutable-content
            """);
    }

    [Fact]
    public async Task WhenImmutableWithPragmaNoCacheAndFreshThenUsesCache()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "immutable-content", 
            ("Cache-Control", "max-age=3600, immutable"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600, immutable
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: immutable-content
            """);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.Pragma.ParseAdd("no-cache");
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600, immutable
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: immutable-content
            """);
    }

    #endregion

    #region Expires Header (RFC 7234 Section 5.3)

    [Fact]
    public async Task WhenExpiresHeaderThenCachesUntilExpiration()
    {
        await using var context = new HttpTestContext2();
        var expires = context.TimeProvider.GetUtcNow().AddSeconds(5);
        context.AddResponse(HttpStatusCode.OK, "expires-content", 
            ("Expires", expires.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 00:00:05 GMT
              Value: expires-content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 00:00:05 GMT
              Value: expires-content
            """);
    }

    [Fact]
    public async Task WhenExpiresInPastThenStaleImmediately()
    {
        await using var context = new HttpTestContext2();
        var expires = context.TimeProvider.GetUtcNow().AddSeconds(-10);
        context.AddResponse(HttpStatusCode.OK, "expired-content", 
            ("Expires", expires.ToString("R")),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Expires: Fri, 31 Dec 1999 23:59:50 GMT
              Value: expired-content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Expires: Fri, 31 Dec 1999 23:59:50 GMT
              Value: expired-content
            """);
    }

    [Fact]
    public async Task WhenMaxAgeAndExpiresBothPresentThenMaxAgeTakesPrecedence()
    {
        await using var context = new HttpTestContext2();
        var expires = context.TimeProvider.GetUtcNow().AddSeconds(2);
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=10"),
            ("Expires", expires.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=10
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 00:00:02 GMT
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));

        // Max-age=10 should be used, not Expires (2 seconds)
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 5
              Cache-Control: max-age=10
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
                Expires: Sat, 01 Jan 2000 00:00:02 GMT
              Value: content
            """);
    }

    [Fact]
    public async Task WhenExpiresInvalidThenTreatedAsStale()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "invalid-expires", 
            ("Expires", "not-a-date"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Expires: not-a-date
              Value: invalid-expires
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
                Expires: not-a-date
              Value: invalid-expires
            """);
    }

    #endregion

    #region Heuristic Freshness (RFC 7234 Section 4.2.2)

    [Fact]
    public async Task WhenLastModifiedWithoutExplicitFreshnessThenUsesHeuristicCaching()
    {
        await using var context = new HttpTestContext2();
        var lastModified = context.TimeProvider.GetUtcNow().AddDays(-10);
        context.AddResponse(HttpStatusCode.OK, "heuristic-content", 
            ("Last-Modified", lastModified.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Wed, 22 Dec 1999 00:00:00 GMT
              Value: heuristic-content
            """);

        // Should use heuristic caching (typically 10% of Last-Modified age)
        context.TimeProvider.Advance(TimeSpan.FromHours(12));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 43200
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
    public async Task WhenNoExplicitFreshnessAndNoLastModifiedThenDoesNotCacheOrUsesMinimalHeuristic()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "no-cache-info-1");
        context.AddResponse(HttpStatusCode.OK, "no-cache-info-2");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: no-cache-info-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: no-cache-info-2
            """);
    }

    #endregion

    #region Age Header and Calculation (RFC 7234 Section 4.2.3)

    [Fact]
    public async Task WhenResponseHasAgeHeaderThenAddsToCalculatedAge()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=100"),
            ("Age", "50"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 50
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(30));

        // Total age = initial 50 + elapsed 30 = 80
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 80
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenAgeExceedsMaxAgeThenResponseIsStale()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=100"),
            ("Age", "90"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 90
              Cache-Control: max-age=100
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(20));

        // Total age = 90 + 20 = 110, exceeds max-age=100
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 110
              Cache-Control: max-age=100
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    #endregion

    #region Warning Headers (RFC 7234 Section 5.5)

    [Fact]
    public async Task WhenServingStaleResponseThenAddsWarning110()
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
    public async Task WhenRevalidationFailsAndServingStaleThenAddsWarning111()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=2, stale-if-error=60"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.ServiceUnavailable, "error");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2, stale-if-error=60
              ETag: "v1"
            Content:
              Headers:
                Content-Type: text/plain; charset=utf-8
                Content-Length: 7
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: max-age=2, stale-if-error=60
              ETag: "v1"
              Warning: 110 - "Response is Stale", 111 - "Revalidation Failed"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenUsingHeuristicExpirationThenAddsWarning113()
    {
        await using var context = new HttpTestContext2();
        var lastModified = context.TimeProvider.GetUtcNow().AddDays(-10);
        context.AddResponse(HttpStatusCode.OK, "content", ("Last-Modified", lastModified.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Wed, 22 Dec 1999 00:00:00 GMT
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromHours(1));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3600
              Warning: 113 - "Heuristic Expiration"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
                Last-Modified: Wed, 22 Dec 1999 00:00:00 GMT
              Value: content
            """);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task WhenEmptyCacheControlValueThenIgnored()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "response-1", ("Cache-Control", ""));
        context.AddResponse(HttpStatusCode.OK, "response-2", ("Cache-Control", ""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: response-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: response-2
            """);
    }

    [Fact]
    public async Task WhenCacheControlWithWhitespaceOnlyThenIgnored()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "response-1", ("Cache-Control", "   "));
        context.AddResponse(HttpStatusCode.OK, "response-2", ("Cache-Control", "   "));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control:    
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: response-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control:    
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: response-2
            """);
    }

    [Fact]
    public async Task WhenMaxAgeZeroThenMustRevalidate()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=0"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"v1\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=0
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenResponseTooLargeThenNotCached()
    {
        // Implementation-specific: test if there's a size limit
        await using var context = new HttpTestContext2();
        var largeContent = new string('x', 10_000_000); // 10MB
        context.AddResponse(HttpStatusCode.OK, largeContent, ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "small-response", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource", $"""
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 10000000
                Content-Type: text/plain; charset=utf-8
              Value: {largeContent}
            """);

        // If size-limited, should fetch again
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 14
                Content-Type: text/plain; charset=utf-8
              Value: small-response
            """);
    }

    [Fact]
    public async Task WhenQueryStringDiffersThenFetchesSeparately()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "query1", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "query2", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource?q=1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: query1
            """);

        await context.SnapshotResponse("http://example.com/resource?q=2", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: query2
            """);
    }

    [Fact]
    public async Task WhenFragmentDiffersThenUsesSameCache()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource#section1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        // Fragments are client-side only, should use same cache
        await context.SnapshotResponse("http://example.com/resource#section2", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    #endregion
}
