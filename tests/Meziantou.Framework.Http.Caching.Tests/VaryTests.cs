using System.Net;
using HttpCaching.Tests.Internals;

namespace HttpCaching.Tests;

/// <summary>
/// Tests for Vary header handling (RFC 7231 Section 7.1.4, RFC 7234 Section 4.1).
/// </summary>
public sealed class VaryHeaderTests
{
    [Fact]
    public async Task WhenVaryHeaderMatchesThenUsesCache()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "en-content", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request1.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: en-content
            """);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request2.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: en-content
            """);
    }

    [Fact]
    public async Task WhenVaryHeaderDiffersThenfetchesNewResponse()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "en-content", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));
        context.AddResponse(HttpStatusCode.OK, "fr-content", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request1.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: en-content
            """);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request2.Headers.Add("Accept-Language", "fr-FR");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: fr-content
            """);
    }

    [Fact]
    public async Task WhenMultipleVaryHeadersThenAllMustMatch()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "json-en", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept, Accept-Language"));

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request1.Headers.Add("Accept", "application/json");
        request1.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary:
                - Accept
                - Accept-Language
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: json-en
            """);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request2.Headers.Add("Accept", "application/json");
        request2.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              Vary:
                - Accept
                - Accept-Language
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: json-en
            """);
    }

    [Fact]
    public async Task WhenVaryHeaderOneFieldDiffersThenFetchesNewResponse()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "json-en", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept, Accept-Language"));
        context.AddResponse(HttpStatusCode.OK, "xml-en", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept, Accept-Language"));

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request1.Headers.Add("Accept", "application/json");
        request1.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary:
                - Accept
                - Accept-Language
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: json-en
            """);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request2.Headers.Add("Accept", "application/xml");
        request2.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary:
                - Accept
                - Accept-Language
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: xml-en
            """);
    }

    [Fact]
    public async Task WhenVaryStarThenEachRequestUnique()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "response-1", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "*"));
        context.AddResponse(HttpStatusCode.OK, "response-2", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "*"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: *
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: response-1
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: *
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: response-2
            """);
    }

    [Fact]
    public async Task WhenVaryHeaderAbsentInRequestButPresentInCacheThenFetchesNewResponse()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "with-encoding", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Encoding"));
        context.AddResponse(HttpStatusCode.OK, "without-encoding", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Encoding"));

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request1.Headers.Add("Accept-Encoding", "gzip");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Encoding
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: with-encoding
            """);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        // No Accept-Encoding header
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Encoding
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: without-encoding
            """);
    }

    [Fact]
    public async Task WhenVaryHeaderCaseInsensitiveThenMatches()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request1.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request2.Headers.Add("accept-language", "en-US");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenVaryHeaderValueOrderDiffersThenStillMatches()
    {
        using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content", 
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request1.Headers.Add("Accept-Language", "en-US, fr-FR");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request2.Headers.Add("Accept-Language", "fr-FR, en-US");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }
}
