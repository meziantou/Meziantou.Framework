using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Collections.Concurrent;

internal sealed class DispatchedObservableCollection<T> : ObservableCollectionBase<T>, IReadOnlyObservableCollection<T>, IList<T>, IList
{
    private readonly ConcurrentQueue<PendingEvent<T>> _pendingEvents = new();
    private readonly ConcurrentObservableCollection<T> _collection;
    private readonly SynchronizationContext _synchronizationContext;

    private volatile bool _isProcessingPending;
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

    public int Count
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return Items.Count;
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
            // it will immediately modify both collections as we are on the synchronization context thread
            AssertIsOnSynchronizationContextThread();
            _collection[index] = (T)value!;
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
            // it will immediately modify both collections as we are on the synchronization context thread
            AssertIsOnSynchronizationContextThread();
            _collection[index] = value;
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        AssertIsOnSynchronizationContextThread();
        return Items.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void CopyTo(T[] array, int arrayIndex)
    {
        AssertIsOnSynchronizationContextThread();
        Items.CopyTo(array, arrayIndex);
    }

    public int IndexOf(T item)
    {
        AssertIsOnSynchronizationContextThread();
        return Items.IndexOf(item);
    }

    public bool Contains(T item)
    {
        AssertIsOnSynchronizationContextThread();
        return Items.Contains(item);
    }

    public T this[int index]
    {
        get
        {
            AssertIsOnSynchronizationContextThread();
            return Items[index];
        }
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

        foreach (var item in items)
        {
            _pendingEvents.Enqueue(PendingEvent.Insert(index, item));
            index++;
        }

        ProcessPendingEventsOrPost();
    }

    private void EnqueueEvent(PendingEvent<T> @event)
    {
        _pendingEvents.Enqueue(@event);
        ProcessPendingEventsOrPost();
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
            return;

        List<Exception>? exceptions = null;
        do
        {
            try
            {
                _isProcessingPending = false;
                while (_pendingEvents.TryDequeue(out var pendingEvent))
                {
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
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        _collection.Insert(index, item);
    }

    void IList<T>.RemoveAt(int index)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        _collection.RemoveAt(index);
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
        ((ICollection)Items).CopyTo(array, index);
    }

    int IList.Add(object? value)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        return ((IList)_collection).Add(value);
    }

    bool IList.Contains(object? value)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        return ((IList)_collection).Contains(value);
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
        return Items.IndexOf((T)value!);
    }

    void IList.Insert(int index, object? value)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        ((IList)_collection).Insert(index, value);
    }

    void IList.Remove(object? value)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        ((IList)_collection).Remove(value);
    }

    void IList.RemoveAt(int index)
    {
        // it will immediately modify both collections as we are on the synchronization context thread
        AssertIsOnSynchronizationContextThread();
        ((IList)_collection).RemoveAt(index);
    }
}
