using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

/// <summary>
/// Tests for conditional requests and revalidation (RFC 7232, RFC 7234 Section 4.3).
/// </summary>
public sealed class ConditionalRequestsAndRevalidationTests
{
    #region ETag-based Revalidation (RFC 7232 Section 2.3 & 3.1)

    [Fact]
    public async Task WhenStaleResponseHasETagThenRevalidatesWithIfNoneMatch()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "max-age=2"),
            ("ETag", "\"abc123\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "\"abc123\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              ETag: "abc123"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: max-age=2
              ETag: "abc123"
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task When304ResponseThenUpdatesStoredHeaders()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=2"),
            ("ETag", "\"v1\""),
            ("X-Custom", "old-value"));
        context.AddResponse(HttpStatusCode.NotModified, 
            ("ETag", "\"v1\""),
            ("Cache-Control", "max-age=10"),
            ("X-Custom", "new-value"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              ETag: "v1"
              X-Custom: old-value
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=10
              ETag: "v1"
              X-Custom: new-value
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task When304ResponseWithoutETagThenUsesStoredResponse()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=2"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified, ("Cache-Control", "max-age=10"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=10
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenETagChangedThenStoresNewResponse()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "v1-content", 
            ("Cache-Control", "max-age=2"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.OK, "v2-content", 
            ("Cache-Control", "max-age=10"),
            ("ETag", "\"v2\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: v1-content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=10
              ETag: "v2"
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: v2-content
            """);
    }

    [Fact]
    public async Task WhenWeakETagThenRevalidatesSuccessfully()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=2"),
            ("ETag", "W/\"weak-tag\""));
        context.AddResponse(HttpStatusCode.NotModified, ("ETag", "W/\"weak-tag\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              ETag: W/"weak-tag"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: max-age=2
              ETag: W/"weak-tag"
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    #endregion

    #region Last-Modified-based Revalidation (RFC 7232 Section 2.2 & 3.3)

    [Fact]
    public async Task WhenStaleResponseHasLastModifiedThenRevalidatesWithIfModifiedSince()
    {
        await using var context = new HttpTestContext2();
        var lastModified = context.TimeProvider.GetUtcNow().AddHours(-1);
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "max-age=2"),
            ("Last-Modified", lastModified.ToString("R")));
        context.AddResponse(HttpStatusCode.NotModified, ("Last-Modified", lastModified.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              Last-Modified: {{lastModified}}
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: max-age=2
              Last-Modified: {{lastModified}}
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    [Fact]
    public async Task WhenBothETagAndLastModifiedThenPrefersETag()
    {
        await using var context = new HttpTestContext2();
        var lastModified = context.TimeProvider.GetUtcNow().AddHours(-1);
        context.AddResponse(HttpStatusCode.OK, "original", 
            ("Cache-Control", "max-age=2"),
            ("ETag", "\"v1\""),
            ("Last-Modified", lastModified.ToString("R")));
        context.AddResponse(HttpStatusCode.NotModified, 
            ("ETag", "\"v1\""),
            ("Last-Modified", lastModified.ToString("R")));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              ETag: "v1"
              Last-Modified: {{lastModified}}
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 3
              Cache-Control: max-age=2
              ETag: "v1"
              Last-Modified: {{lastModified}}
            Content:
              Headers:
                Content-Length: 8
                Content-Type: text/plain; charset=utf-8
              Value: original
            """);
    }

    #endregion

    #region Revalidation Failures

    [Fact]
    public async Task WhenRevalidationReturns200ThenUsesNewResponse()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "old", 
            ("Cache-Control", "max-age=2"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.OK, "new", 
            ("Cache-Control", "max-age=10"),
            ("ETag", "\"v2\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 3
                Content-Type: text/plain; charset=utf-8
              Value: old
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=10
              ETag: "v2"
            Content:
              Headers:
                Content-Length: 3
                Content-Type: text/plain; charset=utf-8
              Value: new
            """);
    }

    [Fact]
    public async Task WhenRevalidationReturns404ThenStoresNewError()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "exists", 
            ("Cache-Control", "max-age=2"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotFound, "gone", ("Cache-Control", "max-age=60"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: exists
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 404 (Not Found)
            Headers:
              Cache-Control: max-age=60
            Content:
              Headers:
                Content-Length: 4
                Content-Type: text/plain; charset=utf-8
              Value: gone
            """);
    }

    [Fact]
    public async Task WhenRevalidationReturns500ThenServesStaleIfAllowed()
    {
        await using var context = new HttpTestContext2();
        context.AddResponse(HttpStatusCode.OK, "cached", 
            ("Cache-Control", "max-age=2, stale-if-error=60"),
            ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.InternalServerError, "error");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2, stale-if-error=60
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=2, stale-if-error=60
              ETag: "v1"
              Age: 3
              Warning: 110 - "Response is Stale", 111 - "Revalidation Failed"
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);
    }

    #endregion
}
