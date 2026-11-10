using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;

namespace Meziantou.Framework.Http.Caching.InMemory;

/// <summary>
/// Stores HTTP cache entries in-memory and can persist them to a JSON file.
/// </summary>
public sealed class InMemoryHttpCacheStore : IHttpCacheStore
{
    // The value is replaced wholesale on every write, never mutated in place, so an immutable array says
    // what the code actually does. A ConcurrentBag here paid for thread-local storage and work stealing that
    // nothing used, since no two threads ever added to the same instance.
    private readonly ConcurrentDictionary<string, ImmutableArray<HttpCachePersistenceEntry>> _entries = new(StringComparer.Ordinal);

    // Null when the store is unbounded. Otherwise, every change of _entries happens while holding its lock, so that the
    // entries and their accounting stay consistent.
    private readonly LruTracker? _lru;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryHttpCacheStore"/> class with no limit.
    /// </summary>
    public InMemoryHttpCacheStore()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryHttpCacheStore"/> class that evicts the least recently used
    /// entries to stay within <paramref name="limits"/>.
    /// </summary>
    /// <param name="limits">The bounds of the store, or <see langword="null"/> for no limit. The values are read once, by the constructor.</param>
    public InMemoryHttpCacheStore(HttpCacheStoreLimits? limits)
    {
        if (limits is not null && (limits.MaximumEntryCount is not null || limits.MaximumTotalSize is not null))
        {
            _lru = new LruTracker(limits.MaximumEntryCount ?? long.MaxValue, limits.MaximumTotalSize ?? long.MaxValue);
        }
    }

    /// <summary>Gets the number of entries and their total size, or <see langword="null"/> when the store is unbounded.</summary>
    internal (long EntryCount, long TotalSize)? GetUsage()
    {
        if (_lru is null)
            return null;

        lock (_lru.SyncRoot)
        {
            return (_lru.EntryCount, _lru.TotalSize);
        }
    }

    /// <summary>Gets the stored entries without changing their recency.</summary>
    internal Dictionary<string, ImmutableArray<HttpCachePersistenceEntry>> GetSnapshot()
    {
        return _entries.ToDictionary(StringComparer.Ordinal);
    }

    /// <summary>
    /// Saves all in-memory cache entries to a JSON file.
    /// </summary>
    /// <param name="filePath">The path of the file to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask SaveToFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = new InMemoryHttpCachePersistenceData();
        foreach (var (primaryKey, entries) in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            snapshot.Entries[primaryKey] = entries.Select(static entry => entry.Clone()).ToList();
        }

