using System.Collections;

namespace Meziantou.Framework.Templating;

public abstract class FreezableCollection<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _items = [];

    public bool IsFrozen { get; private set; }
    public int Count => _items.Count;
    public bool IsReadOnly => IsFrozen;

    public T this[int index]
    {
        get => _items[index];
        set
        {
            ThrowIfFrozen();
            ValidateItem(value);
            _items[index] = value;
        }
    }

    public void Add(T item)
    {
        ThrowIfFrozen();
        ValidateItem(item);
        _items.Add(item);
    }

    public void Clear()
    {
        ThrowIfFrozen();
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

    public IEnumerator<T> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    public int IndexOf(T item)
    {
        return _items.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        ThrowIfFrozen();
        ValidateItem(item);
        _items.Insert(index, item);
    }

    public bool Remove(T item)
    {
        ThrowIfFrozen();
        return _items.Remove(item);
    }

    public void RemoveAt(int index)
    {
        ThrowIfFrozen();
        _items.RemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    internal void Freeze()
    {
        IsFrozen = true;
    }

    internal void AddRange(IEnumerable<T> items)
    {
        ThrowIfFrozen();

        foreach (var item in items)
        {
            ValidateItem(item);
            _items.Add(item);
        }
    }

    protected virtual void ValidateItem(T item)
    {
    }

    protected void ThrowIfFrozen()
    {
        if (IsFrozen)
            throw new InvalidOperationException("Collection is frozen.");
    }
}
