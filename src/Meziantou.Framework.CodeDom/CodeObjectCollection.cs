using System.Collections;

namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a collection of code objects that automatically manages parent-child relationships.</summary>
/// <typeparam name="T">The type of code objects in the collection.</typeparam>
public class CodeObjectCollection<T> : CodeObject, IList<T>, IReadOnlyList<T> where T : CodeObject
{
    private readonly List<T> _list = [];

    public CodeObjectCollection()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CodeObjectCollection{T}"/> class with the specified parent.</summary>
    /// <param name="parent">The parent code object.</param>
    public CodeObjectCollection(CodeObject parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

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

    /// <summary>Adds a range of items to the collection.</summary>
    /// <param name="items">The items to add.</param>
    public void AddRange(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            Add(item);
        }
    }

    void ICollection<T>.Add(T item) => Add(item);

    /// <summary>Adds an item to the collection and returns it.</summary>
    /// <typeparam name="TCodeObject">The type of the code object to add.</typeparam>
    /// <param name="item">The item to add.</param>
    /// <returns>The added item.</returns>
    public TCodeObject Add<TCodeObject>(TCodeObject item)
        where TCodeObject : T
    {
        ArgumentNullException.ThrowIfNull(item);

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

    /// <summary>Gets the number of items in the collection.</summary>
    public int Count => _list.Count;

    /// <summary>Gets a value indicating whether the collection is read-only.</summary>
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

    /// <summary>Gets or sets the item at the specified index.</summary>
    /// <param name="index">The zero-based index of the item.</param>
    public T this[int index]
    {
        get => _list[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            var item = this[index];
            _list[index] = value;
            item?.Parent = null;

            value.Parent = Parent;
        }
    }

    /// <summary>Sorts the elements in the collection using the specified comparer.</summary>
    /// <param name="comparer">The comparer to use for sorting.</param>
    public void Sort(IComparer<T> comparer)
    {
        _list.Sort(comparer);
    }
}
