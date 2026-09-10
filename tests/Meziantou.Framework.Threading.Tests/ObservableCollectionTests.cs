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

    [Theory]
    [InlineData(ObservableCollectionEdit.IndexerSet)]
    [InlineData(ObservableCollectionEdit.Insert)]
    [InlineData(ObservableCollectionEdit.RemoveAt)]
    [InlineData(ObservableCollectionEdit.NonGenericIndexerSet)]
    [InlineData(ObservableCollectionEdit.NonGenericInsert)]
    [InlineData(ObservableCollectionEdit.NonGenericRemoveAt)]
    [InlineData(ObservableCollectionEdit.NonGenericAdd)]
    public void IndexBasedEditsAreRejectedWhileTheObservableCollectionIsOutOfDate(ObservableCollectionEdit edit)
    {
        var context = new QueuedSynchronizationContext();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var collection = new ConcurrentObservableCollection<string>(context);
            var observable = collection.AsObservable;
            collection.AddRange("A", "B");

            Assert.Null(RunOnAnotherThread(() => collection.Insert(0, "X")));

            // The observable collection still exposes the previous state, so its indices designate other items in the source collection
            Assert.Equal(["A", "B"], observable.ToList());
            Assert.Throws<InvalidOperationException>(() => Edit(observable, edit));

            Assert.Equal(["X", "A", "B"], collection.ToList());

            Assert.Equal(1, context.Run());
            Assert.Equal(["X", "A", "B"], observable.ToList());

            // Once the pending change is dispatched, the same edit designates the item the observable collection exposes
            Edit(observable, edit);
            Assert.Equal(GetExpectedItemsAfterEdit(edit), collection.ToList());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    [Theory]
    [InlineData(ObservableCollectionEdit.Add)]
    [InlineData(ObservableCollectionEdit.Remove)]
    [InlineData(ObservableCollectionEdit.Clear)]
    public void ValueBasedEditsAreAllowedWhileTheObservableCollectionIsOutOfDate(ObservableCollectionEdit edit)
    {
        var context = new QueuedSynchronizationContext();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var collection = new ConcurrentObservableCollection<string>(context);
            var observable = collection.AsObservable;
            collection.AddRange("A", "B");

            Assert.Null(RunOnAnotherThread(() => collection.Insert(0, "X")));

            // These edits designate their target by value, so they don't depend on the observable collection being up to date
            Edit(observable, edit);
            Assert.Equal(GetExpectedItemsAfterEdit(edit), collection.ToList());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    [Fact]
    public void ObservableCollectionQueriesReadTheDispatchedItems()
    {
        var context = new QueuedSynchronizationContext();
        var previousSynchronizationContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var collection = new ConcurrentObservableCollection<string>(context);
            var observable = collection.AsObservable;
            var list = (IList)observable;
            collection.AddRange("A", "B");

            Assert.Null(RunOnAnotherThread(() => collection.Add("X")));

            // Contains must agree with the items the observable collection exposes, not with the source collection
            Assert.Equal(["A", "B"], observable.ToList());
            Assert.False(list.Contains("X"));
            Assert.Equal(-1, list.IndexOf("X"));

            Assert.Equal(1, context.Run());

            Assert.Equal(["A", "B", "X"], observable.ToList());
            Assert.True(list.Contains("X"));
            Assert.Equal(2, list.IndexOf("X"));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousSynchronizationContext);
        }
    }

    [Fact]
    public void ObservableCollectionAddAndInsertWrongItemType()
    {
        var collection = (IList)CreateCollection<string>().AsObservable;
        collection.Add(null);
        collection.Add("");

        Assert.Throws<ArgumentException>(() => collection.Add(10));
        Assert.Throws<ArgumentException>(() => collection.Insert(0, 10));
        Assert.Equal([null, ""], collection.Cast<string?>().ToList());
    }

    public enum ObservableCollectionEdit
    {
        IndexerSet,
        Insert,
        RemoveAt,
        NonGenericIndexerSet,
        NonGenericInsert,
        NonGenericRemoveAt,
        NonGenericAdd,
        Add,
        Remove,
        Clear,
    }

    private static void Edit(IReadOnlyObservableCollection<string> observable, ObservableCollectionEdit edit)
    {
        switch (edit)
        {
            case ObservableCollectionEdit.IndexerSet:
                ((IList<string>)observable)[0] = "Z";
                break;

            case ObservableCollectionEdit.Insert:
                ((IList<string>)observable).Insert(0, "Z");
                break;

            case ObservableCollectionEdit.RemoveAt:
                ((IList<string>)observable).RemoveAt(0);
                break;

            case ObservableCollectionEdit.NonGenericIndexerSet:
                ((IList)observable)[0] = "Z";
                break;

            case ObservableCollectionEdit.NonGenericInsert:
                ((IList)observable).Insert(0, "Z");
                break;

            case ObservableCollectionEdit.NonGenericRemoveAt:
                ((IList)observable).RemoveAt(0);
                break;

            case ObservableCollectionEdit.NonGenericAdd:
                ((IList)observable).Add("Z");
                break;

            case ObservableCollectionEdit.Add:
                ((ICollection<string>)observable).Add("Z");
                break;

            case ObservableCollectionEdit.Remove:
                ((ICollection<string>)observable).Remove("A");
                break;

            case ObservableCollectionEdit.Clear:
                ((ICollection<string>)observable).Clear();
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(edit));
        }
    }

    /// <summary>Gets the expected content of the source collection after <see cref="Edit"/> is applied to <c>["X", "A", "B"]</c>.</summary>
    private static string[] GetExpectedItemsAfterEdit(ObservableCollectionEdit edit)
    {
        return edit switch
        {
            ObservableCollectionEdit.IndexerSet or ObservableCollectionEdit.NonGenericIndexerSet => ["Z", "A", "B"],
            ObservableCollectionEdit.Insert or ObservableCollectionEdit.NonGenericInsert => ["Z", "X", "A", "B"],
            ObservableCollectionEdit.RemoveAt or ObservableCollectionEdit.NonGenericRemoveAt => ["A", "B"],
            ObservableCollectionEdit.NonGenericAdd or ObservableCollectionEdit.Add => ["X", "A", "B", "Z"],
            ObservableCollectionEdit.Remove => ["X", "B"],
            ObservableCollectionEdit.Clear => [],
            _ => throw new ArgumentOutOfRangeException(nameof(edit)),
        };
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
