using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Meziantou.Framework.WPF.Collections
{
    internal abstract class ObservableCollectionBase<T> : INotifyCollectionChanged, INotifyPropertyChanged
    {
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        private protected readonly List<T> _items;

        protected ObservableCollectionBase()
        {
            _items = new List<T>();
        }

        protected ObservableCollectionBase(IEnumerable<T> items)
        {
            if (items == null)
            {
                _items = new List<T>();
            }
            else
            {
                _items = new List<T>(items);
            }
        }

        protected void ReplaceItem(int index, T item, bool raiseEvents)
        {
            var oldItem = _items[index];
            _items[index] = item;

            if (raiseEvents)
            {
                OnIndexerPropertyChanged();
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, index));
            }
        }

        protected void InsertItem(int index, T item, bool raiseEvents)
        {
            _items.Insert(index, item);

            if (raiseEvents)
            {
                OnCountPropertyChanged();
                OnIndexerPropertyChanged();
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
            }
        }

        protected void AddItem(T item, bool raiseEvents)
        {
            var index = _items.Count;
            _items.Add(item);

            if (raiseEvents)
            {
                OnCountPropertyChanged();
                OnIndexerPropertyChanged();
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
            }
        }

        protected void RemoveItemAt(int index, bool raiseEvents)
        {
            var item = _items[index];
            _items.RemoveAt(index);

            if (raiseEvents)
            {
                OnCountPropertyChanged();
                OnIndexerPropertyChanged();
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
            }
        }

        protected bool RemoveItem(T item, bool raiseEvents)
        {
            var index = _items.IndexOf(item);
            if (index < 0)
                return false;

            _items.RemoveAt(index);

            if (raiseEvents)
            {
                OnCountPropertyChanged();
                OnIndexerPropertyChanged();
                CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
            }

            return true;
        }

        protected void ClearItems(bool raiseEvents)
        {
            _items.Clear();

            if (raiseEvents)
            {
                RaiseResetEvent();
            }
        }

        protected void RaiseResetEvent()
        {
            OnCountPropertyChanged();
            OnIndexerPropertyChanged();
            CollectionChanged?.Invoke(this, EventArgsCache.ResetCollectionChanged);
        }

        private void OnCountPropertyChanged() => OnPropertyChanged(EventArgsCache.CountPropertyChanged);
        private void OnIndexerPropertyChanged() => OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);
        private void OnPropertyChanged(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);
    }
}
