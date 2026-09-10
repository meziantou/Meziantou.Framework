using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Collections.Concurrent;

// The items of this collection are a replica of the source collection that is only updated on the synchronization
// context thread, so it lags behind the source while pending events wait to be dispatched. Every query reads that
// replica, and the mutations exposed by IList<T> and IList are applied to the source collection: those whose meaning
// depends on the replica (the ones taking an index, and IList.Add which returns one) are rejected while the replica
// is out of date, as the index would designate another item in the source collection. The others (Add, Remove and
// Clear) designate their target by value, so they are always forwarded.
internal sealed class DispatchedObservableCollection<T> : ObservableCollectionBase<T>, IReadOnlyObservableCollection<T>, IList<T>, IList
{
    private readonly ConcurrentQueue<PendingEvent<T>> _pendingEvents = new();
    private readonly ConcurrentObservableCollection<T> _collection;
    private readonly SynchronizationContext _synchronizationContext;

    private volatile bool _isProcessingPending;

    // HasPendingEvents cannot be answered by looking at the queue: an event that has been dequeued but not applied yet
    // leaves it empty while the items are still behind the source collection. Counting instead makes the answer exact.
    // _enqueuedEventCount is only written under the source collection's lock, _processedEventCount only by the drain,
    // and drains never overlap.
    private long _enqueuedEventCount;
    private long _processedEventCount;
    private int _isDraining;

