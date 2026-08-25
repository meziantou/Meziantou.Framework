using System.Net;

namespace Meziantou.Framework.Http.Caching.Tests;

/// <summary>Tests for the No-Vary-Search response header (draft-ietf-httpbis-no-vary-search).</summary>
public sealed class NoVarySearchTests
{
    [Fact]
    public async Task WhenParameterIsIgnoredThenUsesCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params=(\"utm_source\")"));

        await context.SnapshotResponse("http://example.com/resource?utm_source=a", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=("utm_source")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/resource?utm_source=b", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              No-Vary-Search: params=("utm_source")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenParameterIsIgnoredAndMissingThenUsesCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params=(\"id\")"));

        await context.SnapshotResponse("http://example.com/users?id=345", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/users", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenAnotherParameterDiffersThenFetchesNewResponse()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "first",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params=(\"id\")"));
        context.AddResponse(HttpStatusCode.OK, "second",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params=(\"id\")"));

        await context.SnapshotResponse("http://example.com/users?id=1&sort=name", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: first
            """);

        await context.SnapshotResponse("http://example.com/users?id=2&sort=date", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: second
            """);
    }

    [Fact]
    public async Task WhenKeyOrderIsIgnoredThenUsesCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "key-order"));

        await context.SnapshotResponse("http://example.com/search?a=1&b=2&c=3", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: key-order
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/search?b=2&a=1&c=3", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              No-Vary-Search: key-order
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenKeyOrderIsIgnoredAndParameterIsAddedThenFetchesNewResponse()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "first",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "key-order"));
        context.AddResponse(HttpStatusCode.OK, "second",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "key-order"));

        await context.SnapshotResponse("http://example.com/search?a=1&b=2", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: key-order
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: first
            """);

        await context.SnapshotResponse("http://example.com/search?b=2&a=1&c=3", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: key-order
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: second
            """);
    }

    [Fact]
    public async Task WhenExceptListsTheParameterThenFetchesNewResponse()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "first",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params, except=(\"id\")"));
        context.AddResponse(HttpStatusCode.OK, "second",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params, except=(\"id\")"));

        await context.SnapshotResponse("http://example.com/users?id=1&order=asc", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params, except=("id")
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: first
            """);

        // The order parameter is ignored, so this hits the entry stored for id=1
        await context.SnapshotResponse("http://example.com/users?id=1&order=desc", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              No-Vary-Search: params, except=("id")
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: first
            """);

        await context.SnapshotResponse("http://example.com/users?id=2&order=asc", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params, except=("id")
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: second
            """);
    }

    [Fact]
    public async Task WhenExceptIsUsedWithoutParamsThenAllOtherParametersAreIgnored()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "except=(\"id\")"));

        await context.SnapshotResponse("http://example.com/users?id=1&order=asc&lang=fr", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: except=("id")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        await context.SnapshotResponse("http://example.com/users?lang=en&id=1", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              No-Vary-Search: except=("id")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenQueryIsCanonicallyEquivalentThenUsesCache()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "key-order"));

        await context.SnapshotResponse("http://example.com/resource?a=x", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: key-order
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        // The application/x-www-form-urlencoded parser decodes the escapes and drops the empty sequences
        await context.SnapshotResponse("http://example.com/resource?%61=%78&&&", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              No-Vary-Search: key-order
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenHeaderIsInvalidThenQueryIsComparedExactly()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "first",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params=(not-a-string)"));
        context.AddResponse(HttpStatusCode.OK, "second",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params=(not-a-string)"));

        await context.SnapshotResponse("http://example.com/resource?a=1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=(not-a-string)
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: first
            """);

        await context.SnapshotResponse("http://example.com/resource?a=2", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=(not-a-string)
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: second
            """);
    }

    [Fact]
    public async Task WhenVaryHeaderDoesNotMatchThenFetchesNewResponse()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "en-content",
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"),
            ("No-Vary-Search", "params=(\"id\")"));
        context.AddResponse(HttpStatusCode.OK, "fr-content",
            ("Cache-Control", "max-age=3600"),
            ("Vary", "Accept-Language"),
            ("No-Vary-Search", "params=(\"id\")"));

        using var request1 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/users?id=1");
        request1.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request1, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: en-content
            """);

        using var request2 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/users?id=2");
        request2.Headers.Add("Accept-Language", "fr-FR");
        await context.SnapshotResponse(request2, """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: fr-content
            """);

        using var request3 = new HttpRequestMessage(HttpMethod.Get, "http://example.com/users?id=3");
        request3.Headers.Add("Accept-Language", "en-US");
        await context.SnapshotResponse(request3, """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
              Vary: Accept-Language
            Content:
              Headers:
                Content-Length: 10
                Content-Type: text/plain; charset=utf-8
              Value: en-content
            """);
    }

    [Fact]
    public async Task WhenUnsafeMethodSucceedsThenEntriesAreInvalidated()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "cached",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params=(\"id\")"));
        context.AddResponse(HttpStatusCode.OK, "posted");
        context.AddResponse(HttpStatusCode.OK, "refreshed",
            ("Cache-Control", "max-age=3600"),
            ("No-Vary-Search", "params=(\"id\")"));

        await context.SnapshotResponse("http://example.com/users?id=1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: cached
            """);

        await context.SnapshotResponse(HttpMethod.Post, "http://example.com/users?id=1", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: posted
            """);

        await context.SnapshotResponse("http://example.com/users?id=2", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
              No-Vary-Search: params=("id")
            Content:
              Headers:
                Content-Length: 9
                Content-Type: text/plain; charset=utf-8
              Value: refreshed
            """);
    }

    [Fact]
    public async Task WhenStaleThenRevalidatesTheEntryOfAnEquivalentUrl()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "content",
            ("Cache-Control", "max-age=1"),
            ("ETag", "\"v1\""),
            ("No-Vary-Search", "params=(\"utm_source\")"));
        context.AddNotModifiedResponse(
            [("If-None-Match", "\"v1\"")],
            ("Cache-Control", "max-age=3600"),
            ("ETag", "\"v1\""),
            ("No-Vary-Search", "params=(\"utm_source\")"));

        await context.SnapshotResponse("http://example.com/resource?utm_source=a", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
              ETag: "v1"
              No-Vary-Search: params=("utm_source")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        context.TimeProvider.Advance(TimeSpan.FromSeconds(30));

        await context.SnapshotResponse("http://example.com/resource?utm_source=b", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              ETag: "v1"
              No-Vary-Search: params=("utm_source")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);

        // The revalidated entry is still reachable from any equivalent URL
        await context.SnapshotResponse("http://example.com/resource?utm_source=c", """
            StatusCode: 200 (OK)
            Headers:
              Age: 0
              Cache-Control: max-age=3600
              ETag: "v1"
              No-Vary-Search: params=("utm_source")
            Content:
              Headers:
                Content-Length: 7
                Content-Type: text/plain; charset=utf-8
              Value: content
            """);
    }

    [Fact]
    public async Task WhenHeaderIsAbsentThenQueryIsComparedExactly()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "first", ("Cache-Control", "max-age=3600"));
        context.AddResponse(HttpStatusCode.OK, "second", ("Cache-Control", "max-age=3600"));

        await context.SnapshotResponse("http://example.com/resource?a=1", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 5
                Content-Type: text/plain; charset=utf-8
              Value: first
            """);

        await context.SnapshotResponse("http://example.com/resource?a=2", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=3600
            Content:
              Headers:
                Content-Length: 6
                Content-Type: text/plain; charset=utf-8
              Value: second
            """);
    }
}
