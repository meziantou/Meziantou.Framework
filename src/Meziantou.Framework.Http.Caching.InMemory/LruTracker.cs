namespace Meziantou.Framework.Http.Caching.InMemory;

/// <summary>Tracks the recency, the number and the total size of the entries of a bounded store.</summary>
/// <remarks>The caller holds <see cref="SyncRoot"/> while it calls any other member.</remarks>
internal sealed class LruTracker(long maximumEntryCount, long maximumTotalSize)
{
    // The head is the least recently used entry
    private readonly LinkedList<(string PrimaryKey, HttpCachePersistenceEntry Entry)> _list = new();
    private readonly Dictionary<HttpCachePersistenceEntry, LinkedListNode<(string PrimaryKey, HttpCachePersistenceEntry Entry)>> _nodes = new(ReferenceEqualityComparer.Instance);

    public Lock SyncRoot { get; } = new();

    public long EntryCount => _list.Count;

    public long TotalSize { get; private set; }

    public bool IsOverLimit => EntryCount > maximumEntryCount || TotalSize > maximumTotalSize;

    public void Add(string primaryKey, HttpCachePersistenceEntry entry)
    {
        _nodes.Add(entry, _list.AddLast((primaryKey, entry)));
        TotalSize += entry.SerializedResponse.Length;
    }

    public void Remove(HttpCachePersistenceEntry entry)
    {
        if (_nodes.Remove(entry, out var node))
        {
            _list.Remove(node);
            TotalSize -= entry.SerializedResponse.Length;
        }
    }

    public void Touch(HttpCachePersistenceEntry entry)
    {
        if (_nodes.TryGetValue(entry, out var node))
        {
            _list.Remove(node);
            _list.AddLast(node);
        }
    }

    public bool TryGetLeastRecentlyUsed(out string primaryKey, out HttpCachePersistenceEntry entry)
    {
        if (_list.First is { } first)
        {
            (primaryKey, entry) = first.Value;
            return true;
        }

        primaryKey = "";
        entry = null!;
        return false;
    }

    public void Clear()
    {
        _list.Clear();
        _nodes.Clear();
        TotalSize = 0;
    }
}
