namespace Meziantou.Framework.SnapshotTesting;

public sealed class SnapshotComparerCollection : IEnumerable<KeyValuePair<SnapshotType, ISnapshotComparer>>
{
    private readonly Dictionary<SnapshotType, ISnapshotComparer> _comparers;

    public SnapshotComparerCollection()
    {
        _comparers = [];
    }

    internal SnapshotComparerCollection(SnapshotComparerCollection source)
    {
        _comparers = new Dictionary<SnapshotType, ISnapshotComparer>(source._comparers);
    }

    public int Count => _comparers.Count;

    public void Set(SnapshotType type, ISnapshotComparer comparer)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(comparer);
        _comparers[type] = comparer;
    }

    public ISnapshotComparer Get(SnapshotType type)
    {
        if (_comparers.TryGetValue(type, out var comparer))
            return comparer;

        if (_comparers.TryGetValue(SnapshotType.None, out var defaultComparer))
            return defaultComparer;

        return ByteArraySnapshotComparer.Instance;
    }

    public bool Remove(SnapshotType type) => _comparers.Remove(type);

    public void Clear() => _comparers.Clear();

    public IEnumerator<KeyValuePair<SnapshotType, ISnapshotComparer>> GetEnumerator() => _comparers.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
