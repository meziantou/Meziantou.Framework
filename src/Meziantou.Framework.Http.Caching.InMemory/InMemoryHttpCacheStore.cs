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

        var tempFilePath = filePath + ".tmp";
        try
        {
            await using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, InMemorySerializationContext.Default.InMemoryHttpCachePersistenceData, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempFilePath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
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
            _entries.Clear();
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

        _entries.Clear();
        if (snapshot?.Entries is null)
            return;

        foreach (var (primaryKey, entries) in snapshot.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _entries[primaryKey] = [.. entries.Select(static entry => entry.Clone())];
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_entries.TryGetValue(primaryKey, out var entries))
            return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(Array.Empty<HttpCachePersistenceEntry>());

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
        _entries.AddOrUpdate(
            primaryKey,
            static (_, added) => [added],
            static (_, existing, added) => existing.RemoveAll(candidate => HttpCachePersistenceEntry.HasSameSecondaryKey(candidate, added)).Add(added),
            storedEntry);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        _entries.TryRemove(primaryKey, out _);
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
}
