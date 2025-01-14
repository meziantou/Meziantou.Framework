using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Meziantou.Framework.WPF.Collections;
using Xunit;

namespace Meziantou.Framework.Windows.Tests;

public sealed partial class ObservableCollectionTests
{
    public static IEnumerable<object[]> GetCollections
    {
        get
        {
            yield return new object[] { new ConcurrentObservableCollection<int>() };
            yield return new object[] { new System.Collections.ObjectModel.ObservableCollection<int>() };
        }
    }

    private static object GetObservableCollection<T>(IList<T> collection)
    {
        if (collection is ConcurrentObservableCollection<T> result)
            return result.AsObservable;

        return collection;
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Add(IList<int> collection)
    {
        // Arrange
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
        collection.Add(1);
        Assert.Equal([1], collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedAddItem(1);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Remove(IList<int> collection)
    {
        // Arrange
        collection.Add(1);
        collection.Add(2);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
        collection.Remove(1);
        Assert.Equal([2], collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedRemoveItem(1);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void RemoveAt(IList<int> collection)
    {
        // Arrange
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
        collection.RemoveAt(0);
        Assert.Equal([2, 3], collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedRemoveItem(1);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Insert(IList<int> collection)
    {
        // Arrange
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
        collection.Insert(index: 0, item: 1);
        Assert.Equal([1], collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedAddItem(1);
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Clear(IList<int> collection)
    {
        // Arrange
        collection.Add(1);
        collection.Add(2);
        collection.Add(3);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
        collection.Clear();
        Assert.Empty(collection.ToList());
        eventAssert.AssertPropertyChanged("Count", "Item[]");
        eventAssert.AssertCollectionChangedReset();
    }

    [Theory]
    [MemberData(nameof(GetCollections))]
    public void Indexer_Set(IList<int> collection)
    {
        // Arrange
        collection.Add(1);
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
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
        // Arrange
        var collection = new ConcurrentObservableCollection<int>()
        {
            SupportRangeNotifications = supportRangeNotifications,
        };

        collection.AddRange(0, 1, 2);

        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
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
            Assert.Equivalent(new[]
            {
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 3, NewItems = new[] { 3 }  },
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 4, NewItems = new[] { 4 }  },
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 5, NewItems = new[] { 5 }  },
            }, eventAssert.CollectionChangedArgs);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertRange(bool supportRangeNotifications)
    {
        // Arrange
        var collection = new ConcurrentObservableCollection<int>()
        {
            SupportRangeNotifications = supportRangeNotifications,
        };

        collection.AddRange(0, 1, 5);

        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
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
            Assert.Equivalent(new[]
            {
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 2, NewItems = new[] { 2 }  },
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 3, NewItems = new[] { 3 }  },
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 4, NewItems = new[] { 4 }  },
            }, eventAssert.CollectionChangedArgs);
        }
    }

    [Fact]
    public void Sort()
    {
        // Arrange
        var collection = new ConcurrentObservableCollection<int> { 1, 0, 2 };
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
        collection.Sort();
        Assert.Equal([0, 1, 2], collection.ToList());
        Assert.Equal([0, 1, 2], collection.AsObservable.ToList());
        eventAssert.AssertPropertyChanged("Item[]");
        eventAssert.AssertCollectionChangedReset();
    }

    [Fact]
    public void StableSort()
    {
        // Arrange
        var collection = new ConcurrentObservableCollection<int> { 1, 0, 2 };
        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
        collection.StableSort();
        Assert.Equal([0, 1, 2], collection.ToList());
        Assert.Equal([0, 1, 2], collection.AsObservable.ToList());
        eventAssert.AssertPropertyChanged("Item[]");
        eventAssert.AssertCollectionChangedReset();
    }

    [Fact]
    public void StableSort_PreserveOrder()
    {
        // Arrange
        var collection = new ConcurrentObservableCollection<Sample>();
        for (var i = 0; i < 1000; i++)
        {
            collection.Add(new Sample(i * 2, "Value" + (i * 2).ToString("D5", CultureInfo.InvariantCulture)));
            collection.Add(new Sample((i * 2) + 1, "Value" + (i * 2).ToString("D5", CultureInfo.InvariantCulture)));
        }

        using var eventAssert = new EventAssert(GetObservableCollection(collection));

        // Act
        collection.StableSort(new SampleComparer()); // Compare by value

        // Assert
        Assert.Equal(collection, collection.OrderBy(item => item.Index));
        eventAssert.AssertPropertyChanged("Item[]");
        eventAssert.AssertCollectionChangedReset();
    }

    [Fact]
    public void AddWrongItemType()
    {
        var collection = (IList)new ConcurrentObservableCollection<string>();
        collection.Add(null);
        collection.Add("");

        Assert.Throws<ArgumentException>(() => collection.Add(10));
    }

    private sealed record Sample(int Index, string Value);

    private sealed class SampleComparer : IComparer<Sample>
    {
        public int Compare(Sample x, Sample y)
        {
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

        private void NotifyCollectionChanged_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            CollectionChangedArgs.Add(e);
        }

        private void NotifyPropertyChanged_PropertyChanged(object sender, PropertyChangedEventArgs e)
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
            Assert.Equal(obj, args.NewItems[0]);
            Assert.Equal(0, args.NewStartingIndex);
            Assert.Equal(-1, args.OldStartingIndex);
            Assert.Null(args.OldItems);
        }

        public void AssertCollectionChangedAddItems(object[] obj, int startIndex)
        {
            Assert.Single(CollectionChangedArgs);
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Add);
            Assert.Equal(obj, args.NewItems.OfType<object>());
            Assert.Equal(startIndex, args.NewStartingIndex);
            Assert.Equal(-1, args.OldStartingIndex);
            Assert.Null(args.OldItems);
        }

        public void AssertCollectionChangedRemoveItem(object obj)
        {
            Assert.Single(CollectionChangedArgs);
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Remove);
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
            Assert.Equal(newValue, args.NewItems[0]);
            Assert.Equal(oldValue, args.OldItems[0]);
            Assert.Equal(0, args.NewStartingIndex);
            Assert.Equal(0, args.OldStartingIndex);
        }
    }
}
