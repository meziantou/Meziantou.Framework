using System.Collections;

namespace Meziantou.Framework.Collections.Concurrent;

/// <summary>Represents a thread-safe list that can be accessed by multiple threads concurrently.</summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
/// <remarks>
/// Each member is individually atomic, but a sequence of members is not. For instance,
/// <c>if (!list.Contains(item)) list.Add(item);</c> can add the same item twice, and <c>list[list.Count - 1]</c>
/// can throw when another thread removes an element in between. Use <see cref="Execute(Action{List{T}})"/> or
/// <see cref="Execute{TResult}(Func{List{T}, TResult})"/> to run several operations as a single atomic unit.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// var list = new SynchronizedList<int>();
/// list.Add(1);
/// list.Add(2);
/// foreach (var item in list)
/// {
///     Console.WriteLine(item);
/// }
///
/// // Multiple operations as a single atomic unit
/// list.Execute(items =>
/// {
///     if (!items.Contains(3))
///     {
///         items.Add(3);
///     }
/// });
/// ]]></code>
/// </example>
public sealed class SynchronizedList<T> : IList<T>, IReadOnlyList<T>, ICollection
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

    bool ICollection<T>.IsReadOnly => false;

    bool ICollection.IsSynchronized => true;

    object ICollection.SyncRoot => throw new NotSupportedException("The SyncRoot property may not be used for the synchronization of concurrent collections. Use Execute to run several operations atomically.");

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

    /// <summary>Runs the specified action on the underlying list while holding the lock, so the operations it performs are atomic with respect to other members.</summary>
    /// <param name="action">The action to execute. The list passed to the action must not be used after the action returns.</param>
    /// <remarks>Keep the action short and avoid calling code that may block or acquire other locks, because all other members block while it runs.</remarks>
    public void Execute(Action<List<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        lock (_list)
        {
            action(_list);
        }
    }

    /// <summary>Runs the specified function on the underlying list while holding the lock, so the operations it performs are atomic with respect to other members.</summary>
    /// <typeparam name="TResult">The type of the value returned by <paramref name="func"/>.</typeparam>
    /// <param name="func">The function to execute. The list passed to the function must not be used after the function returns.</param>
    /// <returns>The value returned by <paramref name="func"/>.</returns>
    /// <remarks>Keep the function short and avoid calling code that may block or acquire other locks, because all other members block while it runs.</remarks>
    public TResult Execute<TResult>(Func<List<T>, TResult> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        lock (_list)
        {
            return func(_list);
        }
    }

    /// <summary>Returns an enumerator that iterates over a point-in-time snapshot of the list.</summary>
    /// <remarks>A full copy of the list is taken on each call so enumeration is safe while other threads mutate the list. Avoid enumerating in hot paths where the allocation matters.</remarks>
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

    void ICollection.CopyTo(Array array, int index)
    {
        lock (_list)
        {
            ((ICollection)_list).CopyTo(array, index);
        }
    }
}
