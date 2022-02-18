using System.Collections;

namespace Meziantou.Framework.Versioning;

internal static class ReadOnlyList
{
    public static ReadOnlyList<T> From<T>(IEnumerable<T> items)
    {
        var list = new ReadOnlyList<T>(items);
        list.Freeze();
        return list;
    }
}

internal sealed class ReadOnlyList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _items;
    private bool _frozen;

    public ReadOnlyList()
    {
        _items = new List<T>();
    }

    public ReadOnlyList(int capacity)
    {
        _items = new List<T>(capacity);
    }

    public ReadOnlyList(IEnumerable<T> items)
    {
        _items = new List<T>(items);
    }

    public T this[int index]
    {
        get => _items[index];
        set
        {
            CheckFrozen();
            _items[index] = value;
        }
    }

    public int Count => _items.Count;

    public bool IsReadOnly => _frozen || ((ICollection<T>)_items).IsReadOnly;

    public void Add(T item)
    {
        CheckFrozen();
        _items.Add(item);
    }

    public void Clear()
    {
        CheckFrozen();
        _items.Clear();
    }

    public bool Contains(T item)
    {
        return _items.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _items.CopyTo(array, arrayIndex);
    }

    public void Freeze()
    {
        _frozen = true;
    }

    public int IndexOf(T item)
    {
        return _items.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        CheckFrozen();
        _items.Insert(index, item);
    }

    public bool Remove(T item)
    {
        CheckFrozen();
        return _items.Remove(item);
    }

    public void RemoveAt(int index)
    {
        CheckFrozen();
        _items.RemoveAt(index);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    private void CheckFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("The collection is frozen");
    }
}
