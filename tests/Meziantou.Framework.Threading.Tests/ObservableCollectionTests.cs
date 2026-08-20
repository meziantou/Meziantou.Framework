using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using Meziantou.Framework.Collections.Concurrent;

namespace Meziantou.Framework.Tests.Collections.Concurrent;

public sealed partial class ObservableCollectionTests : IDisposable
{
    private readonly SynchronizationContext? _previousSynchronizationContext = SynchronizationContext.Current;
    private readonly SynchronizationContext _synchronizationContext = new();

    public ObservableCollectionTests()
    {
        // The collection raises its notifications synchronously when the current thread is the one
        // associated with its synchronization context, which is what the assertions below rely on.
        SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
    }

    public void Dispose()
    {
        SynchronizationContext.SetSynchronizationContext(_previousSynchronizationContext);
    }

    public enum CollectionKind
    {
        Concurrent,
        Observable,
        BuiltIn,
    }

    public static IEnumerable<object[]> GetCollections
    {
        get
        {
            yield return new object[] { CollectionKind.Concurrent };
            yield return new object[] { CollectionKind.Observable };
            yield return new object[] { CollectionKind.BuiltIn };
        }
    }

    private ConcurrentObservableCollection<T> CreateCollection<T>()
    {
        return new ConcurrentObservableCollection<T>(_synchronizationContext);
    }

