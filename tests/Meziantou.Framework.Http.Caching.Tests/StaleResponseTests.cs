using System.Net;
using Meziantou.Framework.Http.Caching.InMemory;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.Http.Caching.Tests;

public class StaleResponseTests
{
    [Fact]
    public async Task WhenMaxStaleAllowsAnyStalenessThenStaleResponseServed()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-content", ("Cache-Control", "max-age=1"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: stale-content
            """);

        // Advance time to make response stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with max-stale (no limit) should get stale response
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxStale = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 10
              Cache-Control: max-age=1
              Warning: 110 - "Response is Stale"
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: stale-content
            """);
    }

    [Fact]
    public async Task WhenMaxStaleLimitExceededThenRevalidationRequired()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "original-content", ("Cache-Control", "max-age=1"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: original-content
            """);

        // Advance time beyond max-stale limit
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with max-stale=5 (staleness is 9, exceeds limit)
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue 
        { 
            MaxStale = true,
            MaxStaleLimit = TimeSpan.FromSeconds(5),
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 10
              Cache-Control: max-age=1
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 16
                Content-Type: text/plain; charset=utf-8
              Value: original-content
            """);
    }

    [Fact]
    public async Task WhenMaxStaleWithinLimitThenStaleResponseServed()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-content", ("Cache-Control", "max-age=1"));

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: stale-content
            """);

        // Advance time to make response stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(5));

        // Request with max-stale=10 (staleness is 4, within limit)
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue 
        { 
            MaxStale = true,
            MaxStaleLimit = TimeSpan.FromSeconds(10),
        };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 5
              Cache-Control: max-age=1
              Warning: 110 - "Response is Stale"
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: stale-content
            """);
    }

    [Fact]
    public async Task WhenMustRevalidateThenMaxStaleIgnored()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "must-revalidate", ("Cache-Control", "max-age=1, must-revalidate"), ("ETag", "\"v1\""));
        context.AddResponse(HttpStatusCode.NotModified);

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: must-revalidate, max-age=1
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: must-revalidate
            """);

        // Advance time to make response stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Request with max-stale should still trigger revalidation due to must-revalidate
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { MaxStale = true };
        await context.SnapshotResponse(request, """
            StatusCode: 200 (OK)
            Headers:
              Age: 10
              Cache-Control: must-revalidate, max-age=1
              ETag: "v1"
            Content:
              Headers:
                Content-Length: 15
                Content-Type: text/plain; charset=utf-8
              Value: must-revalidate
            """);
    }

    [Fact]
    public async Task WhenResponseStaleWithoutValidatorThenFetchNew()
    {
        await using var context = new HttpTestContext();
        context.AddResponse(HttpStatusCode.OK, "stale-no-validator", ("Cache-Control", "max-age=1"));
        context.AddResponse(HttpStatusCode.OK, "fresh-content");

        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Headers:
              Cache-Control: max-age=1
            Content:
              Headers:
                Content-Length: 18
                Content-Type: text/plain; charset=utf-8
              Value: stale-no-validator
            """);

        // Advance time to make response stale
        context.TimeProvider.Advance(TimeSpan.FromSeconds(10));

        // Without max-stale, should fetch new (but no validator to revalidate)
        await context.SnapshotResponse("http://example.com/resource", """
            StatusCode: 200 (OK)
            Content:
              Headers:
                Content-Length: 13
                Content-Type: text/plain; charset=utf-8
              Value: fresh-content
            """);
    }

    [Fact]
    public async Task WhenRevalidationThrowsAndStaleIfErrorAllowsItThenStaleResponseServed()
    {
        using var innerHandler = new StubHandler();
        innerHandler.AddResponse(static () => CreateResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=2, stale-if-error=60"), ("ETag", "\"v1\"")));
        innerHandler.AddThrow(static () => new HttpRequestException("origin unreachable"));

        var timeProvider = new FakeTimeProvider();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore(), new HttpCachingOptions { TimeProvider = timeProvider });
        using var client = new HttpClient(handler);

        using var firstResponse = await client.GetAsync("http://example.com/resource", CancellationToken.None);
        Assert.Equal("cached", await firstResponse.Content.ReadAsStringAsync(CancellationToken.None));

        timeProvider.Advance(TimeSpan.FromSeconds(3));

        // The origin cannot be reached at all, which is the case stale-if-error exists for.
        using var secondResponse = await client.GetAsync("http://example.com/resource", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("cached", await secondResponse.Content.ReadAsStringAsync(CancellationToken.None));
        Assert.Equal("110 - \"Response is Stale\", 111 - \"Revalidation Failed\"", string.Join(", ", secondResponse.Headers.GetValues("Warning")));
    }

    [Fact]
    public async Task WhenEntryHasNoValidatorAndRequestThrowsAndStaleIfErrorAllowsItThenStaleResponseServed()
    {
        using var innerHandler = new StubHandler();
        innerHandler.AddResponse(static () => CreateResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=2, stale-if-error=60")));
        innerHandler.AddThrow(static () => new HttpRequestException("origin unreachable"));

        var timeProvider = new FakeTimeProvider();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore(), new HttpCachingOptions { TimeProvider = timeProvider });
        using var client = new HttpClient(handler);

        using var firstResponse = await client.GetAsync("http://example.com/resource", CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        // The entry carries no validator, so the request goes to the origin unconditionally.
        using var secondResponse = await client.GetAsync("http://example.com/resource", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("cached", await secondResponse.Content.ReadAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenStaleIfErrorWindowHasPassedThenTransportFailureIsPropagated()
    {
        using var innerHandler = new StubHandler();
        innerHandler.AddResponse(static () => CreateResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=2, stale-if-error=10"), ("ETag", "\"v1\"")));
        innerHandler.AddThrow(static () => new HttpRequestException("origin unreachable"));

        var timeProvider = new FakeTimeProvider();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore(), new HttpCachingOptions { TimeProvider = timeProvider });
        using var client = new HttpClient(handler);

        using var firstResponse = await client.GetAsync("http://example.com/resource", CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(60));

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://example.com/resource", CancellationToken.None));
    }

    [Fact]
    public async Task WhenCallerCancelsThenStaleResponseIsNotServed()
    {
        using var innerHandler = new StubHandler();
        innerHandler.AddResponse(static () => CreateResponse(HttpStatusCode.OK, "cached", ("Cache-Control", "max-age=2, stale-if-error=60"), ("ETag", "\"v1\"")));
        innerHandler.AddThrow(static () => new TaskCanceledException("canceled"));

        var timeProvider = new FakeTimeProvider();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new InMemoryHttpCacheStore(), new HttpCachingOptions { TimeProvider = timeProvider });
        using var client = new HttpClient(handler);

        using var firstResponse = await client.GetAsync("http://example.com/resource", CancellationToken.None);
        timeProvider.Advance(TimeSpan.FromSeconds(3));

        // A cancellation requested by the caller is not an origin failure and must propagate.
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("http://example.com/resource", cancellationTokenSource.Token));
    }

    [Fact]
    public async Task WhenStoredPayloadIsCorruptedThenTheRequestGoesToTheOrigin()
    {
        var store = new InMemoryHttpCacheStore();
        await store.SetEntryAsync("GET http://example.com/resource", new HttpCachePersistenceEntry
        {
            RequestTime = DateTimeOffset.UnixEpoch,
            ResponseTime = DateTimeOffset.UnixEpoch,
            ResponseDate = DateTimeOffset.UnixEpoch,
            MaxAge = TimeSpan.FromHours(1),
            SerializedResponse = "{ this is not valid json"u8.ToArray(),
        }, CancellationToken.None);

        using var innerHandler = new StubHandler();
        innerHandler.AddResponse(static () => CreateResponse(HttpStatusCode.OK, "from-origin", ("Cache-Control", "max-age=3600")));

        using var handler = new HttpCachingDelegateHandler(innerHandler, store);
        using var client = new HttpClient(handler);

        // A truncated or corrupted payload must be treated as a miss instead of throwing.
        using var response = await client.GetAsync("http://example.com/resource", CancellationToken.None);
        Assert.Equal("from-origin", await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to the caller")]
    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, string? content = null, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(statusCode);
        foreach (var (name, value) in headers)
        {
            response.Headers.TryAddWithoutValidation(name, value);
        }

        if (content is not null)
        {
            response.Content = new StringContent(content);
        }

        return response;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _responses = new();

        public void AddResponse(Func<HttpResponseMessage> responseFactory)
        {
            _responses.Enqueue(responseFactory);
        }

        public void AddThrow(Func<Exception> exceptionFactory)
        {
            _responses.Enqueue(() => throw exceptionFactory());
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responses.Dequeue()());
        }
    }
}
