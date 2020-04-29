using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Meziantou.Framework.WPF.Collections
{
    /// <summary>
    /// Thread-safe collection. You can safely bind it to a WPF control using the property <see cref="AsObservable"/>.
    /// </summary>
    public sealed class ConcurrentObservableCollection<T> : IList<T>, IReadOnlyList<T>, IList
    {
        private readonly Dispatcher _dispatcher;
        private readonly object _lock = new object();

        private ImmutableList<T> _items = ImmutableList<T>.Empty;
        private DispatchedObservableCollection<T>? _observableCollection;

        public ConcurrentObservableCollection()
            : this(GetCurrentDispatcher())
        {
        }

        public ConcurrentObservableCollection(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        private static Dispatcher GetCurrentDispatcher()
        {
            return Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public IReadOnlyObservableCollection<T> AsObservable
        {
            get
            {
                if (_observableCollection == null)
                {
                    lock (_lock)
                    {
                        if (_observableCollection == null)
                        {
                            _observableCollection = new DispatchedObservableCollection<T>(this, _dispatcher);
                        }
                    }
                }

                return _observableCollection;
            }
        }

        bool ICollection<T>.IsReadOnly => ((ICollection<T>)_items).IsReadOnly;

        public int Count => _items.Count;

        bool IList.IsReadOnly => ((IList)_items).IsReadOnly;

        bool IList.IsFixedSize => ((IList)_items).IsFixedSize;

        int ICollection.Count => Count;

        object ICollection.SyncRoot => ((ICollection)_items).SyncRoot;

        bool ICollection.IsSynchronized => ((ICollection)_items).IsSynchronized;

        object? IList.this[int index]
        {
            get => this[index];
            set
            {
                AssertType(value, nameof(value));
                this[index] = (T)value!;
            }
        }

        public T this[int index]
        {
            get => _items[index];
            set
            {
                lock (_lock)
                {
                    _items = _items.SetItem(index, value);
                    if (_observableCollection != null)
                    {
                        _observableCollection.EnqueueReplace(index, value);
                    }
                }
            }
        }

        public void Add(T item)
        {
            lock (_lock)
            {
                _items = _items.Add(item);
                _observableCollection?.EnqueueAdd(item);
            }
        }

        public void AddRange(IEnumerable<T> items)
        {
            lock (_lock)
            {
                var count = _items.Count;
                _items = _items.AddRange(items);
                if (count == _items.Count)
                    return;

                _observableCollection?.EnqueueAddRange(_items.GetRange(count, _items.Count - count));
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items = _items.Clear();
                _observableCollection?.EnqueueClear();
            }
        }

        public void Insert(int index, T item)
        {
            lock (_lock)
            {
                _items = _items.Insert(index, item);
                _observableCollection?.EnqueueInsert(index, item);
            }
        }

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

        public void RemoveRange(IEnumerable<T> items)
        {
            var itemsList = items.ToList();

            lock (_lock)
            {
                var count = _items.Count;
                _items = _items.RemoveRange(itemsList);
                if (count == _items.Count)
                    return;

                _observableCollection?.EnqueueRemoveRange(itemsList);
            }
        }

        public void RemoveAt(int index)
        {
            lock (_lock)
            {
                _items = _items.RemoveAt(index);
                _observableCollection?.EnqueueRemoveAt(index);
            }
        }

        /// <summary>
        /// Begin a batch of operations on the collection. No CollectionChanged event is raised during the batch.<br/>
        /// Batch modes:<br/>
        /// - <see cref="BatchMode.DeferEvents"/>: Call all events when disposed.<br/>
        /// - <see cref="BatchMode.CombineEvents"/>: Call all events when disposed and try to optimize events (add/remove events for same item are not called).<br/>
        /// - <see cref="BatchMode.ResetCollection"/>: Call reset event when disposed.
        /// </summary>
        public IDisposable? BeginBatch(BatchMode mode = BatchMode.DeferEvents)
        {
            return _observableCollection?.BeginBatch(mode);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int IndexOf(T item)
        {
            return _items.IndexOf(item);
        }

        public bool Contains(T item)
        {
            return _items.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _items.CopyTo(array, arrayIndex);
        }

        int IList.Add(object? value)
        {
            AssertType(value, nameof(value));
            var item = (T)value!;
            lock (_lock)
            {
                var index = _items.Count;
                _items = _items.Add(item);
                _observableCollection?.EnqueueAdd(item);
                return index;
            }
        }

        bool IList.Contains(object? value)
        {
            AssertType(value, nameof(value));
            return Contains((T)value!);
        }

        void IList.Clear()
        {
            Clear();
        }

        int IList.IndexOf(object? value)
        {
            AssertType(value, nameof(value));
            return IndexOf((T)value!);
        }

        void IList.Insert(int index, object? value)
        {
            AssertType(value, nameof(value));
            Insert(index, (T)value!);
        }

        void IList.Remove(object? value)
        {
            AssertType(value, nameof(value));
            Remove((T)value!);
        }

        void IList.RemoveAt(int index)
        {
            RemoveAt(index);
        }

        void ICollection.CopyTo(Array array, int index)
        {
            ((ICollection)_items).CopyTo(array, index);
        }

        private static void AssertType(object? value, string argumentName)
        {
            if (value is null || value is T)
                return;

            throw new ArgumentException($"value must be of type '{typeof(T).FullName}'", argumentName);
        }
    }
}
