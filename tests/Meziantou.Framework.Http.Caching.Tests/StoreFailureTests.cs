using System.Net;
using Meziantou.Framework.Http.Caching.InMemory;

namespace Meziantou.Framework.Http.Caching.Tests;

/// <summary>Tests that a failing <see cref="IHttpCacheStore"/> degrades to no caching, not to a failed request.</summary>
public sealed class StoreFailureTests
{
    [Fact]
    public async Task WhenSetEntryThrowsThenTheOriginResponseIsStillReturned()
    {
        var errors = new List<Exception>();
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new ThrowingStore(failWrites: true), new HttpCachingOptions { OnStoreError = errors.Add });
        using var client = new HttpClient(handler);

        var body = await client.GetStringAsync("http://example.com/resource", CancellationToken.None);

        Assert.Equal("from-origin", body);
        Assert.Single(errors);
        Assert.IsType<IOException>(errors[0]);
    }

    [Fact]
    public async Task WhenGetEntriesThrowsThenTheRequestGoesToTheOrigin()
    {
        var errors = new List<Exception>();
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new ThrowingStore(failReads: true), new HttpCachingOptions { OnStoreError = errors.Add });
        using var client = new HttpClient(handler);

        var body = await client.GetStringAsync("http://example.com/resource", CancellationToken.None);

        Assert.Equal("from-origin", body);
        Assert.Single(errors);
    }

    [Fact]
    public async Task WhenRemoveEntriesThrowsThenTheUnsafeRequestStillSucceeds()
    {
        var errors = new List<Exception>();
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new ThrowingStore(failRemoves: true), new HttpCachingOptions { OnStoreError = errors.Add });
        using var client = new HttpClient(handler);

        using var content = new StringContent("payload");
        using var response = await client.PostAsync("http://example.com/resource", content, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(errors);
    }

    [Fact]
    public async Task WhenTheStoreFailsAndNoCallbackIsSetThenTheRequestStillSucceeds()
    {
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new ThrowingStore(failWrites: true));
        using var client = new HttpClient(handler);

        Assert.Equal("from-origin", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));
    }

    [Fact]
    public async Task WhenTheCallerCancelsThenTheCancellationIsNotReportedAsAStoreFailure()
    {
        var errors = new List<Exception>();
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, new CancellingStore(), new HttpCachingOptions { OnStoreError = errors.Add });
        using var client = new HttpClient(handler);

        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("http://example.com/resource", cancellationTokenSource.Token));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task WhenTheStoreRecoversThenCachingResumes()
    {
        var store = new FlakyStore();
        using var innerHandler = new StubHandler();
        using var handler = new HttpCachingDelegateHandler(innerHandler, store);
        using var client = new HttpClient(handler);

        store.Fail = true;
        Assert.Equal("from-origin", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));

        store.Fail = false;
        Assert.Equal("from-origin", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));

        // The write that followed the recovery landed, so the third request is a hit.
        Assert.Equal(2, innerHandler.Count);
        Assert.Equal("from-origin", await client.GetStringAsync("http://example.com/resource", CancellationToken.None));
        Assert.Equal(2, innerHandler.Count);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int Count { get; private set; }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership is transferred to the caller")]
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("from-origin") };
            response.Headers.TryAddWithoutValidation("Cache-Control", "max-age=3600");
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingStore(bool failReads = false, bool failWrites = false, bool failRemoves = false) : IHttpCacheStore
    {
        public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            if (failReads)
                throw new IOException("store is down");

            return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>([]);
        }

        public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken)
        {
            return failWrites ? throw new IOException("store is down") : ValueTask.CompletedTask;
        }

        public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            return failRemoves ? throw new IOException("store is down") : ValueTask.CompletedTask;
        }
    }

    private sealed class CancellingStore : IHttpCacheStore
    {
        public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>([]);
        }

        public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }

    private sealed class FlakyStore : IHttpCacheStore
    {
        private readonly InMemoryHttpCacheStore _inner = new();

        public bool Fail { get; set; }

        public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            return Fail ? throw new IOException("store is down") : _inner.GetEntriesAsync(primaryKey, cancellationToken);
        }

        public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken)
        {
            return Fail ? throw new IOException("store is down") : _inner.SetEntryAsync(primaryKey, entry, cancellationToken);
        }

        public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
        {
            return Fail ? throw new IOException("store is down") : _inner.RemoveEntriesAsync(primaryKey, cancellationToken);
        }
    }
}
