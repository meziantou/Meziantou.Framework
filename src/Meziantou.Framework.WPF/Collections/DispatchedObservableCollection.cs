using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;

namespace Meziantou.Framework.Windows.Collections
{
    public sealed class DispatchedObservableCollection<T> : ObservableCollectionBase<T>, IList<T>, IReadOnlyObservableCollection<T>
    {
        private readonly ConcurrentQueue<PendingEvent<T>> _pendingEvents = new ConcurrentQueue<PendingEvent<T>>();

        private readonly Dispatcher _dispatcher;
        private bool _isEventsWaitingToBeProcessed;

        public DispatchedObservableCollection(Dispatcher dispatcher)
            : this(null, dispatcher)
        {
        }

        public DispatchedObservableCollection(IEnumerable<T> items, Dispatcher dispatcher)
            : base(items)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        public T this[int index]
        {
            get => _items[index];
            set => EnqueueEvent(PendingEvent.Replace(index, value));
        }

        bool ICollection<T>.IsReadOnly => ((ICollection<T>)_items).IsReadOnly;

        public void Add(T item)
        {
            EnqueueEvent(PendingEvent.Add(item));
        }

        public bool Remove(T item)
        {
            EnqueueEvent(PendingEvent.Remove(item));
            return true;
        }

        public void RemoveAt(int index)
        {
            EnqueueEvent(PendingEvent.RemoveAt<T>(index));
        }

        public void Clear()
        {
            EnqueueEvent(PendingEvent.Clear<T>());
        }

        public void Insert(int index, T item)
        {
            EnqueueEvent(PendingEvent.Insert(index, item));
        }

        private void EnqueueEvent(PendingEvent<T> @event)
        {
            _pendingEvents.Enqueue(@event);
            ProcessPendingEvents();
        }

        private void ProcessPendingEvents()
        {
            if (!IsOnDispatcherThread())
            {
                if (!_isEventsWaitingToBeProcessed)
                {
                    _dispatcher.BeginInvoke((Action)ProcessPendingEvents);
                    _isEventsWaitingToBeProcessed = true;
                }
                return;
            }

            _isEventsWaitingToBeProcessed = false;
            while (_pendingEvents.TryDequeue(out var pendingEvent))
            {
                switch (pendingEvent.Type)
                {
                    case PendingEventType.Add:
                        AddItem(pendingEvent.Item);
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

                    case PendingEventType.RemoveAt:
                        RemoveItemAt(pendingEvent.Index);
                        break;

                    case PendingEventType.Replace:
                        ReplaceItem(pendingEvent.Index, pendingEvent.Item);
                        break;
                }
            }
        }

        private bool IsOnDispatcherThread()
        {
            return _dispatcher.Thread == Thread.CurrentThread;
        }
    }
}