        var directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        // The temporary name is unique so that two concurrent saves to the same path do not collide on it.
        var tempFilePath = filePath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, InMemorySerializationContext.Default.InMemoryHttpCachePersistenceData, cancellationToken).ConfigureAwait(false);
            }

            await MoveOverwritingWithRetryAsync(tempFilePath, filePath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Cleaning up must not replace the exception that is being propagated, and the file is already
            // gone on the success path.
            try
            {
                File.Delete(tempFilePath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Replaces the destination, waiting out the concurrent saves that are replacing it at the same time.</summary>
    private static async Task MoveOverwritingWithRetryAsync(string sourceFilePath, string destinationFilePath, CancellationToken cancellationToken)
    {
        // Replacing a file is atomic on Unix, but Windows denies access to the destination while another move is
        // replacing it, so concurrent saves to the same path have to wait for each other instead of failing.
        const int MaxAttempts = 50;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(sourceFilePath, destinationFilePath, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is UnauthorizedAccessException or (IOException and not (FileNotFoundException or DirectoryNotFoundException)))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Loads cache entries from a JSON file and replaces current in-memory entries.
    /// </summary>
    /// <param name="filePath">The path of the file to read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public async ValueTask LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(filePath))
        {
            Clear();
            return;
        }

        InMemoryHttpCachePersistenceData? snapshot;
        try
        {
            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            snapshot = await JsonSerializer.DeserializeAsync(stream, InMemorySerializationContext.Default.InMemoryHttpCachePersistenceData, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            snapshot = null;
        }

        if (_lru is null)
        {
            _entries.Clear();
            if (snapshot?.Entries is null)
                return;

            foreach (var (primaryKey, entries) in snapshot.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _entries[primaryKey] = [.. entries.Select(static entry => entry.Clone())];
            }

            return;
        }

        lock (_lru.SyncRoot)
        {
            _entries.Clear();
            _lru.Clear();
            if (snapshot?.Entries is null)
                return;

            // The file does not record the recency of the entries: they are used in the order of the file
            foreach (var (primaryKey, entries) in snapshot.Entries)
            {
                foreach (var entry in entries)
                {
                    SetEntryCore(primaryKey, entry.Clone(), _lru);
                }
            }

            EvictCore(_lru);
        }
    }

    private void Clear()
    {
        if (_lru is null)
        {
            _entries.Clear();
            return;
        }

        lock (_lru.SyncRoot)
        {
            _entries.Clear();
            _lru.Clear();
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<HttpCachePersistenceEntry> entries;
        if (_lru is null)
        {
            if (!_entries.TryGetValue(primaryKey, out entries))
                return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(Array.Empty<HttpCachePersistenceEntry>());
        }
        else
        {
            lock (_lru.SyncRoot)
            {
                if (!_entries.TryGetValue(primaryKey, out entries))
                    return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(Array.Empty<HttpCachePersistenceEntry>());

                foreach (var entry in entries)
                {
                    _lru.Touch(entry);
                }
            }
        }

        // The entries are handed out as clones: HttpCachePersistenceEntry is public and mutable, so a
        // caller must not be able to reach into what the store holds.
        var clonedEntries = new HttpCachePersistenceEntry[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            clonedEntries[i] = entries[i].Clone();
        }

        return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(clonedEntries);
    }

    /// <inheritdoc />
    public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);
        ArgumentNullException.ThrowIfNull(entry);

        cancellationToken.ThrowIfCancellationRequested();

        var storedEntry = entry.Clone();
        if (_lru is null)
        {
            _entries.AddOrUpdate(
                primaryKey,
                static (_, added) => [added],
                static (_, existing, added) => existing.RemoveAll(candidate => HttpCachePersistenceEntry.HasSameSecondaryKey(candidate, added)).Add(added),
                storedEntry);
        }
        else
        {
            lock (_lru.SyncRoot)
            {
                SetEntryCore(primaryKey, storedEntry, _lru);
                EvictCore(_lru);
            }
        }

        return ValueTask.CompletedTask;
    }

    private void SetEntryCore(string primaryKey, HttpCachePersistenceEntry storedEntry, LruTracker lru)
    {
        if (_entries.TryGetValue(primaryKey, out var existing))
        {
            foreach (var candidate in existing)
            {
                if (HttpCachePersistenceEntry.HasSameSecondaryKey(candidate, storedEntry))
                {
                    lru.Remove(candidate);
                }
            }

            _entries[primaryKey] = existing.RemoveAll(candidate => HttpCachePersistenceEntry.HasSameSecondaryKey(candidate, storedEntry)).Add(storedEntry);
        }
        else
        {
            _entries[primaryKey] = [storedEntry];
        }

        lru.Add(primaryKey, storedEntry);
    }

    // The entry that was just written is the most recently used, so it is evicted last: only when it alone exceeds the limits.
    private void EvictCore(LruTracker lru)
    {
        while (lru.IsOverLimit && lru.TryGetLeastRecentlyUsed(out var primaryKey, out var entry))
        {
            lru.Remove(entry);
            if (_entries.TryGetValue(primaryKey, out var entries))
            {
                var keptEntries = entries.Remove(entry, ReferenceEqualityComparer.Instance);
                if (keptEntries.IsEmpty)
                {
                    _entries.TryRemove(primaryKey, out _);
                }
                else
                {
                    _entries[primaryKey] = keptEntries;
                }
            }
        }
    }

    /// <inheritdoc />
    public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        if (_lru is null)
        {
            _entries.TryRemove(primaryKey, out _);
            return ValueTask.CompletedTask;
        }

        lock (_lru.SyncRoot)
        {
            if (_entries.TryRemove(primaryKey, out var entries))
            {
                foreach (var entry in entries)
                {
                    _lru.Remove(entry);
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Removes expired entries that cannot be reused when stale.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask PruneObsoleteEntriesAsync(CancellationToken cancellationToken = default)
    {
        return PruneObsoleteEntriesAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    /// <summary>
    /// Removes expired entries that cannot be reused when stale.
    /// </summary>
    /// <param name="now">The current time used to evaluate expiration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    public ValueTask PruneObsoleteEntriesAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_lru is not null)
        {
            PruneBoundedEntries(now, _lru, cancellationToken);
            return ValueTask.CompletedTask;
        }

        foreach (var (primaryKey, _) in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (_entries.TryGetValue(primaryKey, out var entries))
            {
                var keptEntries = entries.RemoveAll(entry => entry.IsObsolete(now));
                if (keptEntries.Length == entries.Length)
                    break;

                if (keptEntries.IsEmpty)
                {
                    if (_entries.TryRemove(new KeyValuePair<string, ImmutableArray<HttpCachePersistenceEntry>>(primaryKey, entries)))
                    {
                        break;
                    }

                    continue;
                }

                if (_entries.TryUpdate(primaryKey, keptEntries, entries))
                {
                    break;
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    private void PruneBoundedEntries(DateTimeOffset now, LruTracker lru, CancellationToken cancellationToken)
    {
        foreach (var (primaryKey, _) in _entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (lru.SyncRoot)
            {
                if (!_entries.TryGetValue(primaryKey, out var entries))
                    continue;

                var keptEntries = entries.RemoveAll(entry => entry.IsObsolete(now));
                if (keptEntries.Length == entries.Length)
                    continue;

                foreach (var entry in entries)
                {
                    if (!keptEntries.Contains(entry, ReferenceEqualityComparer.Instance))
                    {
                        lru.Remove(entry);
                    }
                }

                if (keptEntries.IsEmpty)
                {
                    _entries.TryRemove(primaryKey, out _);
                }
                else
                {
                    _entries[primaryKey] = keptEntries;
                }
            }
        }
    }
}
