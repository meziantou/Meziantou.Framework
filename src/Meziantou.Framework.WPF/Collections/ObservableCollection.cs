using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace Meziantou.Framework.Windows.Collections
{
    public sealed class ObservableCollection<T> : ObservableCollectionBase<T>, IList<T>, IReadOnlyObservableCollection<T>
    {
        private readonly Dispatcher _dispatcher;
        private DispatchedObservableCollection<T> _observableCollection;

        public ObservableCollection()
            : this(GetCurrentDispatcher())
        {
        }

        public ObservableCollection(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        private static Dispatcher GetCurrentDispatcher()
        {
            return Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public T this[int index]
        {
            get => _items[index];
            set
            {
                ReplaceItem(index, value);
                if (_observableCollection != null)
                {
                    _observableCollection[index] = value;
                }
            }
        }

        public IReadOnlyObservableCollection<T> AsSafeBindable
        {
            get
            {
                if (_observableCollection == null)
                {
                    _observableCollection = new DispatchedObservableCollection<T>(_items, _dispatcher);
                }

                return _observableCollection;
            }
        }

        bool ICollection<T>.IsReadOnly => ((ICollection<T>)_items).IsReadOnly;

        public void Add(T item)
        {
            AddItem(item);
            _observableCollection?.Add(item);
        }

        public void Clear()
        {
            ClearItems();
            _observableCollection?.Clear();
        }

        public void Insert(int index, T item)
        {
            InsertItem(index, item);
            _observableCollection?.Insert(index, item);
        }

        public bool Remove(T item)
        {
            if (RemoveItem(item))
            {
                _observableCollection?.Remove(item);
                return true;
            }

            return false;
        }

        public void RemoveAt(int index)
        {
            RemoveItemAt(index);
            _observableCollection?.RemoveAt(index);
        }
    }
}