    private IList<int> CreateCollection(CollectionKind kind)
    {
        return kind switch
        {
            CollectionKind.Concurrent => CreateCollection<int>(),
            CollectionKind.Observable => (IList<int>)CreateCollection<int>().AsObservable,
            CollectionKind.BuiltIn => new System.Collections.ObjectModel.ObservableCollection<int>(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static object GetObservableCollection<T>(IList<T> collection)
    {
        if (collection is ConcurrentObservableCollection<T> result)
            return result.AsObservable;

        if (collection is DispatchedObservableCollection<T> dispatched)
            return dispatched;

        return collection;
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Add(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.Add(1);
        Assert.Equal([1], collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedAddItem(1);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Remove(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);
        collection.Add(2);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.Remove(1);
        Assert.Equal([2], collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedRemoveItem(1);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void RemoveAt(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.RemoveAt(0);
        Assert.Equal([2, 3], collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedRemoveItem(1);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Insert(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.Insert(index: 0, item: 1);
        Assert.Equal([1], collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedAddItem(1);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Clear(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.Clear();
        Assert.Empty(collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedReset();
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Indexer_Set(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection[0] = 2;
        Assert.Equal([2], collection.ToList());
        eventAssert.AssertPropertyChanged("Item[]");
        eventAssert.AssertCollectionChangedReplace(oldValue: 1, newValue: 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddRange(bool supportRangeNotifications)
    {
        var collection = CreateCollection<int>();
        collection.SupportRangeNotifications = supportRangeNotifications;

        collection.AddRange(0, 1, 2);

        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.AddRange(3, 4, 5);
        Assert.Equal([0, 1, 2, 3, 4, 5], collection.ToList());
        Assert.Equal([0, 1, 2, 3, 4, 5], collection.AsObservable.ToList());

        if (supportRangeNotifications)
        {
            eventAssert.AssertCollectionChangedAddItems([3, 4, 5], startIndex: 3);
        }
        else
        {
            Assert.All(eventAssert.CollectionChangedArgs.Select(e => e.Action), action => Assert.Equal(NotifyCollectionChangedAction.Add, action));
            Assert.Collection(eventAssert.CollectionChangedArgs,
                e => { Assert.Equal(NotifyCollectionChangedAction.Add, e.Action); Assert.Equal(3, e.NewStartingIndex); Assert.Equal([3], e.NewItems!.Cast<int>().ToArray()); },
                e => { Assert.Equal(NotifyCollectionChangedAction.Add, e.Action); Assert.Equal(4, e.NewStartingIndex); Assert.Equal([4], e.NewItems!.Cast<int>().ToArray()); },
                e => { Assert.Equal(NotifyCollectionChangedAction.Add, e.Action); Assert.Equal(5, e.NewStartingIndex); Assert.Equal([5], e.NewItems!.Cast<int>().ToArray()); });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertRange(bool supportRangeNotifications)
    {
        var collection = CreateCollection<int>();
        collection.SupportRangeNotifications = supportRangeNotifications;

        collection.AddRange(0, 1, 5);

        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.InsertRange(2, new[] { 2, 3, 4 });
        Assert.Equal([0, 1, 2, 3, 4, 5], collection.ToList());
        Assert.Equal([0, 1, 2, 3, 4, 5], collection.AsObservable.ToList());

        if (supportRangeNotifications)
        {
            eventAssert.AssertCollectionChangedAddItems([2, 3, 4], startIndex: 2);
        }
        else
        {
            Assert.All(eventAssert.CollectionChangedArgs.Select(e => e.Action), action => Assert.Equal(NotifyCollectionChangedAction.Add, action));
            Assert.Collection(eventAssert.CollectionChangedArgs,
                e => { Assert.Equal(NotifyCollectionChangedAction.Add, e.Action); Assert.Equal(2, e.NewStartingIndex); Assert.Equal([2], e.NewItems!.Cast<int>().ToArray()); },
                e => { Assert.Equal(NotifyCollectionChangedAction.Add, e.Action); Assert.Equal(3, e.NewStartingIndex); Assert.Equal([3], e.NewItems!.Cast<int>().ToArray()); },
                e => { Assert.Equal(NotifyCollectionChangedAction.Add, e.Action); Assert.Equal(4, e.NewStartingIndex); Assert.Equal([4], e.NewItems!.Cast<int>().ToArray()); });
        }
    }

    [Fact]
    public void Sort()
    {
        var collection = CreateCollection<int>();
        collection.AddRange(1, 0, 2);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.Sort();
        Assert.Equal([0, 1, 2], collection.ToList());
        Assert.Equal([0, 1, 2], collection.AsObservable.ToList());
        eventAssert.AssertPropertyChanged("Item[]");
        eventAssert.AssertCollectionChangedReset();
    }

    [Fact]
    public void StableSort()
    {
        var collection = CreateCollection<int>();
        collection.AddRange(1, 0, 2);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.StableSort();
        Assert.Equal([0, 1, 2], collection.ToList());
        Assert.Equal([0, 1, 2], collection.AsObservable.ToList());
        eventAssert.AssertPropertyChanged("Item[]");
        eventAssert.AssertCollectionChangedReset();
    }

    [Fact]
    public void StableSort_PreserveOrder()
    {
        var collection = CreateCollection<Sample>();
        for (var i = 0; i < 1000; i++)
        {
            collection.Add(new Sample(i * 2, "Value" + (i * 2).ToString("D5", CultureInfo.InvariantCulture)));
            collection.Add(new Sample((i * 2) + 1, "Value" + (i * 2).ToString("D5", CultureInfo.InvariantCulture)));
        }

        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        collection.StableSort(new SampleComparer()); // Compare by value

        Assert.Equal(collection, collection.OrderBy(item => item.Index));
        eventAssert.AssertPropertyChanged("Item[]");
        eventAssert.AssertCollectionChangedReset();
    }

    [Fact]
    public void AddWrongItemType()
    {
        var collection = (IList)CreateCollection<string>();
        collection.Add(null);
        collection.Add("");

        Assert.Throws<ArgumentException>(() => collection.Add(10));
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Contains_Struct_Null(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        Assert.False(((IList)collection).Contains(null));
    }

    [Fact]
    public void ChangesFromAnotherThreadAreNotifiedOnTheSynchronizationContext()
    {
        var context = new QueuedSynchronizationContext();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var collection = new ConcurrentObservableCollection<int>(context);
            var observable = collection.AsObservable;
            using var eventAssert = new EventAssert(observable);

            var thread = new Thread(() => collection.AddRange(1, 2, 3));
            thread.Start();
            thread.Join();

            Assert.Equal([1, 2, 3], collection.ToList());
            Assert.Empty(observable);
            Assert.Empty(eventAssert.CollectionChangedArgs);

            Assert.Equal(1, context.Run());

            Assert.Equal([1, 2, 3], observable.ToList());
            Assert.HasCount(3, eventAssert.CollectionChangedArgs);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    [Fact]
    public void ObservableCollectionCannotBeAccessedFromAnotherThread()
    {
        var observable = CreateCollection<int>().AsObservable;

        var thread = new Thread(() => Assert.Throws<InvalidOperationException>(() => observable.Count));
        thread.Start();
        thread.Join();
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        public override void Post(SendOrPostCallback d, object? state) => _callbacks.Enqueue((d, state));

        /// <summary>Runs the pending callbacks and returns how many were executed.</summary>
        public int Run()
        {
            var count = 0;
            while (_callbacks.TryDequeue(out var callback))
            {
                callback.Callback(callback.State);
                count++;
            }

            return count;
        }
    }

    private sealed record Sample(int Index, string Value);

    private sealed class SampleComparer : IComparer<Sample>
    {
        public int Compare(Sample? x, Sample? y)
        {
            if (ReferenceEquals(x, y))
                return 0;

            if (x is null)
                return -1;

            if (y is null)
                return 1;

            return StringComparer.Ordinal.Compare(x.Value, y.Value);
        }
    }

    private sealed class EventAssert : IDisposable
    {
        private readonly object _observedInstance;

        public List<NotifyCollectionChangedEventArgs> CollectionChangedArgs { get; } = [];
        public List<PropertyChangedEventArgs> PropertyChangedArgs { get; } = [];

        public EventAssert(object obj)
        {
            _observedInstance = obj;
            if (obj is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged += NotifyPropertyChanged_PropertyChanged;
            }

            if (obj is INotifyCollectionChanged notifyCollectionChanged)
            {
                notifyCollectionChanged.CollectionChanged += NotifyCollectionChanged_CollectionChanged;
            }
        }

        public void Dispose()
        {
            if (_observedInstance is INotifyPropertyChanged notifyPropertyChanged)
            {
                notifyPropertyChanged.PropertyChanged -= NotifyPropertyChanged_PropertyChanged;
            }

            if (_observedInstance is INotifyCollectionChanged notifyCollectionChanged)
            {
                notifyCollectionChanged.CollectionChanged -= NotifyCollectionChanged_CollectionChanged;
            }
        }

        private void NotifyCollectionChanged_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            CollectionChangedArgs.Add(e);
        }

        private void NotifyPropertyChanged_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            PropertyChangedArgs.Add(e);
        }

        public void AssertPropertyChanged(params string[] propertyNames)
        {
            Assert.Equal(propertyNames, PropertyChangedArgs.Select(e => e.PropertyName).ToList());
        }

        public void AssertCollectionChangedAddItem(object obj)
        {
            Assert.Single(CollectionChangedArgs);
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Add);
            Assert.NotNull(args.NewItems);
            Assert.Equal(obj, args.NewItems[0]);
            Assert.Equal(0, args.NewStartingIndex);
            Assert.Equal(-1, args.OldStartingIndex);
            Assert.Null(args.OldItems);
        }

        public void AssertCollectionChangedAddItems(object[] obj, int startIndex)
        {
            Assert.Single(CollectionChangedArgs);
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Add);
            Assert.NotNull(args.NewItems);
            Assert.Equal(obj, args.NewItems.OfType<object>());
            Assert.Equal(startIndex, args.NewStartingIndex);
            Assert.Equal(-1, args.OldStartingIndex);
            Assert.Null(args.OldItems);
        }

        public void AssertCollectionChangedRemoveItem(object obj)
        {
            Assert.Single(CollectionChangedArgs);
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Remove);
            Assert.NotNull(args.OldItems);
            Assert.Equal(obj, args.OldItems[0]);
            Assert.Equal(-1, args.NewStartingIndex);
            Assert.Equal(0, args.OldStartingIndex);
            Assert.Null(args.NewItems);
        }

        public void AssertCollectionChangedReset()
        {
            Assert.Single(CollectionChangedArgs);
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Reset);
            Assert.Equal(-1, args.NewStartingIndex);
            Assert.Equal(-1, args.OldStartingIndex);
            Assert.Null(args.NewItems);
            Assert.Null(args.OldItems);
        }

        public void AssertCollectionChangedReplace(object oldValue, object newValue)
        {
            Assert.Single(CollectionChangedArgs);
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Replace);
            Assert.NotNull(args.NewItems);
            Assert.NotNull(args.OldItems);
            Assert.Equal(newValue, args.NewItems[0]);
            Assert.Equal(oldValue, args.OldItems[0]);
            Assert.Equal(0, args.NewStartingIndex);
            Assert.Equal(0, args.OldStartingIndex);
        }
    }
}
