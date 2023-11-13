using System.Collections;

namespace Meziantou.Framework.CodeDom;

public class CodeObjectCollection<T> : CodeObject, IList<T>, IReadOnlyList<T> where T : CodeObject
{
    private readonly List<T> _list = [];

    public CodeObjectCollection()
    {
    }

    public CodeObjectCollection(CodeObject parent)
    {
        if (parent is null)
            throw new ArgumentNullException(nameof(parent));

        Parent = parent;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return ((IEnumerable)_list).GetEnumerator();
    }

    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    void ICollection<T>.Add(T item) => Add(item);

    public TCodeObject Add<TCodeObject>(TCodeObject item)
        where TCodeObject : T
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        _list.Add(item);
        item.Parent = Parent;
        return item;
    }

    public void Clear()
    {
        foreach (var item in _list)
        {
            item.Parent = null;
        }
        _list.Clear();
    }

    public bool Contains(T item)
    {
        return _list.Contains(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _list.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        var remove = _list.Remove(item);
        if (remove)
        {
            item.Parent = null;
        }
        return remove;
    }

    public int Count => _list.Count;

    public bool IsReadOnly => ((IList<T>)_list).IsReadOnly;

    public int IndexOf(T item)
    {
        return _list.IndexOf(item);
    }

    public void Insert(int index, T item)
    {
        _list.Insert(index, item);
        item.Parent = Parent;
    }

    public void RemoveAt(int index)
    {
        var item = this[index];
        if (item is null)
            return;

        _list.RemoveAt(index);
        item.Parent = null;
    }

    [SuppressMessage("Style", "IDE0016:Use 'throw' expression", Justification = "It would change the behavior")]
    public T this[int index]
    {
        get => _list[index];
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            var item = this[index];
            _list[index] = value;
            if (item is not null)
            {
                item.Parent = null;
            }
            value.Parent = Parent;
        }
    }

    public void Sort(IComparer<T> comparer)
    {
        _list.Sort(comparer);
    }
}
