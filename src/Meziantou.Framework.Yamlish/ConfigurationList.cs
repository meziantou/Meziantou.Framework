using System.Collections;

namespace Meziantou.Framework.Yamlish;

internal abstract class ConfigurationList<T> : IList<T>
{
    private readonly List<T> _items;

    protected ConfigurationList(IEnumerable<T>? items = null)
    {
        _items = items is null ? [] : [.. items];
    }

    protected abstract bool IsImmutable { get; }

    protected abstract void VerifyMutable();

    public T this[int index]
    {
        get => _items[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            VerifyMutable();
            _items[index] = value;
        }
    }

    public int Count => _items.Count;

    public bool IsReadOnly => IsImmutable;

    public void Add(T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        VerifyMutable();
        _items.Add(item);
    }

    public void Clear()
    {
        VerifyMutable();
        _items.Clear();
    }

    public bool Contains(T item) => _items.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    public int IndexOf(T item) => _items.IndexOf(item);

    public void Insert(int index, T item)
    {
        ArgumentNullException.ThrowIfNull(item);
        VerifyMutable();
        _items.Insert(index, item);
    }

    public bool Remove(T item)
    {
        VerifyMutable();
        return _items.Remove(item);
    }

    public void RemoveAt(int index)
    {
        VerifyMutable();
        _items.RemoveAt(index);
    }

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
