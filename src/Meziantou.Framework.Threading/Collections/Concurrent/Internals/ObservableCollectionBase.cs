using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Meziantou.Framework.Collections.Concurrent;

internal abstract class ObservableCollectionBase<T> : INotifyCollectionChanged, INotifyPropertyChanged
{
    // A SynchronizationContext is not required to run its callbacks on a single thread, so the items can be read while
    // the pending events are applied. They are held as an immutable snapshot published through a fence: a reader takes
    // the reference once and walks a list nobody can mutate, and the drain, the only writer and never running twice at
    // a time, swaps the reference. That keeps the reads free of locks and lets an enumerator outlive the call that
    // created it. It also matches how ConcurrentObservableCollection<T> stores its own items.
    private ImmutableList<T> _items;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private protected ImmutableList<T> Items
    {
        get => Volatile.Read(ref _items);
        private set => Volatile.Write(ref _items, value);
    }

    protected ObservableCollectionBase()
    {
        _items = ImmutableList<T>.Empty;
    }

    protected ObservableCollectionBase(IEnumerable<T> items)
    {
        _items = items is null ? ImmutableList<T>.Empty : ImmutableList.CreateRange(items);
    }

    /// <summary>
    /// Called after <see cref="Items"/> has been changed and before the change is notified, so a handler observes a
    /// collection that is already up to date.
    /// </summary>
    private protected virtual void OnItemsMutated()
    {
    }

    protected void ReplaceItem(int index, T item)
    {
        var items = Items;
        var oldItem = items[index];
        Items = items.SetItem(index, item);

        OnItemsMutated();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, index));
    }

    protected void InsertItem(int index, T item)
    {
        Items = Items.Insert(index, item);

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    protected void InsertItems(int index, ImmutableList<T> items)
    {
        Items = Items.InsertRange(index, items);

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, index));
    }

    protected void AddItem(T item)
    {
        var items = Items;
        var index = items.Count;
        Items = items.Add(item);

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    protected void AddItems(ImmutableList<T> items)
    {
        var currentItems = Items;
        var index = currentItems.Count;
        Items = currentItems.AddRange(items);

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, index));
    }

    protected void RemoveItemAt(int index)
    {
        var items = Items;
        var item = items[index];
        Items = items.RemoveAt(index);

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index));
    }

    protected bool RemoveItem(T item)
    {
        var items = Items;
        var index = items.IndexOf(item);
        if (index >= 0)
        {
            Items = items.RemoveAt(index);
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
        Items = ImmutableList<T>.Empty;

        OnItemsMutated();
        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        CollectionChanged?.Invoke(this, EventArgsCache.ResetCollectionChanged);
    }

    protected void Reset(ImmutableList<T> items)
    {
        Items = items;

        OnItemsMutated();
        OnIndexerPropertyChanged();
        OnCollectionChanged(EventArgsCache.ResetCollectionChanged);
    }

    private void OnCountPropertyChanged() => OnPropertyChanged(EventArgsCache.CountPropertyChanged);
    private void OnIndexerPropertyChanged() => OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);
    protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs args) => CollectionChanged?.Invoke(this, args);
}
