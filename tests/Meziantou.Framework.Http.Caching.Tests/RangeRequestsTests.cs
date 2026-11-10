using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace Meziantou.Framework.Http.Caching.Tests;

/// <summary>Tests for HTTP Range Requests (RFC 9110 Section 14).</summary>
public sealed class RangeRequestsTests
{
    #region Range Requests with Strong ETags (RFC 7233 Section 3.2)

    [Fact]
    public async Task WhenRangeRequestAndFreshStoredResponseThenRangeIsServedFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("ETag", "\"strong-etag\""),
            ("Accept-Ranges", "bytes"));

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
              Accept-Ranges: bytes
              Age: 0
              Cache-Control: max-age=100
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
    public async Task WhenIfRangeWithStrongETagMatchesThenReturnsPartialContentFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("ETag", "\"strong-etag\""),
            ("Accept-Ranges", "bytes"));

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
              Accept-Ranges: bytes
              Age: 0
              Cache-Control: max-age=100
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
    public async Task WhenRangeRequestWithWeakETagThenRangeIsServedFromCache()
    {
        await using var context = new HttpTestContext();
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
            StatusCode: 206 (PartialContent)
            Headers:
              Accept-Ranges: bytes
              Age: 0
              Cache-Control: max-age=100
              ETag: W/"weak-etag"
            Content:
              Headers:
                Content-Length: 7
                Content-Range: bytes 5-11/17
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenIfRangeWithWeakETagThenCompleteResponseIsServedFromCache()
    {
        await using var context = new HttpTestContext();
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
              Age: 0
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

    #region Ranges served from stored complete responses (RFC 9110 Section 14)

    [Fact]
    public async Task WhenRangeHeaderValueIsSetThenRangeIsServedFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"));
        var httpClient = context.HttpClient;

        // The server answers with a complete response that is fresh for 100 seconds: "full content here"
        using var response = await httpClient.GetAsync("https://example.com/file.txt");

        // The cache serves the range from the stored response, without contacting the server
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "https://example.com/file.txt");
        rangeRequest.Headers.Range = new RangeHeaderValue(5, 11);
        using var rangeResponse = await httpClient.SendAsync(rangeRequest);

        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal("bytes 5-11/17", rangeResponse.Content.Headers.ContentRange?.ToString());
        Assert.Equal("content", await rangeResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task WhenSuffixRangeThenLastBytesAreServedFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=-4");

        await context.SnapshotResponse(rangeRequest, """
            StatusCode: 206 (PartialContent)
            Headers:
              Age: 0
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 4
                Content-Range: bytes 13-16/17
                Content-Type: text/plain; charset=utf-8
              Value: here
            """);
    }

    [Fact]
    public async Task WhenOpenEndedRangeThenRemainingBytesAreServedFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=13-100");

        await context.SnapshotResponse(rangeRequest, """
            StatusCode: 206 (PartialContent)
            Headers:
              Age: 0
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 4
                Content-Range: bytes 13-16/17
                Content-Type: text/plain; charset=utf-8
              Value: here
            """);
    }

    [Fact]
    public async Task WhenRangeIsUnsatisfiableThenRangeNotSatisfiableIsServedFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=100-200");

        await context.SnapshotResponse(rangeRequest, """
            StatusCode: 416 (RequestedRangeNotSatisfiable)
            Headers:
              Age: 0
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 0
                Content-Range: bytes */17
              Value:
            """);
    }

    [Fact]
    public async Task WhenSeveralRangesThenMultipartByteRangesIsServedFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-3, 13-16");

        using var response = await context.HttpClient.SendAsync(rangeRequest, XunitCancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("multipart/byteranges", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(XunitCancellationToken);
        Assert.Contains("Content-Range: bytes 0-3/17", body);
        Assert.Contains("Content-Range: bytes 13-16/17", body);
        Assert.Contains("full", body);
        Assert.Contains("here", body);
    }

    [Fact]
    public async Task WhenOverlappingRangesExceedTheRepresentationThenCompleteResponseIsServedFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=100
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: full content here
            """);

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-15, 1-16");

        using var response = await context.HttpClient.SendAsync(rangeRequest, XunitCancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("full content here", await response.Content.ReadAsStringAsync(XunitCancellationToken));
    }

    [Theory]
    [InlineData("items=0-3")]
    [InlineData("bytes=abc")]
    [InlineData("bytes=5-3")]
    public async Task WhenRangeIsInvalidOrHasAnotherUnitThenCompleteResponseIsServedFromCache(string range)
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"));

        using (var response = await context.HttpClient.GetAsync("http://example.com/resource", XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", range);

        using var rangeResponse = await context.HttpClient.SendAsync(rangeRequest, XunitCancellationToken);
        Assert.Equal(HttpStatusCode.OK, rangeResponse.StatusCode);
        Assert.NotNull(rangeResponse.Headers.Age);
        Assert.Equal("full content here", await rangeResponse.Content.ReadAsStringAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task WhenIfRangeDateMatchesStrongLastModifiedThenRangeIsServedFromCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here",
            ("Cache-Control", "max-age=100"),
            ("Date", "Wed, 01 Jan 2025 01:00:00 GMT"),
            ("Last-Modified", "Wed, 01 Jan 2025 00:00:00 GMT"));

        using (var response = await context.HttpClient.GetAsync("http://example.com/resource", XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var matchingRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        matchingRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-3");
        matchingRequest.Headers.TryAddWithoutValidation("If-Range", "Wed, 01 Jan 2025 00:00:00 GMT");
        using (var response = await context.HttpClient.SendAsync(matchingRequest, XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("full", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        }

        using var otherDateRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        otherDateRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-3");
        otherDateRequest.Headers.TryAddWithoutValidation("If-Range", "Tue, 31 Dec 2024 00:00:00 GMT");
        using (var response = await context.HttpClient.SendAsync(otherDateRequest, XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("full content here", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        }
    }

    [Fact]
    public async Task WhenIfRangeWithoutRangeThenItIsIgnored()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"), ("ETag", "\"v1\""));

        using (var response = await context.HttpClient.GetAsync("http://example.com/resource", XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // RFC 9110 Section 13.1.5: If-Range without Range is ignored, so the stored response answers the request
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.TryAddWithoutValidation("If-Range", "\"v2\"");
        using var cachedResponse = await context.HttpClient.SendAsync(request, XunitCancellationToken);
        Assert.Equal(HttpStatusCode.OK, cachedResponse.StatusCode);
        Assert.NotNull(cachedResponse.Headers.Age);
    }

    [Fact]
    public async Task WhenRangeRequestMissesThenItIsForwardedAndPartialResponseIsNotStored()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(context =>
        {
            Assert.Equal("bytes=0-3", context.Request.Headers.Range.ToString());
            context.Response.StatusCode = 206;
            context.Response.Headers.CacheControl = "max-age=100";
            context.Response.Headers.ContentRange = "bytes 0-3/17";
            return context.Response.WriteAsync("full");
        });
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=100"));

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-3");
        using (var response = await context.HttpClient.SendAsync(rangeRequest, XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
            Assert.Equal("full", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        }

        // The 206 response is not stored, so the complete response comes from the server
        using (var response = await context.HttpClient.GetAsync("http://example.com/resource", XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Null(response.Headers.Age);
            Assert.Equal("full content here", await response.Content.ReadAsStringAsync(XunitCancellationToken));
        }
    }

    [Fact]
    public async Task WhenStoredResponseIsStaleThenRangeIsServedAfterRevalidation()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "full content here", ("Cache-Control", "max-age=1"), ("ETag", "\"v1\""));
        context.AddResponse(context =>
        {
            Assert.Equal("\"v1\"", context.Request.Headers.IfNoneMatch.ToString());
            Assert.Equal("bytes=5-11", context.Request.Headers.Range.ToString());
            context.Response.StatusCode = 304;
            context.Response.Headers.CacheControl = "max-age=100";
            context.Response.Headers.ETag = "\"v1\"";
            return Task.CompletedTask;
        });

        using (var response = await context.HttpClient.GetAsync("http://example.com/resource", XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=5-11");
        using var rangeResponse = await context.HttpClient.SendAsync(rangeRequest, XunitCancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, rangeResponse.StatusCode);
        Assert.Equal("bytes 5-11/17", rangeResponse.Content.Headers.ContentRange?.ToString());
        Assert.Equal("content", await rangeResponse.Content.ReadAsStringAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task WhenStoredResponseIsNotOkThenRangeIsIgnored()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.NotFound, "not found here", ("Cache-Control", "max-age=100"));

        using (var response = await context.HttpClient.GetAsync("http://example.com/resource", XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        using var rangeRequest = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-3");
        using var rangeResponse = await context.HttpClient.SendAsync(rangeRequest, XunitCancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, rangeResponse.StatusCode);
        Assert.Equal("not found here", await rangeResponse.Content.ReadAsStringAsync(XunitCancellationToken));
    }

    [Fact]
    public async Task WhenHeadRequestHasRangeThenStoredHeadResponseIsServed()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, ("Cache-Control", "max-age=100"), ("ETag", "\"v1\""));

        using (var request = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource"))
        using (var response = await context.HttpClient.SendAsync(request, XunitCancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // RFC 9110 Section 14.2: Range only applies to GET
        using var rangeRequest = new HttpRequestMessage(HttpMethod.Head, "http://example.com/resource");
        rangeRequest.Headers.TryAddWithoutValidation("Range", "bytes=0-3");
        using var rangeResponse = await context.HttpClient.SendAsync(rangeRequest, XunitCancellationToken);
        Assert.Equal(HttpStatusCode.OK, rangeResponse.StatusCode);
        Assert.NotNull(rangeResponse.Headers.Age);
    }

    #endregion
}
