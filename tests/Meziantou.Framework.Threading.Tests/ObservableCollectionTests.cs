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

    public enum UISynchronizationContextKind
    {
        Wpf,
        WindowsForms,
    }

    private static SynchronizationContext CreateUISynchronizationContext(UISynchronizationContextKind kind)
    {
        return kind switch
        {
            UISynchronizationContextKind.Wpf => new System.Windows.Threading.DispatcherSynchronizationContext(),
            UISynchronizationContextKind.WindowsForms => new System.Windows.Forms.WindowsFormsSynchronizationContext(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddRange_HandlerModifyingTheCollectionKeepsTheViewSynchronized(bool supportRangeNotifications)
    {
        var collection = CreateCollection<int>();
        collection.SupportRangeNotifications = supportRangeNotifications;
        var observable = collection.AsObservable;

        var reentered = false;
        var notifiedItems = new List<int>();
        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            notifiedItems.AddRange(e.NewItems!.Cast<int>());
            if (reentered)
                return;

            reentered = true;
            collection.Add(99);
        }

        observable.CollectionChanged += OnCollectionChanged;
        try
        {
            collection.AddRange(1, 2);
        }
        finally
        {
            observable.CollectionChanged -= OnCollectionChanged;
        }

        Assert.True(reentered);
        Assert.Equal([1, 2, 99], notifiedItems);
        Assert.Equal([1, 2, 99], collection.ToList());
        Assert.Equal([1, 2, 99], observable.ToList());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertRange_HandlerModifyingTheCollectionKeepsTheViewSynchronized(bool supportRangeNotifications)
    {
        var collection = CreateCollection<int>();
        collection.SupportRangeNotifications = supportRangeNotifications;
        collection.Add(0);
        var observable = collection.AsObservable;

        var reentered = false;
        var notifiedItems = new List<int>();
        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            notifiedItems.AddRange(e.NewItems!.Cast<int>());
            if (reentered)
                return;

            reentered = true;
            collection.Add(99);
        }

        observable.CollectionChanged += OnCollectionChanged;
        try
        {
            collection.InsertRange(0, new[] { 1, 2 });
        }
        finally
        {
            observable.CollectionChanged -= OnCollectionChanged;
        }

        Assert.True(reentered);
        Assert.Equal([1, 2, 99], notifiedItems);
        Assert.Equal([1, 2, 0, 99], collection.ToList());
        Assert.Equal([1, 2, 0, 99], observable.ToList());
    }

    [Fact]
    public void ReadmeExample_SynchronizationContext()
    {
        var synchronizationContext = _synchronizationContext;

        #region ConcurrentObservableCollection_SynchronizationContext
        var collection = new ConcurrentObservableCollection<string>(synchronizationContext);
        #endregion

        collection.Add("item");
        Assert.Equal(["item"], collection.AsObservable);
    }

    [Fact]
    public void ConcurrentObservableCollection_RangeNotificationsExample()
    {
        #region ConcurrentObservableCollection_RangeNotifications
        var collection = new ConcurrentObservableCollection<int>
        {
            // Raise one notification for AddRange and InsertRange instead of one per item
            SupportRangeNotifications = true,
        };

        var notifications = new List<NotifyCollectionChangedEventArgs>();
        collection.AsObservable.CollectionChanged += (sender, e) => notifications.Add(e);

        collection.AddRange(1, 2, 3);

        var notification = Assert.Single(notifications);
        Assert.Equal(NotifyCollectionChangedAction.Add, notification.Action);
        Assert.Equal([1, 2, 3], notification.NewItems!.Cast<int>().ToArray());
        #endregion
    }

    [Fact]
    public void Add_HandlerModifyingTheCollectionRaisesTheNotificationsInOrder()
    {
        var collection = CreateCollection<int>();
        var observable = collection.AsObservable;

        var notifiedItems = new List<int>();
        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            var item = e.NewItems!.Cast<int>().Single();
            notifiedItems.Add(item);
            if (item is 1)
            {
                collection.Add(99);
            }
        }

        observable.CollectionChanged += OnCollectionChanged;
        try
        {
            collection.Add(1);
        }
        finally
        {
            observable.CollectionChanged -= OnCollectionChanged;
        }

        Assert.Equal([1, 99], notifiedItems);
        Assert.Equal([1, 99], collection.ToList());
        Assert.Equal([1, 99], observable.ToList());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddRange_HandlerThrowingOnTheFirstItemStillRaisesTheRemainingNotifications(bool supportRangeNotifications)
    {
        var collection = CreateCollection<int>();
        collection.SupportRangeNotifications = supportRangeNotifications;
        var observable = collection.AsObservable;

        var raisedCount = 0;
        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            raisedCount++;
            if (raisedCount is 1)
                throw new InvalidOperationException("Handler failure");
        }

        observable.CollectionChanged += OnCollectionChanged;
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => collection.AddRange(1, 2, 3));
            Assert.Equal("Handler failure", exception.Message);
        }
        finally
        {
            observable.CollectionChanged -= OnCollectionChanged;
        }

        Assert.Equal(supportRangeNotifications ? 1 : 3, raisedCount);
        Assert.Equal([1, 2, 3], observable.ToList());

        collection.Add(4);
        Assert.Equal([1, 2, 3, 4], collection.ToList());
        Assert.Equal([1, 2, 3, 4], observable.ToList());
    }

    [Fact]
    public void AddRange_HandlerThrowingForEveryItemReportsAllTheExceptions()
    {
        var collection = CreateCollection<int>();
        var observable = collection.AsObservable;

        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => throw new InvalidOperationException("Handler failure");

        observable.CollectionChanged += OnCollectionChanged;
        try
        {
            var exception = Assert.Throws<AggregateException>(() => collection.AddRange(1, 2, 3));
            Assert.HasCount(3, exception.InnerExceptions);
            Assert.All(exception.InnerExceptions, item => Assert.IsType<InvalidOperationException>(item));
        }
        finally
        {
            observable.CollectionChanged -= OnCollectionChanged;
        }

        Assert.Equal([1, 2, 3], observable.ToList());
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

    [Fact]
    public void SyncRoot_Throws()
    {
        // It used to return the SyncRoot of the current immutable snapshot, a different object after every modification.
        var collection = (ICollection)CreateCollection<int>();

        Assert.Throws<NotSupportedException>(() => _ = collection.SyncRoot);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Contains_Struct_Null(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        Assert.False(((IList)collection).Contains(null));
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Contains_WrongItemType(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);

        Assert.False(((IList)collection).Contains("1"));
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void IndexOf_WrongItemType(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);

        Assert.Equal(-1, ((IList)collection).IndexOf("1"));
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void IndexOf_Struct_Null(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);

        Assert.Equal(-1, ((IList)collection).IndexOf(null));
    }

    [Fact]
    public void IndexOf_Null_ReferenceType()
    {
        var collection = CreateCollection<string?>();
        collection.AddRange("a", null);

        Assert.Equal(1, ((IList)collection).IndexOf(null));
        Assert.Equal(1, ((IList)collection.AsObservable).IndexOf(null));
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void SetItem_WrongItemType(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);

        Assert.Throws<ArgumentException>(() => ((IList)collection)[0] = "1");
        Assert.Equal([1], collection.ToList());
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void SetItem_Struct_Null(CollectionKind kind)
    {
        var collection = CreateCollection(kind);
        collection.Add(1);

        Assert.Throws<ArgumentNullException>(() => ((IList)collection)[0] = null);
        Assert.Equal([1], collection.ToList());
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
    public void ChangesAreNotifiedAfterTheSynchronizationContextRejectedAPost()
    {
        var context = new QueuedSynchronizationContext();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var collection = new ConcurrentObservableCollection<int>(context);
            var observable = collection.AsObservable;
            using var eventAssert = new EventAssert(observable);

            context.RejectPost = true;
            Assert.IsType<InvalidOperationException>(RunOnAnotherThread(() => collection.Add(1)));

            context.RejectPost = false;
            Assert.Null(RunOnAnotherThread(() => collection.Add(2)));

            // The notification of the first item was queued and is raised by the post of the second one
            Assert.Equal(1, context.Run());
            Assert.Equal([1, 2], observable.ToList());
            Assert.HasCount(2, eventAssert.CollectionChangedArgs);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    [Fact]
    public void HandlerCanWaitForAnotherThreadModifyingTheCollection()
    {
        var context = new QueuedSynchronizationContext();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var collection = new ConcurrentObservableCollection<int>(context);
            var observable = collection.AsObservable;

            // Handlers used to run while the collection held its lock, so a modification from another thread
            // blocked until the handler returned, and a handler waiting for it never did.
            var modifiedFromAnotherThread = false;
            observable.CollectionChanged += (sender, e) =>
            {
                if (e.NewItems?[0] is 1)
                {
                    var thread = new Thread(() => collection.Add(2));
                    thread.Start();
                    modifiedFromAnotherThread = thread.Join(TimeSpan.FromSeconds(30));
                }
            };

            collection.Add(1);

            Assert.True(modifiedFromAnotherThread);

            // The notification posted by the other thread was raised by the drain that was already running
            Assert.Equal([1, 2], observable.ToList());
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

    [Theory]
    [InlineData(UISynchronizationContextKind.Wpf)]
    [InlineData(UISynchronizationContextKind.WindowsForms)]
    public void ObservableCollectionCanBeAccessedWhenTheUISynchronizationContextInstanceChanged(UISynchronizationContextKind kind)
    {
        // WPF installs a new DispatcherSynchronizationContext instance bound to the same Dispatcher while running a
        // dispatcher operation, so the UI thread must be recognized even when the instance is not the captured one.
        var context = CreateUISynchronizationContext(kind);
        SynchronizationContext.SetSynchronizationContext(context);

        var collection = new ConcurrentObservableCollection<int>(context);
        var observable = collection.AsObservable;
        using var eventAssert = new EventAssert(observable);

        SynchronizationContext.SetSynchronizationContext(CreateUISynchronizationContext(kind));
        collection.Add(1);

        Assert.Equal([1], observable.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedAddItem(1);
    }

    [Fact]
    public void ObservableCollectionCannotBeAccessedWhenAnUnknownSynchronizationContextInstanceChanged()
    {
        // A SynchronizationContext is not thread-affine in general, so a context that is not a known UI one is
        // still matched by instance: running on the thread it was installed on doesn't mean it runs the callbacks
        var collection = CreateCollection<int>();
        var observable = collection.AsObservable;

        SynchronizationContext.SetSynchronizationContext(new SynchronizationContext());

        Assert.Throws<InvalidOperationException>(() => observable.Count);
    }

    [Fact]
    public void CollectionCreatedWithoutSynchronizationContextIsNotBoundToTheCreatingThread()
    {
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            // The default SynchronizationContext raises the notifications on the thread pool, so no thread owns the collection
            var observable = new ConcurrentObservableCollection<int>().AsObservable;

            Assert.Throws<InvalidOperationException>(() => observable.Count);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    [Fact]
    public void Add_PropertyChangedHandlerThrowingForCountStillRaisesTheOtherNotifications()
    {
        var collection = CreateCollection<int>();
        var observable = collection.AsObservable;
        using var eventAssert = new EventAssert(observable);

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is "Count")
                throw new InvalidOperationException("Handler failure");
        }

        observable.PropertyChanged += OnPropertyChanged;
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() => collection.Add(1));
            Assert.Equal("Handler failure", exception.Message);
        }
        finally
        {
            observable.PropertyChanged -= OnPropertyChanged;
        }

        Assert.Equal([1], observable.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedAddItem(1);
    }

    [Fact]
    public void PendingModifications_PropertyChangedHandlerThrowingStillRaisesEveryNotification()
    {
        WithQueuedSynchronizationContext(context =>
        {
            var collection = new ConcurrentObservableCollection<int>(context);
            var observable = collection.AsObservable;
            using var eventAssert = new EventAssert(observable);

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is "Count" or "Item[]")
                    throw new InvalidOperationException("Handler failure: " + e.PropertyName);
            }

            observable.PropertyChanged += OnPropertyChanged;
            try
            {
                Assert.Null(RunOnAnotherThread(() =>
                {
                    collection.Add(1);
                    collection.Insert(0, 2);
                    collection[1] = 3;
                }));

                var exception = Assert.Throws<AggregateException>(() => context.Run());

                // Two failures for each Add and Insert (Count and Item[]), one for the Replace (Item[])
                Assert.Equal(
                    ["Handler failure: Count", "Handler failure: Item[]", "Handler failure: Count", "Handler failure: Item[]", "Handler failure: Item[]"],
                    exception.InnerExceptions.Select(ex => ex.Message).ToList());
            }
            finally
            {
                observable.PropertyChanged -= OnPropertyChanged;
            }

            Assert.Equal([2, 3], observable.ToList());
            Assert.Equal(
                [NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Add, NotifyCollectionChangedAction.Replace],
                eventAssert.CollectionChangedArgs.Select(e => e.Action).ToList());
        });
    }

    [Fact]
    public void Remove_ReportsTheStoredItemInsteadOfTheSearchedValue()
    {
        var collection = CreateCollection<MutableKey>();
        var stored = new MutableKey(1);
        collection.Add(stored);
        collection.Add(new MutableKey(2));
        using var eventAssert = new EventAssert(collection.AsObservable);

        Assert.True(collection.Remove(new MutableKey(1)));

        var args = Assert.Single(eventAssert.CollectionChangedArgs);
        Assert.Same(stored, args.OldItems![0]);
        Assert.Equal(0, args.OldStartingIndex);
    }

    [Fact]
    public void Remove_DuplicateValuesRemovesTheFirstOne()
    {
        var collection = CreateCollection<MutableKey>();
        var first = new MutableKey(1);
        var second = new MutableKey(1);
        collection.AddRange(new MutableKey(0), first, second);
        using var eventAssert = new EventAssert(collection.AsObservable);

        Assert.True(collection.Remove(new MutableKey(1)));

        var args = Assert.Single(eventAssert.CollectionChangedArgs);
        Assert.Same(first, args.OldItems![0]);
        Assert.Equal(1, args.OldStartingIndex);
        Assert.Same(second, collection[1]);
        Assert.Equal(collection.ToList(), collection.AsObservable.ToList());
    }

    [Fact]
    public void Remove_ReplaysTheCommittedPositionWhenTheSearchedValueChangesBeforeTheNotification()
    {
        WithQueuedSynchronizationContext(context =>
        {
            var a = new MutableKey(1);
            var b = new MutableKey(2);
            var collection = new ConcurrentObservableCollection<MutableKey>(context);
            collection.AddRange(a, b);
            var observable = collection.AsObservable;
            using var eventAssert = new EventAssert(observable);

            var probe = new MutableKey(1);
            Assert.Null(RunOnAnotherThread(() => Assert.True(collection.Remove(probe))));
            probe.Key = 2;
            context.Run();

            Assert.Equal([b], collection.ToList());
            Assert.Equal([b], observable.ToList());
            Assert.Same(a, Assert.Single(eventAssert.CollectionChangedArgs).OldItems![0]);
        });
    }

    [Fact]
    public void Remove_EqualsModifyingTheCollectionThrows()
    {
        var collection = CreateCollection<ReentrantEquals>();
        var observable = collection.AsObservable;
        void AddItem() => collection.Add(new ReentrantEquals());
        collection.Add(new ReentrantEquals { OnEquals = AddItem });

        Assert.Throws<InvalidOperationException>(() => collection.Remove(new ReentrantEquals { OnEquals = AddItem }));

        // The item added by Equals is kept, and the view matches the collection
        Assert.HasCount(2, collection);
        Assert.Equal(collection.ToList(), observable.ToList(), ReferenceEqualityComparer.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddRange_EnumerationModifyingTheCollectionKeepsTheModification(bool supportRangeNotifications)
    {
        var collection = CreateCollection<int>();
        collection.SupportRangeNotifications = supportRangeNotifications;
        collection.Add(0);
        var observable = collection.AsObservable;

        IEnumerable<int> Items()
        {
            yield return 1;
            collection.Add(99);
            yield return 2;
        }

        collection.AddRange(Items());

        Assert.Equal([0, 99, 1, 2], collection.ToList());
        Assert.Equal([0, 99, 1, 2], observable.ToList());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertRange_EnumerationModifyingTheCollectionKeepsTheModification(bool supportRangeNotifications)
    {
        var collection = CreateCollection<int>();
        collection.SupportRangeNotifications = supportRangeNotifications;
        collection.Add(0);
        var observable = collection.AsObservable;

        IEnumerable<int> Items()
        {
            yield return 1;
            collection.Add(99);
            yield return 2;
        }

        collection.InsertRange(1, Items());

        Assert.Equal([0, 1, 2, 99], collection.ToList());
        Assert.Equal([0, 1, 2, 99], observable.ToList());
    }

    [Fact]
    public void AddRange_ThrowingEnumerationDoesNotModifyTheCollection()
    {
        var collection = CreateCollection<int>();
        collection.Add(0);
        var observable = collection.AsObservable;
        using var eventAssert = new EventAssert(observable);

        IEnumerable<int> Items()
        {
            yield return 1;
            throw new InvalidOperationException("Enumeration failure");
        }

        Assert.Throws<InvalidOperationException>(() => collection.AddRange(Items()));
        Assert.Throws<InvalidOperationException>(() => collection.InsertRange(0, Items()));

        Assert.Equal([0], collection.ToList());
        Assert.Equal([0], observable.ToList());
        Assert.Empty(eventAssert.CollectionChangedArgs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sort_ComparerModifyingTheCollectionThrows(bool stable)
    {
        var collection = CreateCollection<int>();
        collection.AddRange(2, 1);
        var observable = collection.AsObservable;

        var modified = false;
        var comparer = Comparer<int>.Create((x, y) =>
        {
            if (!modified)
            {
                modified = true;
                collection.Add(0);
            }

            return x.CompareTo(y);
        });

        if (stable)
        {
            Assert.Throws<InvalidOperationException>(() => collection.StableSort(comparer));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => collection.Sort(comparer));
        }

        Assert.Equal([2, 1, 0], collection.ToList());
        Assert.Equal([2, 1, 0], observable.ToList());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservableRemoveAt_TargetsTheDisplayedItemWhenAnInsertionIsPending(bool useNonGenericList)
    {
        WithQueuedSynchronizationContext(context =>
        {
            var (collection, observable) = CreateCollectionWithPendingModification(context, [10, 20], c => c.Insert(0, 99));

            if (useNonGenericList)
            {
                ((IList)observable).RemoveAt(0);
            }
            else
            {
                ((IList<int>)observable).RemoveAt(0);
            }

            Assert.Equal([99, 20], collection.ToList());
            context.Run();
            Assert.Equal([99, 20], observable.ToList());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservableIndexer_TargetsTheDisplayedItemWhenARemovalIsPending(bool useNonGenericList)
    {
        WithQueuedSynchronizationContext(context =>
        {
            var (collection, observable) = CreateCollectionWithPendingModification(context, [10, 20, 30], c => c.RemoveAt(0));

            if (useNonGenericList)
            {
                ((IList)observable)[2] = 31;
            }
            else
            {
                ((IList<int>)observable)[2] = 31;
            }

            Assert.Equal([20, 31], collection.ToList());
            context.Run();
            Assert.Equal([20, 31], observable.ToList());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObservableInsert_UsesThePositionOfTheViewWhenModificationsArePending(bool useNonGenericList)
    {
        WithQueuedSynchronizationContext(context =>
        {
            var (collection, observable) = CreateCollectionWithPendingModification(context, [10, 20], c =>
            {
                c.Insert(0, 99);
                c.Add(30);
            });

            // Position 2 is the end of the view [10, 20]: the item goes after 20, before the pending 30
            if (useNonGenericList)
            {
                ((IList)observable).Insert(2, 25);
            }
            else
            {
                ((IList<int>)observable).Insert(2, 25);
            }

            Assert.Equal([99, 10, 20, 30, 25], collection.ToList());
            context.Run();
            Assert.Equal(collection.ToList(), observable.ToList());
        });
    }

    [Fact]
    public void ObservableIndexedEdit_ThrowsWhenTheDisplayedItemWasRemoved()
    {
        WithQueuedSynchronizationContext(context =>
        {
            var (collection, observable) = CreateCollectionWithPendingModification(context, [10, 20], c => c.Remove(10));
            var list = (IList<int>)observable;

            Assert.Throws<InvalidOperationException>(() => list.RemoveAt(0));
            Assert.Throws<InvalidOperationException>(() => list[0] = 11);
            Assert.Throws<InvalidOperationException>(() => ((IList)observable).RemoveAt(0));

            // The position of the other item is still known
            list.RemoveAt(1);

            Assert.Empty(collection);
            context.Run();
            Assert.Empty(observable);
        });
    }

    [Fact]
    public void ObservableIndexedEdit_ThrowsWhenTheCollectionWasReordered()
    {
        WithQueuedSynchronizationContext(context =>
        {
            var (collection, observable) = CreateCollectionWithPendingModification(context, [20, 10], c => c.Sort());
            var list = (IList<int>)observable;

            Assert.Throws<InvalidOperationException>(() => list.RemoveAt(0));
            Assert.Throws<InvalidOperationException>(() => list.Insert(0, 5));

            Assert.Equal([10, 20], collection.ToList());
            context.Run();
            Assert.Equal([10, 20], observable.ToList());
        });
    }

    [Fact]
    public void ObservableIndexedEdit_ThrowsWhenTheIndexIsOutOfTheView()
    {
        WithQueuedSynchronizationContext(context =>
        {
            var (collection, observable) = CreateCollectionWithPendingModification(context, [10], c => c.Add(20));
            var list = (IList<int>)observable;

            // Index 1 is valid in the source collection but not in the view
            Assert.Throws<ArgumentOutOfRangeException>(() => list.RemoveAt(1));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Insert(2, 5));
            Assert.Equal([10, 20], collection.ToList());
        });
    }

    [Fact]
    public void ObservableIndexedEdit_FromAHandlerUsesThePositionOfTheView()
    {
        var collection = CreateCollection<int>();
        collection.Add(10);
        var observable = collection.AsObservable;

        var removed = false;
        void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // When the first item of the range is notified, the view is [10, 1] while the collection is [10, 1, 2]
            if (!removed)
            {
                removed = true;
                ((IList<int>)observable).RemoveAt(0);
            }
        }

        observable.CollectionChanged += OnCollectionChanged;
        try
        {
            collection.AddRange(1, 2);
        }
        finally
        {
            observable.CollectionChanged -= OnCollectionChanged;
        }

        Assert.Equal([1, 2], collection.ToList());
        Assert.Equal([1, 2], observable.ToList());
    }

    [Fact]
    public async Task CollectionCreatedWithoutSynchronizationContext_HandlersCanAccessTheView()
    {
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            await AssertHandlerCanAccessTheView(new ConcurrentObservableCollection<int>());
            await AssertHandlerCanAccessTheView(new ConcurrentObservableCollection<int>(new SynchronizationContext()));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }

        static async Task AssertHandlerCanAccessTheView(ConcurrentObservableCollection<int> collection)
        {
            var observable = collection.AsObservable;
            var result = new TaskCompletionSource<(int Count, int[] Items, int First)>(TaskCreationOptions.RunContinuationsAsynchronously);
            observable.CollectionChanged += (sender, e) =>
            {
                try
                {
                    result.TrySetResult((observable.Count, observable.ToArray(), observable[0]));
                }
                catch (Exception ex)
                {
                    result.TrySetException(ex);
                }
            };

            collection.Add(42);

            var (count, items, first) = await result.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(1, count);
            Assert.Equal([42], items);
            Assert.Equal(42, first);

            // The access is granted to the drain only, not to every thread pool thread
            await Task.Run(() => Assert.Throws<InvalidOperationException>(() => observable.Count));
        }
    }

    private static (ConcurrentObservableCollection<int> Collection, IReadOnlyObservableCollection<int> Observable) CreateCollectionWithPendingModification(
        QueuedSynchronizationContext context,
        int[] initialItems,
        Action<ConcurrentObservableCollection<int>> modification)
    {
        var collection = new ConcurrentObservableCollection<int>(context);
        collection.AddRange(initialItems);
        var observable = collection.AsObservable;

        Assert.Null(RunOnAnotherThread(() => modification(collection)));
        Assert.Equal(initialItems, observable.ToList());
        return (collection, observable);
    }

    private static void WithQueuedSynchronizationContext(Action<QueuedSynchronizationContext> action)
    {
        var context = new QueuedSynchronizationContext();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            action(context);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    /// <summary>Runs <paramref name="action"/> on a thread with no synchronization context and returns the exception it threw, if any.</summary>
    private static Exception? RunOnAnotherThread(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.Start();
        thread.Join();
        return exception;
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

        /// <summary>When set to <see langword="true"/>, <see cref="Post"/> throws instead of queuing the callback.</summary>
        public bool RejectPost { get; set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (RejectPost)
                throw new InvalidOperationException("The synchronization context cannot accept the callback");

            _callbacks.Enqueue((d, state));
        }

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

    private sealed class MutableKey(int key) : IEquatable<MutableKey>
    {
        public int Key { get; set; } = key;

        public bool Equals([NotNullWhen(true)] MutableKey? other) => other is not null && other.Key == Key;

        public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as MutableKey);

        public override int GetHashCode() => 0;
    }

    private sealed class ReentrantEquals : IEquatable<ReentrantEquals>
    {
        public Action? OnEquals { get; init; }

        public bool Equals([NotNullWhen(true)] ReentrantEquals? other)
        {
            OnEquals?.Invoke();
            return false;
        }

        public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as ReentrantEquals);

        public override int GetHashCode() => 0;
    }

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
