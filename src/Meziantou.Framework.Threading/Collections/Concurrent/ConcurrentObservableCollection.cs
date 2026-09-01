using System.Collections;
using System.Collections.Immutable;

namespace Meziantou.Framework.Collections.Concurrent;

/// <summary>
/// Thread-safe collection. You can safely bind it to a UI control using the property <see cref="AsObservable"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
/// <remarks>
/// The collection itself can be modified from any thread. The collection exposed by <see cref="AsObservable"/> raises
/// its change notifications on the thread associated with the <see cref="SynchronizationContext"/> provided to the constructor.
/// </remarks>
/// <example>
/// <code>
/// var collection = new ConcurrentObservableCollection&lt;string&gt;();
/// myListBox.ItemsSource = collection.AsObservable;
///
/// // Safe to call from any thread
/// await Task.Run(() => collection.Add("Item 1"));
/// </code>
/// </example>
public class ConcurrentObservableCollection<T> : IList<T>, IReadOnlyList<T>, IList
{
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Lock _lock = new();

    private ImmutableList<T> _items = ImmutableList<T>.Empty;
    private DispatchedObservableCollection<T>? _observableCollection;

    // _items is written under _lock but read without it by Count, the indexer, IndexOf, Contains, CopyTo and
    // GetEnumerator. A plain read is not guaranteed to ever observe the latest write on a weak memory model
    // (arm64), and the JIT may hoist it out of a loop, so publication goes through an explicit fence.
    private ImmutableList<T> Items
    {
        get => Volatile.Read(ref _items);
        set => Volatile.Write(ref _items, value);
    }

    /// <summary>Initializes a new instance of the <see cref="ConcurrentObservableCollection{T}"/> class using the synchronization context of the current thread.</summary>
    /// <remarks>
    /// When the current thread has no synchronization context, a default <see cref="SynchronizationContext"/> is used and the collection
    /// returned by <see cref="AsObservable"/> raises its change notifications on the thread pool. Use the
    /// <see cref="ConcurrentObservableCollection{T}(SynchronizationContext)"/> constructor to bind the collection to a specific thread.
    /// </remarks>
    public ConcurrentObservableCollection()
        : this(GetCurrentSynchronizationContext())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ConcurrentObservableCollection{T}"/> class with the specified synchronization context.</summary>
    /// <param name="synchronizationContext">The synchronization context used to raise collection change notifications.</param>
    public ConcurrentObservableCollection(SynchronizationContext synchronizationContext)
    {
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
    }

    private static SynchronizationContext GetCurrentSynchronizationContext()
    {
        return SynchronizationContext.Current ?? new SynchronizationContext();
    }

    /// <summary>
    /// Determines whether the current thread is the thread the collection returned by <see cref="AsObservable"/> is bound to.
    /// When it is, change notifications are raised synchronously instead of being posted to <see cref="SynchronizationContext"/>.
    /// </summary>
    /// <returns><see langword="true"/> when the current thread can raise the notifications directly; otherwise, <see langword="false"/>.</returns>
    protected internal virtual bool IsOnSynchronizationContextThread()
    {
        return SynchronizationContext.Current == _synchronizationContext;
    }

    /// <summary>
    /// When set to <see langword="true"/> AddRange and InsertRange methods raise NotifyCollectionChanged with all items instead of one event per item.
    /// </summary>
    /// <remarks>Most UI controls, such as the WPF ones, don't support batch modifications</remarks>
    public bool SupportRangeNotifications { get; set; }

    /// <summary>Gets an observable collection that can be bound to UI controls.</summary>
    public IReadOnlyObservableCollection<T> AsObservable
    {
        get
        {
            // Double-checked locking: the fast path reads the field without the lock, so it needs an acquire
            // fence. Without it a caller can observe the reference before the constructor's writes are visible
            // and get a DispatchedObservableCollection whose fields still read as null.
            var observableCollection = Volatile.Read(ref _observableCollection);
            if (observableCollection is null)
            {
                lock (_lock)
                {
                    observableCollection = _observableCollection;
                    if (observableCollection is null)
                    {
                        observableCollection = new DispatchedObservableCollection<T>(this, _synchronizationContext);
                        Volatile.Write(ref _observableCollection, observableCollection);
                    }
                }
            }

            return observableCollection;
        }
    }

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Gets the number of elements in the collection.</summary>
    public int Count => Items.Count;

    bool IList.IsReadOnly => false;

    bool IList.IsFixedSize => false;

    int ICollection.Count => Count;

    object ICollection.SyncRoot => ((ICollection)Items).SyncRoot;

    bool ICollection.IsSynchronized => ((ICollection)Items).IsSynchronized;

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
        get => Items[index];
        set
        {
            lock (_lock)
            {
                Items = Items.SetItem(index, value);
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
            Items = Items.Add(item);
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
            var count = Items.Count;
            Items = Items.AddRange(items);
            if (SupportRangeNotifications)
            {
                _observableCollection?.EnqueueAddRange(Items.GetRange(count, Items.Count - count));
            }
            else
            {
                if (_observableCollection is not null)
                {
                    foreach (var item in Items.GetRange(count, Items.Count - count))
                    {
                        _observableCollection.EnqueueAdd(item);
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
            var count = Items.Count;
            Items = Items.InsertRange(index, items);
            var addedItemsCount = Items.Count - count;
            if (SupportRangeNotifications)
            {
                _observableCollection?.EnqueueInsertRange(index, Items.GetRange(index, addedItemsCount));
            }
            else
            {
                if (_observableCollection is not null)
                {
                    var i = index;
                    foreach (var item in Items.GetRange(index, addedItemsCount))
                    {
                        _observableCollection.EnqueueInsert(i, item);
                        i++;
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
            Items = Items.Clear();
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
            Items = Items.Insert(index, item);
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
            var newList = Items.Remove(item);
            if (Items != newList)
            {
                Items = newList;
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
            Items = Items.RemoveAt(index);
            _observableCollection?.EnqueueRemoveAt(index);
        }
    }

    /// <summary>Returns an enumerator that iterates through the collection.</summary>
    public IEnumerator<T> GetEnumerator()
    {
        return Items.GetEnumerator();
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
        return Items.IndexOf(item);
    }

    /// <summary>Determines whether the collection contains a specific item.</summary>
    /// <param name="item">The item to locate.</param>
    /// <returns><see langword="true"/> if the item is found; otherwise, <see langword="false"/>.</returns>
    public bool Contains(T item)
    {
        return Items.Contains(item);
    }

    /// <summary>Copies the elements of the collection to an array, starting at a particular array index.</summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the array at which copying begins.</param>
    public void CopyTo(T[] array, int arrayIndex)
    {
        Items.CopyTo(array, arrayIndex);
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
            Items = Items.Sort(comparer);
            _observableCollection?.EnqueueReset(Items);
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
            Items = ImmutableList.CreateRange(Items.Order(comparer));
            _observableCollection?.EnqueueReset(Items);
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
                var index = Items.Count;
                Items = Items.Add(item);
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
        ((ICollection)Items).CopyTo(array, index);
    }

    private static bool IsCompatibleObject(object? value)
    {
        // Non-null values are fine. Only accept nulls if T is a class or Nullable<U>.
        // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
        return (value is T) || (value == null && default(T) == null);
    }
}
