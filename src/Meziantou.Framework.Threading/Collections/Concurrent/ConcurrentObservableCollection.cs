using System.Collections;
using System.Collections.Immutable;

namespace Meziantou.Framework.Collections.Concurrent;

/// <summary>
/// Thread-safe collection. You can safely bind it to a UI control using the property <see cref="AsObservable"/>.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
/// <remarks>
/// <para>
/// The collection itself can be modified from any thread. The collection exposed by <see cref="AsObservable"/> raises
/// its change notifications on the thread associated with the <see cref="SynchronizationContext"/> provided to the constructor.
/// </para>
/// <para>
/// All the notifications of an operation are queued before any of them is raised, and the notifications are never raised
/// recursively: a handler that modifies the collection gets the notifications of its own modification after the ones
/// already queued, so <see cref="AsObservable"/> ends up in the same state as this collection. A handler that throws
/// doesn't prevent the remaining notifications from being raised: the other notifications of the same modification
/// (<c>Count</c>, <c>Item[]</c> and <c>CollectionChanged</c>) and the notifications of the following modifications are
/// still raised. The handlers subscribed to the same event after the one that threw are not invoked, as with any event.
/// The exception is rethrown to the caller of the modification once the queued notifications have all been raised, as an
/// <see cref="AggregateException"/> when several handlers threw.
/// </para>
/// <para>
/// The indexed modifications made through <see cref="AsObservable"/> (the indexer, <c>Insert</c> and <c>RemoveAt</c>) use
/// the indices of the view, which can be behind this collection while notifications are pending. They are applied to the
/// item the view shows at that index, wherever it is in this collection. When that item was removed, or the collection was
/// cleared or sorted, by a modification whose notification is still pending, an <see cref="InvalidOperationException"/> is
/// thrown and nothing is modified.
/// </para>
/// <para>
/// The notifications are raised once the modification has released the internal lock, so a handler can wait for another
/// thread that modifies the collection without deadlocking.
/// </para>
/// <para>
/// When the synchronization context refuses the callback, for instance because the thread it is bound to is gone, the exception is
/// propagated to the caller that modified the collection. The modification is kept, and its notification stays queued until a later
/// modification succeeds in posting to the context.
/// </para>
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../../samples/Meziantou.Framework.WPF.CollectionSamples/ConcurrentObservableCollectionSamples.cs" ref:region="ConcurrentObservableCollection_BindToListBox" ref:dedent="true">
/// var collection = new ConcurrentObservableCollection&lt;string&gt;();
/// myListBox.ItemsSource = collection.AsObservable;
///
/// // Safe to call from any thread
/// await Task.Run(() =&gt; collection.Add("Item 1"));
/// </code>
/// </example>
public class ConcurrentObservableCollection<T> : IList<T>, IReadOnlyList<T>, IList
{
    private readonly SynchronizationContext _synchronizationContext;
    private readonly Thread? _synchronizationContextThread;
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
    /// returned by <see cref="AsObservable"/> raises its change notifications on the thread pool. The handlers can access the collection
    /// returned by <see cref="AsObservable"/> while they run, but no other thread can. Use the
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

