namespace Meziantou.Framework.SnapshotTesting;

public sealed class SnapshotComparerCollection : IEnumerable<KeyValuePair<SnapshotType, ISnapshotComparer>>
{
    private readonly Lock _lock = new();

    // Copy-on-write: the published dictionary is never mutated, so a lookup can run while another thread registers a comparer.
    private Dictionary<SnapshotType, ISnapshotComparer> _comparers;

    public SnapshotComparerCollection()
    {
        _comparers = [];
    }

    internal SnapshotComparerCollection(SnapshotComparerCollection source)
    {
        _comparers = new Dictionary<SnapshotType, ISnapshotComparer>(source.Current);
    }

    private Dictionary<SnapshotType, ISnapshotComparer> Current => Volatile.Read(ref _comparers);

    public int Count => Current.Count;

    public void Set(SnapshotType type, ISnapshotComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(comparer);
        lock (_lock)
        {
            var updated = new Dictionary<SnapshotType, ISnapshotComparer>(_comparers)
            {
                [type] = comparer,
            };

            Volatile.Write(ref _comparers, updated);
        }
    }

    /// <summary>Looks up a comparer registered for exactly this type, without falling back to a default.</summary>
    internal bool TryGet(SnapshotType type, [NotNullWhen(true)] out ISnapshotComparer? comparer) => Current.TryGetValue(type, out comparer);

    public ISnapshotComparer Get(SnapshotType type)
    {
        var comparers = Current;
        if (comparers.TryGetValue(type, out var comparer))
            return comparer;

        if (comparers.TryGetValue(SnapshotType.None, out var defaultComparer))
            return defaultComparer;

        return ByteArraySnapshotComparer.Instance;
    }

    public bool Remove(SnapshotType type)
    {
        lock (_lock)
        {
            if (!_comparers.ContainsKey(type))
                return false;

            var updated = new Dictionary<SnapshotType, ISnapshotComparer>(_comparers);
            updated.Remove(type);
            Volatile.Write(ref _comparers, updated);
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Volatile.Write(ref _comparers, []);
        }
    }

    public IEnumerator<KeyValuePair<SnapshotType, ISnapshotComparer>> GetEnumerator() => ((IEnumerable<KeyValuePair<SnapshotType, ISnapshotComparer>>)Current).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
