using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Meziantou.Framework.Collections.Concurrent;

internal abstract class ObservableCollectionBase<T> : INotifyCollectionChanged, INotifyPropertyChanged
{
    // A SynchronizationContext is not required to run its callbacks on a single thread, so the items can be read while
    // the pending events are being applied. Every access to the list holds this lock. The notifications are raised
    // outside of it: a handler can call back into the collection, or block on a thread that needs the lock.
    private readonly Lock _itemsLock = new();

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Guards <see cref="Items"/>. Every read and every write of the list must hold it.</summary>
    private protected Lock ItemsLock => _itemsLock;

    private protected List<T> Items { get; }

    protected ObservableCollectionBase()
    {
        Items = [];
    }

    protected ObservableCollectionBase(IEnumerable<T> items)
    {
        if (items is null)
        {
            Items = [];
        }
        else
        {
            Items = new List<T>(items);
        }
    }

    /// <summary>
    /// Called after <see cref="Items"/> has been changed and before the change is notified, so a handler observes a
    /// collection that is already up to date.
    /// </summary>
    private protected virtual void OnItemsMutated()
    {
    }

    public void EnsureCapacity(int capacity)
    {
        lock (ItemsLock)
        {
            Items.EnsureCapacity(capacity);
        }
    }

    protected void ReplaceItem(int index, T item)
    {
        T oldItem;
        lock (ItemsLock)
        {
            oldItem = Items[index];
            Items[index] = item;
        }

        OnItemsMutated();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, index));
    }

    protected void InsertItem(int index, T item)
    {
        lock (ItemsLock)
        {
            Items.Insert(index, item);
        }

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    protected void InsertItems(int index, ImmutableList<T> items)
    {
        lock (ItemsLock)
        {
            Items.InsertRange(index, items);
        }

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, index));
    }

    protected void AddItem(T item)
    {
        int index;
        lock (ItemsLock)
        {
            index = Items.Count;
            Items.Add(item);
        }

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    protected void AddItems(ImmutableList<T> items)
    {
        int index;
        lock (ItemsLock)
        {
            index = Items.Count;
            Items.AddRange(items);
        }

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, index));
    }

    protected void RemoveItemAt(int index)
    {
        T item;
        lock (ItemsLock)
        {
            item = Items[index];
            Items.RemoveAt(index);
        }

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    protected bool RemoveItem(T item)
    {
        int index;
        lock (ItemsLock)
        {
            index = Items.IndexOf(item);
            if (index >= 0)
            {
                Items.RemoveAt(index);
            }
        }

        // The event is processed whether or not the item was there, so the hook runs in both cases
        OnItemsMutated();
        if (index < 0)
            return false;

        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
        return true;
    }

    protected void ClearItems()
    {
        lock (ItemsLock)
        {
            Items.Clear();
        }

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        CollectionChanged?.Invoke(this, EventArgsCache.ResetCollectionChanged);
    }

    protected void Reset(ImmutableList<T> items)
    {
        lock (ItemsLock)
        {
            Items.Clear();
            Items.AddRange(items);
        }

        OnItemsMutated();
        OnIndexerPropertyChanged();
        OnCollectionChanged(EventArgsCache.ResetCollectionChanged);
    }

    private void OnCountPropertyChanged() => OnPropertyChanged(EventArgsCache.CountPropertyChanged);
    private void OnIndexerPropertyChanged() => OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);
    protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs args) => CollectionChanged?.Invoke(this, args);
}
