using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Windows;
using System.Windows.Threading;

namespace Meziantou.Framework.WPF.Collections
{
    /// <summary>
    /// Thread-safe collection. You can safely bind it to a WPF control using the property <see cref="AsObservable"/>.
    /// </summary>
    public sealed class ConcurrentObservableCollection<T> : IList<T>, IReadOnlyList<T>
    {
        private readonly Dispatcher _dispatcher;
        private readonly object _lock = new object();

        private ImmutableList<T> _items = ImmutableList<T>.Empty;
        private DispatchedObservableCollection<T> _observableCollection;

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
                            _observableCollection = new DispatchedObservableCollection<T>(_items, _dispatcher);
                        }
                    }
                }

                return _observableCollection;
            }
        }

        bool ICollection<T>.IsReadOnly => ((ICollection<T>)_items).IsReadOnly;

        public int Count => _items.Count;

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
                        _observableCollection[index] = value;
                    }
                }
            }
        }

        public void Add(T item)
        {
            lock (_lock)
            {
                _items = _items.Add(item);
                _observableCollection?.Add(item);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items = _items.Clear();
                _observableCollection?.Clear();
            }
        }

        public void Insert(int index, T item)
        {
            lock (_lock)
            {
                _items = _items.Insert(index, item);
                _observableCollection?.Insert(index, item);
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
                    _observableCollection?.Remove(item);
                    return true;
                }

                return false;
            }
        }

        public void RemoveAt(int index)
        {
            lock (_lock)
            {
                _items = _items.RemoveAt(index);
                _observableCollection?.RemoveAt(index);
            }
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
    }
}
