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
