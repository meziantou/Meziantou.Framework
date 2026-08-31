using System.Collections.Concurrent;
using System.Text.Json;

namespace Meziantou.Framework.Http.Caching.InMemory;

/// <summary>
/// Stores HTTP cache entries in-memory and can persist them to a JSON file.
/// </summary>
public sealed class InMemoryHttpCacheStore : IHttpCacheStore
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<HttpCachePersistenceEntry>> _entries = new(StringComparer.Ordinal);

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

            File.Move(tempFilePath, filePath, overwrite: true);
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

            _entries[primaryKey] = new ConcurrentBag<HttpCachePersistenceEntry>(entries.Select(static entry => entry.Clone()));
        }
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_entries.TryGetValue(primaryKey, out var entries))
            return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(Array.Empty<HttpCachePersistenceEntry>());

        var clonedEntries = entries.Select(static entry => entry.Clone()).ToArray();
        return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(clonedEntries);
    }

    /// <inheritdoc />
    public ValueTask SetEntryAsync(string primaryKey, HttpCachePersistenceEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);
        ArgumentNullException.ThrowIfNull(entry);

        cancellationToken.ThrowIfCancellationRequested();

        _entries.AddOrUpdate(
            primaryKey,
            _ => new ConcurrentBag<HttpCachePersistenceEntry> { entry.Clone() },
            (_, bag) =>
            {
                var newBag = new ConcurrentBag<HttpCachePersistenceEntry>();
                foreach (var existing in bag)
                {
                    if (!HttpCachePersistenceEntry.HasSameSecondaryKey(existing, entry))
                    {
                        newBag.Add(existing);
                    }
                }

                newBag.Add(entry.Clone());
                return newBag;
            });

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

            while (_entries.TryGetValue(primaryKey, out var bag))
            {
                var keptEntries = new List<HttpCachePersistenceEntry>();
                var deletedEntriesForKey = 0;

                foreach (var entry in bag)
                {
                    if (entry.IsObsolete(now))
                    {
                        deletedEntriesForKey++;
                    }
                    else
                    {
                        keptEntries.Add(entry);
                    }
                }

                if (deletedEntriesForKey is 0)
                    break;

                if (keptEntries.Count is 0)
                {
                    if (_entries.TryRemove(new KeyValuePair<string, ConcurrentBag<HttpCachePersistenceEntry>>(primaryKey, bag)))
                    {
                        break;
                    }

                    continue;
                }

                var replacementBag = new ConcurrentBag<HttpCachePersistenceEntry>(keptEntries);
                if (_entries.TryUpdate(primaryKey, replacementBag, bag))
                {
                    break;
                }
            }
        }

        return ValueTask.CompletedTask;
    }
}