    public DispatchedObservableCollection(ConcurrentObservableCollection<T> collection, SynchronizationContext synchronizationContext)
        : base(collection)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
    }

    private void AssertIsOnSynchronizationContextThread()
    {
        if (!_collection.IsOnSynchronizationContextThread())
        {
            var currentThreadId = Environment.CurrentManagedThreadId;
            throw new InvalidOperationException("The collection must be accessed from the synchronization context thread only. Current thread ID: " + currentThreadId.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static T ConvertItem(object? value)
    {
        ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(value, nameof(value));
        if (!ConcurrentObservableCollection<T>.IsCompatibleObject(value))
        {
            ThrowHelper.ThrowInvalidTypeException<T>(value);
        }

        return (T)value!;
    }

    public int Count
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            lock (ItemsLock)
            {
                return Items.Count;
            }
        }
    }

    bool ICollection<T>.IsReadOnly
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return ((ICollection<T>)Items).IsReadOnly;
        }
    }

    int ICollection.Count
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return Count;
        }
    }

    object ICollection.SyncRoot
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return ((ICollection)Items).SyncRoot;
        }
    }

    bool ICollection.IsSynchronized
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return ((ICollection)Items).IsSynchronized;
        }
    }

    bool IList.IsReadOnly
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return ((IList)Items).IsReadOnly;
        }
    }

    bool IList.IsFixedSize
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return ((IList)Items).IsFixedSize;
        }
    }

    object? IList.this[int index]
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return this[index];
        }

        set
        {
            AssertIsOnSynchronizationContextThread();
            _collection.SetItemFromObservableCollection(index, ConvertItem(value));
        }
    }

    T IList<T>.this[int index]
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return this[index];
        }
        set
        {
            AssertIsOnSynchronizationContextThread();
            _collection.SetItemFromObservableCollection(index, value);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        AssertIsOnSynchronizationContextThread();

        // The enumerator outlives the lock, and the pending events can be applied on another thread while the caller
        // walks it, so it walks a snapshot instead of the live list.
        lock (ItemsLock)
        {
            return Items.ToList().GetEnumerator();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void CopyTo(T[] array, int arrayIndex)
    {
        AssertIsOnSynchronizationContextThread();
        lock (ItemsLock)
        {
            Items.CopyTo(array, arrayIndex);
        }
    }

    public int IndexOf(T item)
    {
        AssertIsOnSynchronizationContextThread();
        lock (ItemsLock)
        {
            return Items.IndexOf(item);
        }
    }

    public bool Contains(T item)
    {
        AssertIsOnSynchronizationContextThread();
        lock (ItemsLock)
        {
            return Items.Contains(item);
        }
    }

    public T this[int index]
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            lock (ItemsLock)
            {
                return Items[index];
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether changes made to the source collection are still waiting to be applied to this collection.
    /// </summary>
    /// <remarks>Read by the source collection while holding its lock to detect that the indices of this collection are stale.</remarks>
    internal bool HasPendingEvents => Volatile.Read(ref _processedEventCount) != Volatile.Read(ref _enqueuedEventCount);

    /// <summary>Counts an event as processed as soon as the items are updated, before its handlers are notified.</summary>
    /// <remarks>
    /// A handler is free to modify the source collection, and at that point this collection already reflects the event
    /// being notified, so the edit must be allowed to go through instead of being rejected as stale.
    /// </remarks>
    private protected override void OnItemsMutated()
    {
        Volatile.Write(ref _processedEventCount, _processedEventCount + 1);
    }

    internal void EnqueueReplace(int index, T value)
    {
        EnqueueEvent(PendingEvent.Replace(index, value));
    }

    internal void EnqueueReset(System.Collections.Immutable.ImmutableList<T> items)
    {
        EnqueueEvent(PendingEvent.Reset(items));
    }

    internal void EnqueueAdd(T item)
    {
        EnqueueEvent(PendingEvent.Add(item));
    }

    internal void EnqueueAddRange(System.Collections.Immutable.ImmutableList<T> items)
    {
        EnqueueEvent(PendingEvent.AddRange(items));
    }

    internal bool EnqueueRemove(T item)
    {
        EnqueueEvent(PendingEvent.Remove(item));
        return true;
    }

    internal void EnqueueRemoveAt(int index)
    {
        EnqueueEvent(PendingEvent.RemoveAt<T>(index));
    }

    internal void EnqueueClear()
    {
        EnqueueEvent(PendingEvent.Clear<T>());
    }

    internal void EnqueueInsert(int index, T item)
    {
        EnqueueEvent(PendingEvent.Insert(index, item));
    }

    internal void EnqueueInsertRange(int index, System.Collections.Immutable.ImmutableList<T> items)
    {
        EnqueueEvent(PendingEvent.InsertRange(index, items));
    }

    /// <summary>Enqueues one <see cref="PendingEventType.Add"/> event per item of a range committed to the source collection.</summary>
    internal void EnqueueAddRangeAsSingleItemEvents(System.Collections.Immutable.ImmutableList<T> items)
    {
        if (items.IsEmpty)
            return;

        // The whole range is queued before anything is dispatched. A handler invoked while dispatching can modify the
        // source collection, and the events it enqueues must come after the ones of the range that is already committed.
        CountEnqueuedEvents(items.Count);
        foreach (var item in items)
        {
            _pendingEvents.Enqueue(PendingEvent.Add(item));
        }

        ProcessPendingEventsOrPost();
    }

    /// <summary>Enqueues one <see cref="PendingEventType.Insert"/> event per item of a range committed to the source collection.</summary>
    internal void EnqueueInsertRangeAsSingleItemEvents(int index, System.Collections.Immutable.ImmutableList<T> items)
    {
        if (items.IsEmpty)
            return;

        CountEnqueuedEvents(items.Count);
        foreach (var item in items)
        {
            _pendingEvents.Enqueue(PendingEvent.Insert(index, item));
            index++;
        }

        ProcessPendingEventsOrPost();
    }

    private void EnqueueEvent(PendingEvent<T> @event)
    {
        CountEnqueuedEvents(1);
        _pendingEvents.Enqueue(@event);
        ProcessPendingEventsOrPost();
    }

    /// <summary>Counts events that are about to be enqueued, so <see cref="HasPendingEvents"/> knows about them.</summary>
    /// <remarks>
    /// Every caller runs while the source collection holds its lock, which is also where the count is read, so the count
    /// and the content of the source collection stay consistent with each other.
    /// </remarks>
    private void CountEnqueuedEvents(int count)
    {
        Volatile.Write(ref _enqueuedEventCount, _enqueuedEventCount + count);
    }

    private void ProcessPendingEventsOrPost()
    {
        if (!_collection.IsOnSynchronizationContextThread())
        {
            if (!_isProcessingPending)
            {
                _isProcessingPending = true;
                try
                {
                    _synchronizationContext.Post(static state => ((DispatchedObservableCollection<T>)state!).ProcessPendingEvents(), this);
                }
                catch
                {
                    // The synchronization context refused the callback, so nothing will process the queue. The events stay
                    // queued and the flag is restored, so the next modification posts again and raises every pending
                    // notification as soon as the context accepts a callback.
                    _isProcessingPending = false;
                    throw;
                }
            }

            return;
        }

        ProcessPendingEvents();
    }

    private void ProcessPendingEvents()
    {
        // Only one drain at a time. A handler invoked below can modify the source collection, which enqueues new
        // events and re-enters this method on the same thread: dispatching them right away would apply them before
        // the events already queued and leave the view ordered differently from the source. The running loop picks
        // them up instead.
        if (Interlocked.Exchange(ref _isDraining, 1) is 1)
        {
            // Another thread owns the drain and picks up whatever is queued when it loops. The post that led here is
            // over, so the flag has to be cleared: it is only reset when a drain starts, and leaving it set when this
            // callback does nothing would stop every later modification from posting, wedging the dispatch for good.
            _isProcessingPending = false;
            return;
        }

        List<Exception>? exceptions = null;
        do
        {
            try
            {
                _isProcessingPending = false;
                while (_pendingEvents.TryDequeue(out var pendingEvent))
                {
                    var processedCount = Volatile.Read(ref _processedEventCount) + 1;
                    try
                    {
                        ApplyPendingEvent(pendingEvent);
                    }
                    catch (Exception ex)
                    {
                        // The view is updated before its handlers are notified, so a handler that throws doesn't undo
                        // the change. The remaining events are still applied to keep the view synchronized with the
                        // source collection, and the failures are reported once the queue is drained.
                        exceptions ??= [];
                        exceptions.Add(ex);
                    }
                    finally
                    {
                        // OnItemsMutated already counted the event unless it could not be applied at all. Counting it
                        // here as well keeps the queue from looking permanently behind in that case.
                        if (Volatile.Read(ref _processedEventCount) < processedCount)
                        {
                            Volatile.Write(ref _processedEventCount, processedCount);
                        }
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _isDraining, 0);
            }

            // Events enqueued while the drain was running are processed here instead of waiting for the next
            // modification, unless another thread took over the drain in the meantime.
        }
        while (!_pendingEvents.IsEmpty && Interlocked.Exchange(ref _isDraining, 1) is 0);

        if (exceptions is not null)
        {
            if (exceptions.Count is 1)
            {
                ExceptionDispatchInfo.Throw(exceptions[0]);
            }

            throw new AggregateException(exceptions);
        }
    }

    private void ApplyPendingEvent(PendingEvent<T> pendingEvent)
    {
        switch (pendingEvent.Type)
        {
            case PendingEventType.Add:
                AddItem(pendingEvent.Item);
                break;

            case PendingEventType.AddRange:
                AddItems(pendingEvent.Items!);
                break;

            case PendingEventType.Remove:
                RemoveItem(pendingEvent.Item);
                break;

            case PendingEventType.Clear:
                ClearItems();
                break;

            case PendingEventType.Insert:
                InsertItem(pendingEvent.Index, pendingEvent.Item);
                break;

            case PendingEventType.InsertRange:
                InsertItems(pendingEvent.Index, pendingEvent.Items!);
                break;

            case PendingEventType.RemoveAt:
                RemoveItemAt(pendingEvent.Index);
                break;

            case PendingEventType.Replace:
                ReplaceItem(pendingEvent.Index, pendingEvent.Item);
                break;

            case PendingEventType.Reset:
                Reset(pendingEvent.Items!);
                break;
        }
    }

    void IList<T>.Insert(int index, T item)
    {
        AssertIsOnSynchronizationContextThread();
        _collection.InsertFromObservableCollection(index, item);
    }

    void IList<T>.RemoveAt(int index)
    {
        AssertIsOnSynchronizationContextThread();
        _collection.RemoveAtFromObservableCollection(index);
    }

    void ICollection<T>.Add(T item)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        _collection.Add(item);
    }

    void ICollection<T>.Clear()
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        _collection.Clear();
    }

    bool ICollection<T>.Remove(T item)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        return _collection.Remove(item);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        AssertIsOnSynchronizationContextThread();
        lock (ItemsLock)
        {
            ((ICollection)Items).CopyTo(array, index);
        }
    }

    int IList.Add(object? value)
    {
        AssertIsOnSynchronizationContextThread();
        return _collection.AddFromObservableCollection(ConvertItem(value));
    }

    bool IList.Contains(object? value)
    {
        // The lookup targets the replica, which can still be behind the source collection
        AssertIsOnSynchronizationContextThread();
        if (ConcurrentObservableCollection<T>.IsCompatibleObject(value))
        {
            return Contains((T)value!);
        }

        return false;
    }

    void IList.Clear()
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        ((IList)_collection).Clear();
    }

    int IList.IndexOf(object? value)
    {
        AssertIsOnSynchronizationContextThread();
        if (ConcurrentObservableCollection<T>.IsCompatibleObject(value))
        {
            return IndexOf((T)value!);
        }

        return -1;
    }

    void IList.Insert(int index, object? value)
    {
        AssertIsOnSynchronizationContextThread();
        _collection.InsertFromObservableCollection(index, ConvertItem(value));
    }

    void IList.Remove(object? value)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        ((IList)_collection).Remove(value);
    }

    void IList.RemoveAt(int index)
    {
        AssertIsOnSynchronizationContextThread();
        _collection.RemoveAtFromObservableCollection(index);
    }
}
