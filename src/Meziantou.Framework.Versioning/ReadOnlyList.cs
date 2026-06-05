using System.Collections;

namespace Meziantou.Framework.Versioning;

// A small append-then-freeze builder backed directly by an array.
// Prerelease/metadata label collections typically hold 1-2 items, so the backing
// array grows from 2 (instead of List<T>'s default of 4) and avoids the extra
// List<T> object allocation. Only IReadOnlyList<T> is exposed; the mutating
// members are non-interface methods used while building the collection.
internal sealed class ReadOnlyList<T> : IReadOnlyList<T>
{
    private T[] _items = [];
    private int _count;
    private bool _frozen;

    public ReadOnlyList()
    {
    }

    public ReadOnlyList(int capacity)
    {
        if (capacity > 0)
        {
            _items = new T[capacity];
        }
    }

    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            return _items[index];
        }
    }

    public int Count => _count;

    public void Add(T item)
    {
        if (_frozen)
            throw new InvalidOperationException("The collection is frozen");

        if (_count == _items.Length)
        {
            Array.Resize(ref _items, _items.Length == 0 ? 2 : _items.Length * 2);
        }

        _items[_count++] = item;
    }

    public void Freeze() => _frozen = true;

    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
        {
            yield return _items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
