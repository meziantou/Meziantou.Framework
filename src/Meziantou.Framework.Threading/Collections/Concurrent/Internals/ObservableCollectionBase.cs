using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Meziantou.Framework.Collections.Concurrent;

/// <remarks>
/// Each modification raises up to three notifications: <c>Count</c>, <c>Item[]</c> and <see cref="CollectionChanged"/>.
/// A handler that throws doesn't prevent the following notifications of the modification from being raised: the exceptions
/// are added to the list provided by the caller, which reports them once every notification has been raised. The handlers
/// subscribed to the same event after the one that threw are not invoked, as with any multicast delegate.
/// </remarks>
internal abstract class ObservableCollectionBase<T> : INotifyCollectionChanged, INotifyPropertyChanged
{
    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private protected List<T> Items { get; }

    protected ObservableCollectionBase(IEnumerable<T> items)
    {
        Items = [.. items];
    }

    protected void ReplaceItem(int index, T item, ref List<Exception>? exceptions)
    {
        var oldItem = Items[index];
        Items[index] = item;

        RaiseNotifications(raiseCountChanged: false, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, item, oldItem, index), ref exceptions);
    }

    protected void InsertItem(int index, T item, ref List<Exception>? exceptions)
    {
        Items.Insert(index, item);

        RaiseNotifications(raiseCountChanged: true, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index), ref exceptions);
    }

    protected void InsertItems(int index, ImmutableList<T> items, ref List<Exception>? exceptions)
    {
        Items.InsertRange(index, items);

        RaiseNotifications(raiseCountChanged: true, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, index), ref exceptions);
    }

    protected void AddItem(T item, ref List<Exception>? exceptions)
    {
        var index = Items.Count;
        Items.Add(item);

        RaiseNotifications(raiseCountChanged: true, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index), ref exceptions);
    }

    protected void AddItems(ImmutableList<T> items, ref List<Exception>? exceptions)
    {
        var index = Items.Count;
        Items.AddRange(items);

        RaiseNotifications(raiseCountChanged: true, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items, index), ref exceptions);
    }

    protected void RemoveItemAt(int index, ref List<Exception>? exceptions)
    {
        var item = Items[index];
        Items.RemoveAt(index);

        RaiseNotifications(raiseCountChanged: true, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item, index), ref exceptions);
    }

    protected void ClearItems(ref List<Exception>? exceptions)
    {
        Items.Clear();

        RaiseNotifications(raiseCountChanged: true, EventArgsCache.ResetCollectionChanged, ref exceptions);
    }

    protected void Reset(ImmutableList<T> items, ref List<Exception>? exceptions)
    {
        Items.Clear();
        Items.AddRange(items);

        RaiseNotifications(raiseCountChanged: false, EventArgsCache.ResetCollectionChanged, ref exceptions);
    }

    private void RaiseNotifications(bool raiseCountChanged, NotifyCollectionChangedEventArgs args, ref List<Exception>? exceptions)
    {
        if (raiseCountChanged)
        {
            try
            {
                OnPropertyChanged(EventArgsCache.CountPropertyChanged);
            }
            catch (Exception ex)
            {
                (exceptions ??= []).Add(ex);
            }
        }

        try
        {
            OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);
        }
        catch (Exception ex)
        {
            (exceptions ??= []).Add(ex);
        }

        try
        {
            OnCollectionChanged(args);
        }
        catch (Exception ex)
        {
            (exceptions ??= []).Add(ex);
        }
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);
    protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs args) => CollectionChanged?.Invoke(this, args);
}
