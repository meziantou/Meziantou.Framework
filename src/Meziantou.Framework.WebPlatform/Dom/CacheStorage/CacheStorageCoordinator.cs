namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The state of the caches of a <see cref="CacheStorageBackend"/> shared by the windows and the workers that use it (on
/// several threads): the handles of the caches used by <c>Cache</c> objects, the matching of the requests
/// (https://w3c.github.io/ServiceWorker/#query-cache) and the batch operations
/// (https://w3c.github.io/ServiceWorker/#batch-cache-operations).
/// </summary>
internal sealed class CacheStorageCoordinator(CacheStorageBackend backend)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Origin, string Name), CacheHandle> _handles = [];
    private readonly Dictionary<long, CacheHandle> _handlesById = [];
    private long _nextHandleId = 1;

    public CacheStorageBackend Backend { get; } = backend;

    /// <summary>https://w3c.github.io/ServiceWorker/#cache-storage-keys</summary>
    public IReadOnlyList<string> Keys(string origin) => Backend.GetCacheNames(origin);

    /// <summary>https://w3c.github.io/ServiceWorker/#cache-storage-has</summary>
    public bool Has(string origin, string name) => Backend.GetCacheNames(origin).Contains(name, StringComparer.Ordinal);

    /// <summary>https://w3c.github.io/ServiceWorker/#cache-storage-open: returns the id of the handle of the cache (created when it does not exist).</summary>
    public long Open(string origin, string name)
    {
        lock (_lock)
        {
            Backend.CreateCache(origin, name);
            if (!_handles.TryGetValue((origin, name), out var handle))
            {
                handle = new CacheHandle(this, _nextHandleId++, origin, name);
                _handles.Add((origin, name), handle);
                _handlesById.Add(handle.Id, handle);
            }

            return handle.Id;
        }
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#cache-storage-delete: the cache is removed from the backend, and the
    /// <c>Cache</c> objects that use it keep its entries in memory.
    /// </summary>
    public bool Delete(string origin, string name)
    {
        lock (_lock)
        {
            if (!Has(origin, name))
                return false;

            if (_handles.Remove((origin, name), out var handle))
            {
                handle.Doom(Backend.GetEntries(origin, name, urlWithoutQuery: null));
            }

            return Backend.DeleteCache(origin, name);
        }
    }

    public CacheHandle GetHandle(long id)
    {
        lock (_lock)
        {
            return _handlesById.TryGetValue(id, out var handle) ? handle : throw new InvalidOperationException($"The cache {id} does not exist");
        }
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#request-matches-cached-item: whether a cached request (and its response) matches
    /// the query.
    /// </summary>
    public static bool RequestMatchesCachedItem(CachedRequest query, CachedRequest request, CachedResponse? response, CacheQueryOptions options)
    {
        if (!options.IgnoreMethod && request.Method != "GET")
            return false;

        var queryUrl = RemoveFragment(query.Url);
        var cachedUrl = RemoveFragment(request.Url);
        if (options.IgnoreSearch)
        {
            queryUrl = CachedRequest.GetUrlWithoutQuery(queryUrl);
            cachedUrl = CachedRequest.GetUrlWithoutQuery(cachedUrl);
        }

        if (!string.Equals(queryUrl, cachedUrl, StringComparison.Ordinal))
            return false;

        // The headers of an opaque response are the ones of its internal response: the header list of the filtered response is empty
        if (response is null || options.IgnoreVary || response.Type is "opaque" or "opaqueredirect" || HeaderList.GetDecodeAndSplit(response.Headers, "Vary") is not { } fieldValues)
            return true;

        foreach (var fieldValue in fieldValues)
        {
            if (fieldValue == "*")
                return false;

            if (!string.Equals(HeaderList.Get(request.Headers, fieldValue), HeaderList.Get(query.Headers, fieldValue), StringComparison.Ordinal))
                return false;
        }

        return true;

        static string RemoveFragment(string url) => url.IndexOf('#', StringComparison.Ordinal) is var index and >= 0 ? url[..index] : url;
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#query-cache: the matching entries of the cache, in insertion order.</summary>
    public static List<CacheEntry> QueryCache(CacheHandle handle, CachedRequest query, CacheQueryOptions options, IReadOnlyList<CacheEntry>? targetStorage = null)
    {
        var storage = targetStorage ?? handle.GetEntries(query.UrlWithoutQuery);
        return [.. storage.Where(entry => RequestMatchesCachedItem(query, entry.Request, entry.Response, options))];
    }

    /// <summary>
    /// https://w3c.github.io/ServiceWorker/#batch-cache-operations: applies the operations atomically, and returns the
    /// number of entries matched by each operation.
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when two put operations have the same request.</exception>
    /// <exception cref="TypeErrorException">An invalid put operation.</exception>
    public static IReadOnlyList<int> BatchCacheOperations(CacheHandle handle, IReadOnlyList<CacheOperation> operations)
    {
        lock (handle.Lock)
        {
            // The entries of the URLs of the operations, changed in memory, then written at once
            var cache = new Dictionary<string, List<CacheEntry>>(StringComparer.Ordinal);
            var removed = new List<long>();
            var addedItems = new List<CacheEntry>();
            var results = new List<int>();
            foreach (var operation in operations)
            {
                if (QueryCache(handle, operation.Request, operation.Options, addedItems).Count > 0)
                    throw new DomException("The batch has two operations with the same request", "InvalidStateError");

                var urlKey = operation.Request.UrlWithoutQuery;
                List<CacheEntry> GetStorage(string key)
                {
                    if (!cache.TryGetValue(key, out var entries))
                    {
                        entries = [.. handle.GetEntries(key)];
                        cache.Add(key, entries);
                    }

                    return entries;
                }

                if (operation.Response is null)
                {
                    // A delete operation: with ignoreSearch, the entries of the URL with any query
                    var storage = GetStorage(urlKey);
                    var requestResponses = QueryCache(handle, operation.Request, operation.Options, storage);
                    foreach (var entry in requestResponses)
                    {
                        storage.Remove(entry);
                        if (entry.Id != 0)
                        {
                            removed.Add(entry.Id);
                        }
                    }

                    results.Add(requestResponses.Count);
                }
                else
                {
                    var request = operation.Request;
                    if (!request.Url.StartsWith("http:", StringComparison.Ordinal) && !request.Url.StartsWith("https:", StringComparison.Ordinal))
                        throw new TypeErrorException("The request URL must be an HTTP(S) URL");

                    if (request.Method != "GET")
                        throw new TypeErrorException("The request method must be GET");

                    var storage = GetStorage(urlKey);
                    var requestResponses = QueryCache(handle, request, new CacheQueryOptions(), storage);
                    foreach (var entry in requestResponses)
                    {
                        storage.Remove(entry);
                        if (entry.Id != 0)
                        {
                            removed.Add(entry.Id);
                        }
                        else
                        {
                            addedItems.Remove(entry);
                        }
                    }

                    var added = new CacheEntry { Request = request, Response = operation.Response };
                    storage.Add(added);
                    addedItems.Add(added);
                    results.Add(requestResponses.Count);
                }
            }

            handle.ApplyBatch(removed, addedItems);
            return results;
        }
    }
}

/// <summary>The options of a query (https://w3c.github.io/ServiceWorker/#dictdef-cachequeryoptions).</summary>
internal readonly record struct CacheQueryOptions(bool IgnoreSearch = false, bool IgnoreMethod = false, bool IgnoreVary = false);

/// <summary>A cache batch operation (https://w3c.github.io/ServiceWorker/#dfn-cache-batch-operation): a delete operation has no response.</summary>
internal sealed record CacheOperation(CachedRequest Request, CachedResponse? Response, CacheQueryOptions Options);

/// <summary>
/// A cache used by <c>Cache</c> objects (https://w3c.github.io/ServiceWorker/#dfn-relevant-request-response-list): the
/// cache of the backend, or the entries kept in memory when the cache is deleted.
/// </summary>
internal sealed class CacheHandle(CacheStorageCoordinator coordinator, long id, string origin, string name)
{
    private List<CacheEntry>? _doomedEntries;
    private long _nextDoomedId;

    public Lock Lock { get; } = new();

    public long Id { get; } = id;

    public string Origin { get; } = origin;

    public string Name { get; } = name;

    /// <summary>Keeps the entries in memory: the cache is deleted from the backend.</summary>
    public void Doom(IReadOnlyList<CacheEntry> entries)
    {
        lock (Lock)
        {
            _doomedEntries = [.. entries];
            _nextDoomedId = entries.Count == 0 ? 1 : entries.Max(entry => entry.Id) + 1;
        }
    }

    /// <summary>Returns the entries, or the entries whose request URL without query is <paramref name="urlWithoutQuery"/>.</summary>
    public IReadOnlyList<CacheEntry> GetEntries(string? urlWithoutQuery)
    {
        lock (Lock)
        {
            if (_doomedEntries is { } entries)
                return urlWithoutQuery is null ? [.. entries] : [.. entries.Where(entry => entry.Request.UrlWithoutQuery == urlWithoutQuery)];
        }

        return coordinator.Backend.GetEntries(Origin, Name, urlWithoutQuery);
    }

    public void ApplyBatch(IReadOnlyList<long> removed, IReadOnlyList<CacheEntry> added)
    {
        lock (Lock)
        {
            if (_doomedEntries is { } entries)
            {
                entries.RemoveAll(entry => removed.Contains(entry.Id));
                foreach (var entry in added)
                {
                    entries.Add(new CacheEntry { Id = _nextDoomedId++, Request = entry.Request, Response = entry.Response });
                }

                return;
            }
        }

        coordinator.Backend.ApplyBatch(Origin, Name, removed, added);
    }
}
