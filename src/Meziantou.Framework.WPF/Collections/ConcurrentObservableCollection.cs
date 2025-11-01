using System.Collections;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Threading;

namespace Meziantou.Framework.WPF.Collections;

/// <summary>
/// Thread-safe collection. You can safely bind it to a WPF control using the property <see cref="AsObservable"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
/// <example>
/// <code>
/// var collection = new ConcurrentObservableCollection&lt;string&gt;();
/// myListBox.ItemsSource = collection.AsObservable;
/// 
/// // Safe to call from any thread
/// await Task.Run(() => collection.Add("Item 1"));
/// </code>
/// </example>
public sealed class ConcurrentObservableCollection<T> : IList<T>, IReadOnlyList<T>, IList
{
    private readonly Dispatcher _dispatcher;
    private readonly Lock _lock = new();

    private ImmutableList<T> _items = ImmutableList<T>.Empty;
    private DispatchedObservableCollection<T>? _observableCollection;

    /// <summary>Initializes a new instance of the <see cref="ConcurrentObservableCollection{T}"/> class using the current dispatcher.</summary>
    public ConcurrentObservableCollection()
        : this(GetCurrentDispatcher())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ConcurrentObservableCollection{T}"/> class with the specified dispatcher.</summary>
    /// <param name="dispatcher">The dispatcher to use for raising collection change notifications.</param>
    public ConcurrentObservableCollection(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    private static Dispatcher GetCurrentDispatcher()
    {
        return Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    /// <summary>
    /// When set to <see langword="true"/> AddRange and InsertRange methods raise NotifyCollectionChanged with all items instead of one event per item.
    /// </summary>
    /// <remarks>Most WPF controls doesn't support batch modifications</remarks>
    public bool SupportRangeNotifications { get; set; }

    /// <summary>Gets an observable collection that can be bound to WPF controls.</summary>
    public IReadOnlyObservableCollection<T> AsObservable
    {
        get
        {
            if (_observableCollection is null)
            {
                lock (_lock)
                {
                    _observableCollection ??= new DispatchedObservableCollection<T>(this, _dispatcher);
                }
            }

            return _observableCollection;
        }
    }

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Gets the number of elements in the collection.</summary>
    public int Count => _items.Count;

    bool IList.IsReadOnly => false;

    bool IList.IsFixedSize => false;

    int ICollection.Count => Count;

    object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

    bool ICollection.IsSynchronized => ((ICollection)_items).IsSynchronized;

    object? IList.this[int index]
    {
        get => this[index];
        set
        {
            ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(value, nameof(value));

            try
            {
                this[index] = (T)value!;
            }
            catch (InvalidCastException)
            {
                ThrowHelper.ThrowInvalidTypeException<T>(value);
            }
        }
    }

    /// <summary>Gets or sets the element at the specified index.</summary>
    public T this[int index]
    {
        get => _items[index];
        set
        {
            lock (_lock)
            {
                _items = _items.SetItem(index, value);
                _observableCollection?.EnqueueReplace(index, value);
            }
        }
    }

    /// <summary>Adds an item to the collection.</summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        lock (_lock)
        {
            _items = _items.Add(item);
            _observableCollection?.EnqueueAdd(item);
        }
    }

    /// <summary>Adds multiple items to the collection.</summary>
    /// <param name="items">The items to add.</param>
    public void AddRange(params T[] items)
    {
        AddRange((IEnumerable<T>)items);
    }

    /// <summary>Adds multiple items to the collection.</summary>
    /// <param name="items">The items to add.</param>
    public void AddRange(IEnumerable<T> items)
    {
        lock (_lock)
        {
            var count = _items.Count;
            _items = _items.AddRange(items);
            if (SupportRangeNotifications)
            {
                _observableCollection?.EnqueueAddRange(_items.GetRange(count, _items.Count - count));
            }
            else
            {
                if (_observableCollection is not null)
                {
                    for (var i = count; i < _items.Count; i++)
                    {
                        _observableCollection.EnqueueAdd(_items[i]);
                    }
                }
            }
        }
    }

    /// <summary>Inserts multiple items into the collection at the specified index.</summary>
    /// <param name="index">The zero-based index at which items should be inserted.</param>
    /// <param name="items">The items to insert.</param>
    public void InsertRange(int index, IEnumerable<T> items)
    {
        lock (_lock)
        {
            var count = _items.Count;
            _items = _items.InsertRange(index, items);
            var addedItemsCount = _items.Count - count;
            if (SupportRangeNotifications)
            {
                _observableCollection?.EnqueueInsertRange(index, _items.GetRange(index, addedItemsCount));
            }
            else
            {
                if (_observableCollection is not null)
                {
                    for (var i = index; i < index + addedItemsCount; i++)
                    {
                        _observableCollection.EnqueueInsert(i, _items[i]);
                    }
                }
            }
        }
    }

    /// <summary>Removes all items from the collection.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _items = _items.Clear();
            _observableCollection?.EnqueueClear();
        }
    }

    /// <summary>Inserts an item into the collection at the specified index.</summary>
    /// <param name="index">The zero-based index at which the item should be inserted.</param>
    /// <param name="item">The item to insert.</param>
    public void Insert(int index, T item)
    {
        lock (_lock)
        {
            _items = _items.Insert(index, item);
            _observableCollection?.EnqueueInsert(index, item);
        }
    }

    /// <summary>Removes the first occurrence of a specific item from the collection.</summary>
    /// <param name="item">The item to remove.</param>
    /// <returns><see langword="true"/> if the item was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(T item)
    {
        lock (_lock)
        {
            var newList = _items.Remove(item);
            if (_items != newList)
            {
                _items = newList;
                _observableCollection?.EnqueueRemove(item);
                return true;
            }

            return false;
        }
    }

    /// <summary>Removes the item at the specified index.</summary>
    /// <param name="index">The zero-based index of the item to remove.</param>
    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            _items = _items.RemoveAt(index);
            _observableCollection?.EnqueueRemoveAt(index);
        }
    }

    /// <summary>Returns an enumerator that iterates through the collection.</summary>
    public IEnumerator<T> GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Determines the index of a specific item in the collection.</summary>
    /// <param name="item">The item to locate.</param>
    /// <returns>The index of the item if found; otherwise, -1.</returns>
    public int IndexOf(T item)
    {
        return _items.IndexOf(item);
    }

    /// <summary>Determines whether the collection contains a specific item.</summary>
    /// <param name="item">The item to locate.</param>
    /// <returns><see langword="true"/> if the item is found; otherwise, <see langword="false"/>.</returns>
    public bool Contains(T item)
    {
        return _items.Contains(item);
    }

    /// <summary>Copies the elements of the collection to an array, starting at a particular array index.</summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the array at which copying begins.</param>
    public void CopyTo(T[] array, int arrayIndex)
    {
        _items.CopyTo(array, arrayIndex);
    }

    /// <summary>Sorts the elements in the collection using the default comparer.</summary>
    public void Sort()
    {
        Sort(comparer: null);
    }

    /// <summary>Sorts the elements in the collection using the specified comparer.</summary>
    /// <param name="comparer">The comparer to use when comparing elements.</param>
    public void Sort(IComparer<T>? comparer)
    {
        lock (_lock)
        {
            _items = _items.Sort(comparer);
            _observableCollection?.EnqueueReset(_items);
        }
    }

    /// <summary>Performs a stable sort on the collection using the default comparer.</summary>
    public void StableSort()
    {
        StableSort(comparer: null);
    }

    /// <summary>Performs a stable sort on the collection using the specified comparer.</summary>
    /// <param name="comparer">The comparer to use when comparing elements.</param>
    public void StableSort(IComparer<T>? comparer)
    {
        lock (_lock)
        {
            _items = ImmutableList.CreateRange(_items.Order(comparer));
            _observableCollection?.EnqueueReset(_items);
        }
    }

    int IList.Add(object? value)
    {
        ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(value, nameof(value));

        try
        {
            var item = (T)value!;
            lock (_lock)
            {
                var index = _items.Count;
                _items = _items.Add(item);
                _observableCollection?.EnqueueAdd(item);
                return index;
            }
        }
        catch (InvalidCastException)
        {
            ThrowHelper.ThrowInvalidTypeException<T>(value);
            return -1; // Never reached, but the compiler needs it
        }
    }

    bool IList.Contains(object? value)
    {
        if (IsCompatibleObject(value))
        {
            return Contains((T)value!);
        }

        return false;
    }

    void IList.Clear()
    {
        Clear();
    }

    int IList.IndexOf(object? value)
    {
        if (IsCompatibleObject(value))
        {
            return IndexOf((T)value!);
        }

        return -1;
    }

    void IList.Insert(int index, object? value)
    {
        ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(value, nameof(value));

        try
        {
            Insert(index, (T)value!);
        }
        catch (InvalidCastException)
        {
            ThrowHelper.ThrowInvalidTypeException<T>(value);
        }
    }

    void IList.Remove(object? value)
    {
        if (IsCompatibleObject(value))
        {
            Remove((T)value!);
        }
    }

    void IList.RemoveAt(int index)
    {
        RemoveAt(index);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        ((ICollection)_items).CopyTo(array, index);
    }

    private static bool IsCompatibleObject(object? value)
    {
        // Non-null values are fine. Only accept nulls if T is a class or Nullable<U>.
        // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
        return (value is T) || (value == null && default(T) == null);
    }
}
