using System.Collections.Concurrent;

namespace Meziantou.Framework.Http;

internal sealed class DefaultHttpCachePersistenceProvider : IHttpCachePersistenceProvider
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<HttpCachePersistenceEntry>> _entries = new(StringComparer.Ordinal);

    public ValueTask<IReadOnlyCollection<HttpCachePersistenceEntry>> GetEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_entries.TryGetValue(primaryKey, out var entries))
            return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(Array.Empty<HttpCachePersistenceEntry>());

        var clonedEntries = entries.Select(static entry => entry.Clone()).ToArray();
        return ValueTask.FromResult<IReadOnlyCollection<HttpCachePersistenceEntry>>(clonedEntries);
    }

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

    public ValueTask RemoveEntriesAsync(string primaryKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(primaryKey);

        cancellationToken.ThrowIfCancellationRequested();

        _entries.TryRemove(primaryKey, out _);
        return ValueTask.CompletedTask;
    }
}