        // WPF and Windows Forms install a new SynchronizationContext instance for the same thread: WPF hands out a new
        // DispatcherSynchronizationContext bound to the same Dispatcher while running a dispatcher operation, so comparing
        // the instances reports the UI thread as a foreign thread. Both contexts run their callbacks on a single thread,
        // so the thread they are installed on identifies them. A SynchronizationContext is not thread-affine in general
        // (its callbacks can run on any thread), so every other context keeps the instance comparison.
        if (IsThreadAffine(synchronizationContext) && SynchronizationContext.Current == synchronizationContext)
        {
            _synchronizationContextThread = Thread.CurrentThread;
        }
    }

    private static bool IsThreadAffine(SynchronizationContext synchronizationContext)
    {
        // The assembly doesn't depend on a UI framework, so the types cannot be used directly. Both are sealed,
        // so comparing the type name is enough. Override IsOnSynchronizationContextThread to support another
        // thread-affine synchronization context.
        return synchronizationContext.GetType().FullName is "System.Windows.Threading.DispatcherSynchronizationContext" // WPF
            or "System.Windows.Forms.WindowsFormsSynchronizationContext"; // Windows Forms
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
    /// <remarks>
    /// The current thread is considered to be the right one when it runs the <see cref="SynchronizationContext"/> provided to the
    /// constructor. The WPF and Windows Forms synchronization contexts are also matched by thread, as they run their callbacks on a
    /// single thread but expose several instances for it. Override this method to support another thread-affine context.
    /// </remarks>
    protected internal virtual bool IsOnSynchronizationContextThread()
    {
        return SynchronizationContext.Current == _synchronizationContext || _synchronizationContextThread == Thread.CurrentThread;
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

    // The underlying immutable list is replaced on every modification, so its SyncRoot would be a different object each
    // time and locking on it would synchronize nothing.
    object ICollection.SyncRoot => throw new NotSupportedException("The SyncRoot property may not be used for the synchronization of concurrent collections.");

    bool ICollection.IsSynchronized => ((ICollection)Items).IsSynchronized;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = ConvertValue(value);
    }

    /// <summary>Gets or sets the element at the specified index.</summary>
    public T this[int index]
    {
        get => Items[index];
        set => SetItem(index, value, isViewIndex: false);
    }

    internal void SetItemFromView(int index, T value) => SetItem(index, value, isViewIndex: true);

    private void SetItem(int index, T value, bool isViewIndex)
    {
        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            observableCollection = _observableCollection;
            if (isViewIndex)
            {
                index = observableCollection!.GetSourceIndex(index, isInsertion: false);
            }

            Items = Items.SetItem(index, value);
            observableCollection?.EnqueueReplace(index, value);
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
    }

    /// <summary>Adds an item to the collection.</summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            Items = Items.Add(item);
            observableCollection = _observableCollection;
            observableCollection?.EnqueueAdd(item);
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
    }

    /// <summary>Adds multiple items to the collection.</summary>
    /// <param name="items">The items to add.</param>
    public void AddRange(params T[] items)
    {
        AddRange((IEnumerable<T>)items);
    }

    /// <summary>Adds multiple items to the collection.</summary>
    /// <param name="items">The items to add.</param>
    /// <remarks>
    /// <paramref name="items"/> is enumerated before the collection is modified, so the enumeration can modify the collection.
    /// </remarks>
    public void AddRange(IEnumerable<T> items)
    {
        // Enumerating the items while holding the lock would compute the new list from a snapshot taken before the
        // enumeration, and a modification made by the enumeration on this thread (the lock is reentrant) would be lost.
        var addedItems = ImmutableList.CreateRange(items);

        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            Items = Items.AddRange(addedItems);
            observableCollection = _observableCollection;
            if (SupportRangeNotifications)
            {
                observableCollection?.EnqueueAddRange(addedItems);
            }
            else
            {
                observableCollection?.EnqueueAddRangeAsSingleItemEvents(addedItems);
            }
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
    }

    /// <summary>Inserts multiple items into the collection at the specified index.</summary>
    /// <param name="index">The zero-based index at which items should be inserted.</param>
    /// <param name="items">The items to insert.</param>
    /// <remarks>
    /// <paramref name="items"/> is enumerated before the collection is modified, so the enumeration can modify the collection.
    /// <paramref name="index"/> is validated against the collection once the enumeration is complete.
    /// </remarks>
    public void InsertRange(int index, IEnumerable<T> items)
    {
        var insertedItems = ImmutableList.CreateRange(items);

        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            Items = Items.InsertRange(index, insertedItems);
            observableCollection = _observableCollection;
            if (SupportRangeNotifications)
            {
                observableCollection?.EnqueueInsertRange(index, insertedItems);
            }
            else
            {
                observableCollection?.EnqueueInsertRangeAsSingleItemEvents(index, insertedItems);
            }
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
    }

    /// <summary>Removes all items from the collection.</summary>
    public void Clear()
    {
        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            Items = Items.Clear();
            observableCollection = _observableCollection;
            observableCollection?.EnqueueClear();
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
    }

    /// <summary>Inserts an item into the collection at the specified index.</summary>
    /// <param name="index">The zero-based index at which the item should be inserted.</param>
    /// <param name="item">The item to insert.</param>
    public void Insert(int index, T item) => Insert(index, item, isViewIndex: false);

    internal void InsertFromView(int index, T item) => Insert(index, item, isViewIndex: true);

    private void Insert(int index, T item, bool isViewIndex)
    {
        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            observableCollection = _observableCollection;
            if (isViewIndex)
            {
                index = observableCollection!.GetSourceIndex(index, isInsertion: true);
            }

            Items = Items.Insert(index, item);
            observableCollection?.EnqueueInsert(index, item);
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
    }

    /// <summary>Removes the first occurrence of a specific item from the collection.</summary>
    /// <param name="item">The item to remove.</param>
    /// <returns><see langword="true"/> if the item was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(T item)
    {
        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            var items = Items;
            var index = items.IndexOf(item);
            ThrowIfModifiedByCallback(items);
            if (index < 0)
                return false;

            // The view replays the removal by position, so it removes the item that was stored in the collection
            // rather than searching again for a value the caller can still modify.
            Items = items.RemoveAt(index);
            observableCollection = _observableCollection;
            observableCollection?.EnqueueRemoveAt(index);
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
        return true;
    }

    /// <summary>Removes the item at the specified index.</summary>
    /// <param name="index">The zero-based index of the item to remove.</param>
    public void RemoveAt(int index) => RemoveAt(index, isViewIndex: false);

    internal void RemoveAtFromView(int index) => RemoveAt(index, isViewIndex: true);

    private void RemoveAt(int index, bool isViewIndex)
    {
        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            observableCollection = _observableCollection;
            if (isViewIndex)
            {
                index = observableCollection!.GetSourceIndex(index, isInsertion: false);
            }

            Items = Items.RemoveAt(index);
            observableCollection?.EnqueueRemoveAt(index);
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
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
    /// <exception cref="InvalidOperationException"><paramref name="comparer"/> modified the collection. The collection is not sorted.</exception>
    public void Sort(IComparer<T>? comparer)
    {
        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            var items = Items;
            var sortedItems = items.Sort(comparer);
            ThrowIfModifiedByCallback(items);
            Items = sortedItems;
            observableCollection = _observableCollection;
            observableCollection?.EnqueueReset(Items);
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
    }

    /// <summary>Performs a stable sort on the collection using the default comparer.</summary>
    public void StableSort()
    {
        StableSort(comparer: null);
    }

    /// <summary>Performs a stable sort on the collection using the specified comparer.</summary>
    /// <param name="comparer">The comparer to use when comparing elements.</param>
    /// <exception cref="InvalidOperationException"><paramref name="comparer"/> modified the collection. The collection is not sorted.</exception>
    public void StableSort(IComparer<T>? comparer)
    {
        DispatchedObservableCollection<T>? observableCollection;
        lock (_lock)
        {
            var items = Items;
            var sortedItems = ImmutableList.CreateRange(items.Order(comparer));
            ThrowIfModifiedByCallback(items);
            Items = sortedItems;
            observableCollection = _observableCollection;
            observableCollection?.EnqueueReset(Items);
        }

        observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
    }

    int IList.Add(object? value)
    {
        ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(value, nameof(value));

        try
        {
            var item = (T)value!;
            int index;
            DispatchedObservableCollection<T>? observableCollection;
            lock (_lock)
            {
                index = Items.Count;
                Items = Items.Add(item);
                observableCollection = _observableCollection;
                observableCollection?.EnqueueAdd(item);
            }

            observableCollection?.ProcessPendingEventsIfOnSynchronizationContextThread();
            return index;
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
        Insert(index, ConvertValue(value));
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

    /// <summary>
    /// Throws when a callback invoked while holding the lock, such as a comparer or an <c>Equals</c> method, modified the
    /// collection. The lock is reentrant, so the modification succeeded and was notified; replacing the list computed from
    /// <paramref name="snapshot"/> would silently lose it and leave <see cref="AsObservable"/> different from this collection.
    /// </summary>
    private void ThrowIfModifiedByCallback(ImmutableList<T> snapshot)
    {
        if (Items != snapshot)
            throw new InvalidOperationException("The collection was modified by a callback of the operation, such as a comparer or an Equals method. The operation was not applied.");
    }

    internal static T ConvertValue(object? value)
    {
        ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(value, nameof(value));

        if (!IsCompatibleObject(value))
        {
            ThrowHelper.ThrowInvalidTypeException<T>(value);
        }

        return (T)value!;
    }

    internal static bool IsCompatibleObject(object? value)
    {
        // Non-null values are fine. Only accept nulls if T is a class or Nullable<U>.
        // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
        return (value is T) || (value == null && default(T) == null);
    }
}
