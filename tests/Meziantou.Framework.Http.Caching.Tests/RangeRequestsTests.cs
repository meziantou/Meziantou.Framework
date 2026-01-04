using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

/// <summary>
/// Tests for HTTP Range Requests with ETags (RFC 7233).
/// </summary>
public sealed class RangeRequestsTests
{
    #region Range Requests with Strong ETags (RFC 7233 Section 3.2)

    [Fact]
    public async Task WhenRangeRequestWithStrongETagThenIfRangeHeaderSent()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("ETag", "\"strong-etag\""),
            ("Accept-Ranges", "bytes"));
        context.AddResponse(HttpStatusCode.PartialContent, "content",
            ("Content-Range", "bytes 5-11/17"),
            ("ETag", "\"strong-etag\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Accept-Ranges: bytes
              Cache-Control: max-age=100
              ETag: "strong-etag"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=5-11");

        await context.SnapshotResponse(rangeRequest, """
            StatusCode: 206 (PartialContent)
            Headers:
              ETag: "strong-etag"
            Content:
              Headers:
                Content-Length: 7
                Content-Range: bytes 5-11/17
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenIfRangeWithStrongETagMatchesThenReturnsPartialContent()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("ETag", "\"strong-etag\""),
            ("Accept-Ranges", "bytes"));
        context.AddResponse(HttpStatusCode.PartialContent, "content",
            ("Content-Range", "bytes 5-11/17"),
            ("ETag", "\"strong-etag\""));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Accept-Ranges: bytes
              Cache-Control: max-age=100
              ETag: "strong-etag"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=5-11");
        rangeRequest.Headers.TryAddWithoutValidation("If-Range", "\"strong-etag\"");

        await context.SnapshotResponse(rangeRequest, """
            StatusCode: 206 (PartialContent)
            Headers:
              ETag: "strong-etag"
            Content:
              Headers:
                Content-Length: 7
                Content-Range: bytes 5-11/17
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenIfRangeWithStrongETagMismatchesThenReturnsFullContent()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original content",
            ("Cache-Control", "max-age=2"),
            ("ETag", "\"v1\""),
            ("Accept-Ranges", "bytes"));
        context.AddResponse(HttpStatusCode.OK, "updated content!",
            ("Cache-Control", "max-age=100"),
            ("ETag", "\"v2\""),
            ("Accept-Ranges", "bytes"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Accept-Ranges: bytes
              Cache-Control: max-age=2
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: original content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(3));

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=5-11");
        rangeRequest.Headers.TryAddWithoutValidation("If-Range", "\"v1\"");

        await context.SnapshotResponse(rangeRequest, """
            StatusCode: 200 (OK)
            Headers:
              Accept-Ranges: bytes
              Cache-Control: max-age=100
              ETag: "v2"
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: updated content!
            """);
    }

    #endregion

    #region Range Requests with Weak ETags (RFC 7233 Section 3.2)

    [Fact]
    public async Task WhenRangeRequestWithWeakETagThenShouldNotUseIfRange()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("ETag", "W/\"weak-etag\""),
            ("Accept-Ranges", "bytes"));
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("ETag", "W/\"weak-etag\""),
            ("Accept-Ranges", "bytes"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Accept-Ranges: bytes
              Cache-Control: max-age=100
              ETag: W/"weak-etag"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=5-11");

        await context.SnapshotResponse(rangeRequest, """
            StatusCode: 200 (OK)
            Headers:
              Accept-Ranges: bytes
              Cache-Control: max-age=100
              ETag: W/"weak-etag"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);
    }

    [Fact]
    public async Task WhenIfRangeWithWeakETagThenIgnoresIfRange()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("ETag", "W/\"weak-etag\""),
            ("Accept-Ranges", "bytes"));
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("ETag", "W/\"weak-etag\""),
            ("Accept-Ranges", "bytes"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Accept-Ranges: bytes
              Cache-Control: max-age=100
              ETag: W/"weak-etag"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=5-11");
        rangeRequest.Headers.TryAddWithoutValidation("If-Range", "W/\"weak-etag\"");

        await context.SnapshotResponse(rangeRequest, """
            StatusCode: 200 (OK)
            Headers:
              Accept-Ranges: bytes
              Cache-Control: max-age=100
              ETag: W/"weak-etag"
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);
    }

    #endregion
}
