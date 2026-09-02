using System.Net;
using Meziantou.Framework.Http.Caching.InMemory;

namespace Meziantou.Framework.Http.Caching.Tests;

/// <summary>Tests for how one entry is selected among the ones stored under a primary key.</summary>
public sealed class CacheEntrySelectionTests
{
    private const string PrimaryKey = "GET http://example.com/resource";

    [Fact]
    public async Task WhenSeveralVariantsAreStoredThenTheMatchingOneIsSelected()
    {
        var store = new FixedStore(
            await CreateEntryAsync("english", ("Accept-Language", "en-US")),
            await CreateEntryAsync("french", ("Accept-Language", "fr-FR")),
            await CreateEntryAsync("german", ("Accept-Language", "de-DE")));

        using var innerHandler = new UnreachableHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, store);
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        request.Headers.Add("Accept-Language", "fr-FR");
        using var response = await client.SendAsync(request, CancellationToken.None);

        Assert.Equal("french", await response.Content.ReadAsStringAsync(CancellationToken.None));
    }

    [Fact]
    public async Task WhenSeveralEntriesMatchThenTheMostRecentByDateIsSelected()
    {
        // The Date offsets stay small so that every entry is still fresh; only their order matters.
        var older = await CreateEntryAsync("older");
        older.ResponseDate = older.ResponseTime - TimeSpan.FromSeconds(10);
        var newer = await CreateEntryAsync("newer");
        newer.ResponseDate = newer.ResponseTime;
        var oldest = await CreateEntryAsync("oldest");
        oldest.ResponseDate = oldest.ResponseTime - TimeSpan.FromSeconds(20);

        // Deliberately not in date order, so the selection cannot come from the enumeration order.
        var store = new FixedStore(older, newer, oldest);

        using var innerHandler = new UnreachableHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, store);
        using var client = new HttpClient(handler);

        Assert.Equal("newer", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheMostRecentEntryIsCorruptedThenTheNextBestIsUsed()
    {
        var corrupted = await CreateEntryAsync("ignored");
        corrupted.ResponseDate = corrupted.ResponseTime;
        corrupted.SerializedResponse = "{ this is not valid json"u8.ToArray();

        var usable = await CreateEntryAsync("fallback");
        usable.ResponseDate = usable.ResponseTime - TimeSpan.FromSeconds(10);

        var store = new FixedStore(corrupted, usable);

        using var innerHandler = new UnreachableHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, store);
        using var client = new HttpClient(handler);

        Assert.Equal("fallback", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));
    }

    [Fact]
    public async Task WhenEveryEntryIsCorruptedThenTheRequestGoesToTheOrigin()
    {
        var first = await CreateEntryAsync("ignored");
        first.SerializedResponse = "{ this is not valid json"u8.ToArray();
        var second = await CreateEntryAsync("ignored");
        second.SerializedResponse = "also not json"u8.ToArray();

        var store = new FixedStore(first, second);

        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, store);
        using var client = new HttpClient(handler);

        Assert.Equal("from-origin", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));
    }

    /// <summary>Produces a real stored entry by running one request through the handler.</summary>
    private static async Task<HttpCachePersistenceEntry> CreateEntryAsync(string body, params (string Name, string Value)[] varyHeaders)
    {
        var store = new InMemoryHttpCacheStore();
        using var innerHandler = new SingleResponseHandler(body, varyHeaders);
        using var handler = new HttpCachingDelegateHandler(innerHandler, store);
        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/resource");
        foreach (var (name, value) in varyHeaders)
        {
            request.Headers.Add(name, value);
        }

        using var response = await client.SendAsync(request, CancellationToken.None);
        _ = await response.Content.ReadAsStringAsync(CancellationToken.None);

        return Assert.Single(await store.GetEntriesAsync(PrimaryKey, CancellationToken.None));
    }

    private sealed class SingleResponseHandler(string body, (string Name, string Value)[] varyHeaders) : HttpMessageHandler
    {
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to the caller")]
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            foreach (var (name, _) in varyHeaders)
            {
                response.Headers.TryAddWithoutValidation("Vary", name);
            }

            return Task.FromResult(response);
        }
    }

    private sealed class FixedStore(params HttpCachePersistenceEntry[] entries) : IHttpCacheStore
    {
        public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            var result = string.Equals(primaryKey, PrimaryKey, StringComparison.Ordinal) ? entries : [];
            return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(result);
        }

        public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("The request should have been served from the cache");
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to the caller")]
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("from-origin") };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            return Task.FromResult(response);
        }
    }
}
