using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Collections.Concurrent;

internal sealed class DispatchedObservableCollection<T> : ObservableCollectionBase<T>, IReadOnlyObservableCollection<T>, IList<T>, IList
{
    private readonly ConcurrentQueue<PendingEvent<T>> _pendingEvents = new();
    private readonly ConcurrentObservableCollection<T> _collection;
    private readonly SynchronizationContext _synchronizationContext;

    // Both flags are ints driven by Interlocked operations, which are full fences. Each one guards a check-then-act
    // against the queue: a drain clears the flag and then reads the queue, while a producer writes the queue and then
    // reads the flag. With plain volatile accesses, the store and the following load can be reordered, so the drain
    // could miss the event while the producer still sees the flag set and skips the post, leaving the event queued
    // until the next modification.
    private int _isPostPending;
    private int _isDraining;

    // The thread running the drain, while it runs. The default SynchronizationContext runs its callbacks on the thread
    // pool without installing itself as the current context, so the handlers would otherwise be refused access to the
    // view they are notified for. The drain is serialized, so this thread is the only one using the view meanwhile.
    private Thread? _drainingThread;

    public DispatchedObservableCollection(ConcurrentObservableCollection<T> collection, SynchronizationContext synchronizationContext)
        : base(collection)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _synchronizationContext = synchronizationContext ?? throw new ArgumentNullException(nameof(synchronizationContext));
    }

    private bool IsOnSynchronizationContextThread()
    {
        return _collection.IsOnSynchronizationContextThread() || Volatile.Read(ref _drainingThread) == Thread.CurrentThread;
    }

    private void AssertIsOnSynchronizationContextThread()
    {
        if (!IsOnSynchronizationContextThread())
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
            AssertIsOnSynchronizationContextThread();
            _collection.SetItemFromView(index, ConcurrentObservableCollection<T>.ConvertValue(value));
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
            _collection.SetItemFromView(index, value);
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

        PostIfNotOnSynchronizationContextThread();
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

        PostIfNotOnSynchronizationContextThread();
    }

    private void EnqueueEvent(PendingEvent<T> @event)
    {
        _pendingEvents.Enqueue(@event);
        PostIfNotOnSynchronizationContextThread();
    }

    /// <summary>Posts the processing of the queued events to the synchronization context, unless the current thread
    /// can raise them itself. Called while the source collection holds its lock, so posts are serialized with the
    /// modifications, and a post that the context refused is retried by the next modification.</summary>
    private void PostIfNotOnSynchronizationContextThread()
    {
        if (IsOnSynchronizationContextThread())
            return;

        if (Interlocked.CompareExchange(ref _isPostPending, 1, 0) is not 0)
            return;

        try
        {
            _synchronizationContext.Post(static state => ((DispatchedObservableCollection<T>)state!).ProcessPendingEvents(), this);
        }
        catch
        {
            // The synchronization context refused the callback, so nothing will process the queue. The events stay
            // queued and the flag is restored, so the next modification posts again and raises every pending
            // notification as soon as the context accepts a callback.
            Interlocked.Exchange(ref _isPostPending, 0);
            throw;
        }
    }

    /// <summary>Raises the queued events when the current thread is the synchronization context thread.</summary>
    /// <remarks>Must be called once the source collection has released its lock: the handlers may block on another
    /// thread that modifies the collection, which would otherwise deadlock.</remarks>
    internal void ProcessPendingEventsIfOnSynchronizationContextThread()
    {
        if (IsOnSynchronizationContextThread())
        {
            ProcessPendingEvents();
        }
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
            Volatile.Write(ref _drainingThread, Thread.CurrentThread);
            try
            {
                Interlocked.Exchange(ref _isPostPending, 0);
                while (_pendingEvents.TryDequeue(out var pendingEvent))
                {
                    // The view is updated before its handlers are notified, so a handler that throws doesn't undo
                    // the change. The remaining notifications and events are still raised and applied to keep the
                    // view synchronized with the source collection, and the failures are reported once the queue
                    // is drained.
                    ApplyPendingEvent(pendingEvent, ref exceptions);
                }
            }
            finally
            {
                Volatile.Write(ref _drainingThread, value: null);
                Interlocked.Exchange(ref _isDraining, 0);
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

    private void ApplyPendingEvent(PendingEvent<T> pendingEvent, ref List<Exception>? exceptions)
    {
        switch (pendingEvent.Type)
        {
            case PendingEventType.Add:
                AddItem(pendingEvent.Item, ref exceptions);
                break;

            case PendingEventType.AddRange:
                AddItems(pendingEvent.Items!, ref exceptions);
                break;

            case PendingEventType.Clear:
                ClearItems(ref exceptions);
                break;

            case PendingEventType.Insert:
                InsertItem(pendingEvent.Index, pendingEvent.Item, ref exceptions);
                break;

            case PendingEventType.InsertRange:
                InsertItems(pendingEvent.Index, pendingEvent.Items!, ref exceptions);
                break;

            case PendingEventType.RemoveAt:
                RemoveItemAt(pendingEvent.Index, ref exceptions);
                break;

            case PendingEventType.Replace:
                ReplaceItem(pendingEvent.Index, pendingEvent.Item, ref exceptions);
                break;

            case PendingEventType.Reset:
                Reset(pendingEvent.Items!, ref exceptions);
                break;
        }
    }

    /// <summary>
    /// Converts an index of the view into the index of the same position in the source collection, which can be ahead of
    /// the view by the queued events. Must be called on the synchronization context thread while the source collection
    /// holds its lock: no event can be queued or applied meanwhile, so the queued events are exactly the difference
    /// between the view and the source collection.
    /// </summary>
    /// <param name="index">The index in the view.</param>
    /// <param name="isInsertion"><see langword="true"/> when <paramref name="index"/> designates a position between items, from 0 to <c>Count</c>; <see langword="false"/> when it designates an item.</param>
    /// <exception cref="InvalidOperationException">The item designated by <paramref name="index"/> was removed from the source collection, or the source collection was cleared or reordered, by a modification whose notification is still queued.</exception>
    internal int GetSourceIndex(int index, bool isInsertion)
    {
        var count = Items.Count;
        if ((uint)index > (uint)count || (!isInsertion && index == count))
            throw new ArgumentOutOfRangeException(nameof(index), index, "Index was out of range. Must be non-negative and less than the size of the collection.");

        foreach (var pendingEvent in _pendingEvents)
        {
            switch (pendingEvent.Type)
            {
                case PendingEventType.Add:
                    ShiftForInsertion(insertionIndex: count, insertedCount: 1);
                    break;

                case PendingEventType.AddRange:
                    ShiftForInsertion(insertionIndex: count, pendingEvent.Items!.Count);
                    break;

                case PendingEventType.Insert:
                    ShiftForInsertion(pendingEvent.Index, insertedCount: 1);
                    break;

                case PendingEventType.InsertRange:
                    ShiftForInsertion(pendingEvent.Index, pendingEvent.Items!.Count);
                    break;

                case PendingEventType.RemoveAt:
                    count--;
                    if (pendingEvent.Index < index)
                    {
                        index--;
                    }
                    else if (pendingEvent.Index == index && !isInsertion)
                    {
                        throw new InvalidOperationException("The item of the view was removed from the collection by a modification whose notification is still pending.");
                    }

                    break;

                case PendingEventType.Replace:
                    // The position still exists in the source collection, only its value changed
                    break;

                case PendingEventType.Clear:
                case PendingEventType.Reset:
                    throw new InvalidOperationException("The collection was cleared or reordered by a modification whose notification is still pending, so the index of the view doesn't match any position of the collection.");
            }
        }

        return index;

        void ShiftForInsertion(int insertionIndex, int insertedCount)
        {
            // Items inserted at the designated position are placed before it, as they are for an item
            count += insertedCount;
            if (insertionIndex <= index)
            {
                index += insertedCount;
            }
        }
    }

    void IList<T>.Insert(int index, T item)
    {
        AssertIsOnSynchronizationContextThread();
        _collection.InsertFromView(index, item);
    }

    void IList<T>.RemoveAt(int index)
    {
        AssertIsOnSynchronizationContextThread();
        _collection.RemoveAtFromView(index);
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
        _collection.InsertFromView(index, ConcurrentObservableCollection<T>.ConvertValue(value));
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
        _collection.RemoveAtFromView(index);
    }
}
