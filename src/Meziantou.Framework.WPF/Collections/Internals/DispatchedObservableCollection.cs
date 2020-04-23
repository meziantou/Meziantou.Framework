using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Threading;

namespace Meziantou.Framework.WPF.Collections
{
    internal sealed class DispatchedObservableCollection<T> : ObservableCollectionBase<T>, IReadOnlyObservableCollection<T>, IList<T>, IList
    {
        private readonly ConcurrentQueue<PendingEvent<T>> _pendingEvents = new ConcurrentQueue<PendingEvent<T>>();
        private readonly ConcurrentObservableCollection<T> _collection;
        private readonly Dispatcher _dispatcher;

        private bool _isDispatcherPending;

        public DispatchedObservableCollection(ConcurrentObservableCollection<T> collection, Dispatcher dispatcher)
            : base(collection)
        {
            _collection = collection ?? throw new ArgumentNullException(nameof(collection));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        private void AssertIsOnDispatcherThread()
        {
            if (!IsOnDispatcherThread())
            {
                throw new InvalidOperationException("The collection must be accessed from the dispatcher thread only. Current thread ID: " + Thread.CurrentThread.ManagedThreadId.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static void AssertType(object? value, string argumentName)
        {
            if (value is null || value is T)
                return;

            throw new ArgumentException($"value must be of type '{typeof(T).FullName}'", argumentName);
        }

        public int Count
        {
            get
            {
                AssertIsOnDispatcherThread();
                return _items.Count;
            }
        }

        bool ICollection<T>.IsReadOnly
        {
            get
            {
                AssertIsOnDispatcherThread();
                return ((ICollection<T>)_collection).IsReadOnly;
            }
        }

        int ICollection.Count
        {
            get
            {
                AssertIsOnDispatcherThread();
                return Count;
            }
        }

        object ICollection.SyncRoot
        {
            get
            {
                AssertIsOnDispatcherThread();
                return ((ICollection)_items).SyncRoot;
            }
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                AssertIsOnDispatcherThread();
                return ((ICollection)_items).IsSynchronized;
            }
        }

        bool IList.IsReadOnly
        {
            get
            {
                AssertIsOnDispatcherThread();
                return ((IList)_items).IsReadOnly;
            }
        }

        bool IList.IsFixedSize
        {
            get
            {
                AssertIsOnDispatcherThread();
                return ((IList)_items).IsFixedSize;
            }
        }

        object? IList.this[int index]
        {
            get
            {
                AssertIsOnDispatcherThread();
                return this[index];
            }

            set
            {
                // it will immediatly modify both collections as we are on the dispatcher thread
                AssertType(value, nameof(value));
                AssertIsOnDispatcherThread();
                _collection[index] = (T)value!;
            }
        }

        T IList<T>.this[int index]
        {
            get
            {
                AssertIsOnDispatcherThread();
                return this[index];
            }
            set
            {
                // it will immediatly modify both collections as we are on the dispatcher thread
                AssertIsOnDispatcherThread();
                _collection[index] = value;
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            AssertIsOnDispatcherThread();
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void CopyTo(T[] array, int arrayIndex)
        {
            AssertIsOnDispatcherThread();
            _items.CopyTo(array, arrayIndex);
        }

        public int IndexOf(T item)
        {
            AssertIsOnDispatcherThread();
            return _items.IndexOf(item);
        }

        public bool Contains(T item)
        {
            AssertIsOnDispatcherThread();
            return _items.Contains(item);
        }

        public T this[int index]
        {
            get
            {
                AssertIsOnDispatcherThread();
                return _items[index];
            }
        }

        internal void EnqueueReplace(int index, T value)
        {
            EnqueueEvent(PendingEvent.Replace(index, value));
        }

        internal void EnqueueAdd(T item)
        {
            EnqueueEvent(PendingEvent.Add(item));
        }

        internal void EnqueueAddRange(IEnumerable<T> items)
        {
            EnqueueEvents(items.Select(PendingEvent.Add));
        }

        internal bool EnqueueRemove(T item)
        {
            EnqueueEvent(PendingEvent.Remove(item));
            return true;
        }

        internal bool EnqueueRemoveRange(IEnumerable<T> items)
        {
            EnqueueEvents(items.Select(PendingEvent.Remove));
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

        private void EnqueueEvent(PendingEvent<T> @event)
        {
            _pendingEvents.Enqueue(@event);
            ProcessPendingEventsOrDispatch();
        }

        private void EnqueueEvents(IEnumerable<PendingEvent<T>> events)
        {
            foreach (var @event in events)
            {
                _pendingEvents.Enqueue(@event);
            }

            ProcessPendingEventsOrDispatch();
        }

        private void ProcessPendingEventsOrDispatch()
        {
            if (!IsOnDispatcherThread())
            {
                if (!_isDispatcherPending)
                {
                    _isDispatcherPending = true;
                    _dispatcher.BeginInvoke((Action)ProcessPendingEvents);
                }

                return;
            }

            ProcessPendingEvents();
        }

        private void ProcessPendingEvents()
        {
            _isDispatcherPending = false;
            foreach (var events in AccumulatePendingEvents())
            {
                switch (events[0].Type)
                {
                    case PendingEventType.Add:
                        if (events.Count == 1)
                            AddItem(events[0].Item);
                        else
                            AddItems(events.Select(e => e.Item));
                        break;

                    case PendingEventType.Remove:
                        if (events.Count == 1)
                            RemoveItem(events[0].Item);
                        else
                            RemoveItems(events.Select(e => e.Item));
                        break;

                    case PendingEventType.Clear:
                        ClearItems();
                        break;

                    case PendingEventType.Insert:
                        foreach (var pendingEvent in events)
                        {
                            InsertItem(pendingEvent.Index, pendingEvent.Item);
                        }
                        break;

                    case PendingEventType.RemoveAt:
                        foreach (var pendingEvent in events)
                        {
                            RemoveItemAt(pendingEvent.Index);
                        }
                        break;

                    case PendingEventType.Replace:
                        foreach (var pendingEvent in events)
                        {
                            ReplaceItem(pendingEvent.Index, pendingEvent.Item);
                        }
                        break;
                }
            }
        }

        private IEnumerable<List<PendingEvent<T>>> AccumulatePendingEvents()
        {
            var index = 0;
            var list = new List<PendingEvent<T>>();
            while (_pendingEvents.TryDequeue(out var pendingEvent))
            {
                if (list.Count > 0 && list[list.Count - 1].Type != pendingEvent.Type)
                {
                    yield return list.GetRange(index, list.Count - index);
                    index = list.Count;
                }

                list.Add(pendingEvent);
            }

            if (index != list.Count)
            {
                yield return list.GetRange(index, list.Count - index);
            }
        }

        private bool IsOnDispatcherThread()
        {
            return _dispatcher.Thread == Thread.CurrentThread;
        }

        void IList<T>.Insert(int index, T item)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertIsOnDispatcherThread();
            _collection.Insert(index, item);
        }

        void IList<T>.RemoveAt(int index)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertIsOnDispatcherThread();
            _collection.RemoveAt(index);
        }

        void ICollection<T>.Add(T item)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertIsOnDispatcherThread();
            _collection.Add(item);
        }

        void ICollection<T>.Clear()
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertIsOnDispatcherThread();
            _collection.Clear();
        }

        bool ICollection<T>.Remove(T item)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertIsOnDispatcherThread();
            return _collection.Remove(item);
        }

        void ICollection.CopyTo(Array array, int index)
        {
            ((ICollection)_items).CopyTo(array, index);
        }

        int IList.Add(object? value)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertType(value, nameof(value));
            AssertIsOnDispatcherThread();
            return ((IList)_collection).Add(value);
        }

        bool IList.Contains(object? value)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertType(value, nameof(value));
            AssertIsOnDispatcherThread();
            return ((IList)_collection).Contains(value);
        }

        void IList.Clear()
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertIsOnDispatcherThread();
            ((IList)_collection).Clear();
        }

        int IList.IndexOf(object? value)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertType(value, nameof(value));
            AssertIsOnDispatcherThread();
            return _items.IndexOf((T)value!);
        }

        void IList.Insert(int index, object? value)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertType(value, nameof(value));
            AssertIsOnDispatcherThread();
            ((IList)_collection).Insert(index, value);
        }

        void IList.Remove(object? value)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertType(value, nameof(value));
            AssertIsOnDispatcherThread();
            ((IList)_collection).Remove(value);
        }

        void IList.RemoveAt(int index)
        {
            // it will immediatly modify both collections as we are on the dispatcher thread
            AssertIsOnDispatcherThread();
            ((IList)_collection).RemoveAt(index);
        }
    }
}
