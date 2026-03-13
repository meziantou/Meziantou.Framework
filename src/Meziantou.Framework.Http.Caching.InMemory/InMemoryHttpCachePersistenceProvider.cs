using System.Collections.Concurrent;
using System.Text.Json;

namespace Meziantou.Framework.Http;

/// <summary>
/// Stores HTTP cache entries in-memory and can persist them to a JSON file.
/// </summary>
public sealed class InMemoryHttpCachePersistenceProvider : IHttpCachePersistenceProvider
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
}
