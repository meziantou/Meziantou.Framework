namespace Meziantou.Framework.Http.Caching;

/// <summary>Provides persistent storage for HTTP cache entries.</summary>
public interface IHttpCacheStore
{
    /// <summary>Gets all entries associated with a primary cache key.</summary>
    /// <param name="primaryKey">The primary cache key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken);

    /// <summary>Adds or replaces a cache entry for a primary key.</summary>
    /// <param name="primaryKey">The primary cache key.</param>
    /// <param name="entry">The entry to store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken);

    /// <summary>Removes all entries associated with a primary cache key.</summary>
    /// <param name="primaryKey">The primary cache key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken);

    /// <summary>Removes obsolete entries that are expired and cannot be reused when stale.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    ValueTask PruneObsoleteEntriesAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
