using System.Collections;

namespace Meziantou.Framework.Collections.Concurrent;

/// <summary>Represents a thread-safe list that can be accessed by multiple threads concurrently.</summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
/// <example>
/// <code><![CDATA[
/// var list = new SynchronizedList<int>();
/// list.Add(1);
/// list.Add(2);
/// foreach (var item in list)
/// {
///     Console.WriteLine(item);
/// }
/// ]]></code>
/// </example>
public sealed class SynchronizedList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly List<T> _list;

    /// <summary>Initializes a new instance of the <see cref="SynchronizedList{T}"/> class that is empty.</summary>
    public SynchronizedList()
    {
        _list = [];
    }

    /// <summary>Initializes a new instance of the <see cref="SynchronizedList{T}"/> class that is empty and has the specified initial capacity.</summary>
    /// <param name="capacity">The number of elements that the new list can initially store.</param>
    public SynchronizedList(int capacity)
    {
        _list = new List<T>(capacity);
    }

    /// <summary>Initializes a new instance of the <see cref="SynchronizedList{T}"/> class that contains elements copied from the specified collection.</summary>
    /// <param name="items">The collection whose elements are copied to the new list.</param>
    public SynchronizedList(IEnumerable<T>? items = null)
    {
        _list = items is not null ? [.. items] : [];
    }

    /// <summary>Initializes a new instance of the <see cref="SynchronizedList{T}"/> class that contains elements copied from the specified span.</summary>
    /// <param name="items">The span whose elements are copied to the new list.</param>
    public SynchronizedList(ReadOnlySpan<T> items)
    {
        _list = [.. items];
    }

    public int Count
    {
        get
        {
            lock (_list)
            {
                return _list.Count;
            }
        }
    }

    bool ICollection<T>.IsReadOnly
    {
        get
        {
            lock (_list)
            {
                return ((ICollection<T>)_list).IsReadOnly;
            }
        }
    }

    public T this[int index]
    {
        get
        {
            lock (_list)
            {
                return _list[index];
            }
        }
        set
        {
            lock (_list)
            {
                _list[index] = value;
            }
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        lock (_list)
        {
            return ((IReadOnlyCollection<T>)[.. _list]).GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Ensures that the capacity of this list is at least the specified capacity.</summary>
    /// <param name="capacity">The minimum capacity to ensure.</param>
    /// <returns>The new capacity of this list.</returns>
    public int EnsureCapacity(int capacity)
    {
        lock (_list)
        {
            return _list.EnsureCapacity(capacity);
        }
    }

    public void Add(T item)
    {
        lock (_list)
        {
            _list.Add(item);
        }
    }

    public void Clear()
    {
        lock (_list)
        {
            _list.Clear();
        }
    }

    public bool Contains(T item)
    {
        lock (_list)
        {
            return _list.Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        lock (_list)
        {
            _list.CopyTo(array, arrayIndex);
        }
    }

    public bool Remove(T item)
    {
        lock (_list)
        {
            return _list.Remove(item);
        }
    }

    public int IndexOf(T item)
    {
        lock (_list)
        {
            return _list.IndexOf(item);
        }
    }

    public void Insert(int index, T item)
    {
        lock (_list)
        {
            _list.Insert(index, item);
        }
    }

    public void RemoveAt(int index)
    {
        lock (_list)
        {
            _list.RemoveAt(index);
        }
    }

    public void CopyTo(Array array, int index)
    {
        lock (_list)
        {
            ((ICollection)_list).CopyTo(array, index);
        }
    }
}
