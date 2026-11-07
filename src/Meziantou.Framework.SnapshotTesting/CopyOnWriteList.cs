namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// A list that publishes a new array on every mutation instead of updating one in place. Readers observe a stable
/// snapshot, so a registration happening on another thread can neither break an enumeration nor be observed halfway.
/// </summary>
internal sealed class CopyOnWriteList<T> : IList<T>
{
    private readonly Lock _lock = new();
    private T[] _items;

    public CopyOnWriteList() => _items = [];

    public CopyOnWriteList(IEnumerable<T> items) => _items = [.. items];

    private T[] Current => Volatile.Read(ref _items);

    public T this[int index]
    {
        get => Current[index];
        set
        {
            lock (_lock)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _items.Length);

                T[] updated = [.. _items];
                updated[index] = value;
                Volatile.Write(ref _items, updated);
            }
        }
    }

    public int Count => Current.Length;

    public bool IsReadOnly => false;

    public void Add(T item)
    {
        lock (_lock)
        {
            Volatile.Write(ref _items, [.. _items, item]);
        }
    }

    public void Insert(int index, T item)
    {
        lock (_lock)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _items.Length);

            var updated = new T[_items.Length + 1];
            _items.AsSpan(0, index).CopyTo(updated);
            updated[index] = item;
            _items.AsSpan(index).CopyTo(updated.AsSpan(index + 1));
            Volatile.Write(ref _items, updated);
        }
    }

    public bool Remove(T item)
    {
        lock (_lock)
        {
            var index = Array.IndexOf(_items, item);
            if (index < 0)
                return false;

            RemoveAtCore(index);
            return true;
        }
    }

    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _items.Length);

            RemoveAtCore(index);
        }
    }

    private void RemoveAtCore(int index)
    {
        var updated = new T[_items.Length - 1];
        _items.AsSpan(0, index).CopyTo(updated);
        _items.AsSpan(index + 1).CopyTo(updated.AsSpan(index));
        Volatile.Write(ref _items, updated);
    }

    public void Clear()
    {
        lock (_lock)
        {
            Volatile.Write(ref _items, []);
        }
    }

    public bool Contains(T item) => Array.IndexOf(Current, item) >= 0;

    public int IndexOf(T item) => Array.IndexOf(Current, item);

    public void CopyTo(T[] array, int arrayIndex) => Current.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Current).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
