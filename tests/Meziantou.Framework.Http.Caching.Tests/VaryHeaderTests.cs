using System.Net;
using Meziantou.Framework.Http.Caching.InMemory;

namespace Meziantou.Framework.Http.Caching.Tests;

/// <summary>Tests for Vary header handling (RFC 7231 Section 7.1.4, RFC 7234 Section 4.1).</summary>
public sealed class VaryHeaderTests
{
    [Fact]
    public async Task WhenVaryHeaderMatchesThenUsesCache()
    {
        await using var context = new HttpTestContext();
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
        await using var context = new HttpTestContext();
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
        await using var context = new HttpTestContext();
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
        await using var context = new HttpTestContext();
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
        await using var context = new HttpTestContext();
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
        await using var context = new HttpTestContext();
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
    public async Task WhenVaryHeaderAbsentInCacheButPresentInRequestThenFetchesNewResponse()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "without-language",
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));
        context.AddResponse(HttpStatusCode.OK, "french",
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));

        // Stored for a request that carried none of the nominated headers.
        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: without-language
            """);

        // RFC 9111 Section 4.1: a nominated field absent from one request and present in the other does not
        // match, so this must not be served the entry above.
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request2.Headers.Add("Accept-Language", "fr-FR");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: french
            """);
    }

    [Fact]
    public async Task WhenVaryHeaderAbsentInBothThenUsesCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "without-language",
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));

        // Absent from both requests is a match, so the second one is a hit.
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: without-language
            """);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: without-language
            """);
    }

    [Fact]
    public async Task WhenSomeVaryHeadersAbsentThenOnlyAnIdenticalRequestMatches()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "first",
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept, Accept-Language"));
        context.AddResponse(HttpStatusCode.OK, "second",
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept, Accept-Language"));

        // Accept present, Accept-Language absent.
        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request1.Headers.Add("Accept", "text/plain");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              Vary:
                - Accept
                - Accept-Language
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: first
            """);

        // Same Accept, but Accept-Language is now present: not a match.
        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request2.Headers.Add("Accept", "text/plain");
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
              Value: second
            """);
    }

    [Fact]
    public async Task WhenVaryStarThenTheResponseIsNotStored()
    {
        var store = new InMemoryHttpCacheStore();
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, store);
        using var client = new HttpClient(handler);

        using var response = await client.GetAsync("http://example.com/resource", CancellationToken.None);
        _ = await response.Content.ReadAsStringAsync(CancellationToken.None);

        // RFC 9111 Section 4.1: an entry stored under "Vary: *" can never be selected for any request, so
        // storing it only takes up room that nothing will ever reclaim.
        Assert.Empty(await store.GetEntriesAsync("GET http://example.com/resource", CancellationToken.None));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to the caller")]
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("content") };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            response.Headers.TryAddWithoutValidation("Vary", "*");
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task WhenVaryHeaderCaseInsensitiveThenMatches()
    {
        await using var context = new HttpTestContext();
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
    public async Task WhenVaryHeaderValueOrderDiffersThenFetchesNewResponse()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content",
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"));
        context.AddResponse(HttpStatusCode.OK, "different-content",
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
              Cache-Control: max-age=3600
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 17
                Content-Type: text/plain; charset=utf-8
              Value: different-content
            """);
    }
}
