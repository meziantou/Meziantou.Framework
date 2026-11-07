namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A <see cref="CacheStorageBackend"/> that keeps the caches in memory: the default backend of the browsing contexts (see
/// <see cref="BrowsingContextOptions.CacheStorage"/>). It is thread-safe, and can be shared by several browsing contexts.
/// </summary>
public sealed class InMemoryCacheStorageBackend : CacheStorageBackend
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, OrderedDictionary<string, List<CacheEntry>>> _origins = new(StringComparer.Ordinal);
    private long _nextId = 1;

    /// <inheritdoc/>
    public override IReadOnlyList<string> GetCacheNames(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        lock (_lock)
        {
            return _origins.TryGetValue(origin, out var caches) ? [.. caches.Keys] : [];
        }
    }

    /// <inheritdoc/>
    public override bool CreateCache(string origin, string name)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(name);
        lock (_lock)
        {
            if (!_origins.TryGetValue(origin, out var caches))
            {
                caches = new OrderedDictionary<string, List<CacheEntry>>(StringComparer.Ordinal);
                _origins.Add(origin, caches);
            }

            return caches.TryAdd(name, []);
        }
    }

    /// <inheritdoc/>
    public override bool DeleteCache(string origin, string name)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(name);
        lock (_lock)
        {
            return _origins.TryGetValue(origin, out var caches) && caches.Remove(name);
        }
    }

    /// <inheritdoc/>
    public override IReadOnlyList<CacheEntry> GetEntries(string origin, string name, string? urlWithoutQuery)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(name);
        lock (_lock)
        {
            var entries = GetCache(origin, name);
            return urlWithoutQuery is null ? [.. entries] : [.. entries.Where(entry => entry.Request.UrlWithoutQuery == urlWithoutQuery)];
        }
    }

    /// <inheritdoc/>
    public override void ApplyBatch(string origin, string name, IReadOnlyList<long> removedEntryIds, IReadOnlyList<CacheEntry> addedEntries)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(removedEntryIds);
        ArgumentNullException.ThrowIfNull(addedEntries);
        lock (_lock)
        {
            var entries = GetCache(origin, name);
            var removed = removedEntryIds.ToHashSet();
            entries.RemoveAll(entry => removed.Contains(entry.Id));
            foreach (var entry in addedEntries)
            {
                entries.Add(new CacheEntry { Id = _nextId++, Request = entry.Request, Response = entry.Response });
            }
        }
    }

    /// <inheritdoc/>
    public override long GetUsage(string origin)
    {
        ArgumentNullException.ThrowIfNull(origin);

        // The bodies of the responses, the URLs and the headers, and a fixed size per cache
        const int CacheOverhead = 128;
        lock (_lock)
        {
            if (!_origins.TryGetValue(origin, out var caches))
                return 0;

            long usage = 0;
            foreach (var (name, entries) in caches)
            {
                usage += CacheOverhead + (name.Length * sizeof(char));
                foreach (var entry in entries)
                {
                    usage += entry.Request.Url.Length + GetSize(entry.Request.Headers) + GetSize(entry.Response.Headers) + (entry.Response.Body?.Length ?? 0);
                }
            }

            return usage;
        }

        static long GetSize(IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            long size = 0;
            foreach (var (name, value) in headers)
            {
                size += name.Length + value.Length;
            }

            return size;
        }
    }

    private List<CacheEntry> GetCache(string origin, string name)
        => _origins.TryGetValue(origin, out var caches) && caches.TryGetValue(name, out var entries) ? entries : throw new InvalidOperationException($"The cache '{name}' does not exist");
}
