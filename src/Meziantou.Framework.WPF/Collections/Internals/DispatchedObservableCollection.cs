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
        private BatchMode? _batchMode;

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

        public IDisposable BeginBatch(BatchMode mode)
        {
            if (_batchMode != null)
                throw new InvalidOperationException("Collection already in batch mode");

            _batchMode = mode;
            return new BatchModeScope(() =>
            {
                ProcessPendingEventsOrDispatch(forceProcess: true);
                _batchMode = null;
            });
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

        private void ProcessPendingEventsOrDispatch(bool forceProcess = false)
        {
            if (!forceProcess && _batchMode != null)
                return;

            if (!IsOnDispatcherThread())
            {
                if (!_isDispatcherPending)
                {
                    _isDispatcherPending = true;
                    var mode = _batchMode;
                    _dispatcher.BeginInvoke(new Action(() => ProcessPendingEvents(mode)));
                }

                return;
            }

            ProcessPendingEvents(_batchMode);
        }

        private void ProcessPendingEvents(BatchMode? batchMode)
        {
            _isDispatcherPending = false;
            var raiseEvents = batchMode != BatchMode.Reset;
            while (_pendingEvents.TryDequeue(out var pendingEvent))
            {
                switch (pendingEvent.Type)
                {
                    case PendingEventType.Add:
                        AddItem(pendingEvent.Item, raiseEvents);
                        break;

                    case PendingEventType.Remove:
                        RemoveItem(pendingEvent.Item, raiseEvents);
                        break;

                    case PendingEventType.Clear:
                        ClearItems(raiseEvents);
                        break;

                    case PendingEventType.Insert:
                        InsertItem(pendingEvent.Index, pendingEvent.Item, raiseEvents);
                        break;

                    case PendingEventType.RemoveAt:
                        RemoveItemAt(pendingEvent.Index, raiseEvents);
                        break;

                    case PendingEventType.Replace:
                        ReplaceItem(pendingEvent.Index, pendingEvent.Item, raiseEvents);
                        break;
                }
            }

            if (batchMode == BatchMode.Reset)
            {
                RaiseResetEvent();
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

        private class BatchModeScope : IDisposable
        {
            private readonly Action _action;

            public BatchModeScope(Action action)
            {
                _action = action ?? throw new ArgumentNullException(nameof(action));
            }

            public void Dispose()
            {
                _action();
            }
        }
    }
}
