using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using FluentAssertions;
using FluentAssertions.Execution;
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

        // Assert
        collection.ToList().Should().Equal([1]);
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

        // Assert
        collection.ToList().Should().Equal([2]);
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

        // Assert
        collection.ToList().Should().Equal([2, 3]);
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

        // Assert
        collection.ToList().Should().Equal([1]);
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

        // Assert
        collection.ToList().Should().BeEmpty();
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

        // Assert
        collection.ToList().Should().Equal([2]);
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

        // Assert
        collection.ToList().Should().Equal([0, 1, 2, 3, 4, 5]);
        collection.AsObservable.ToList().Should().Equal([0, 1, 2, 3, 4, 5]);

        if (supportRangeNotifications)
        {
            eventAssert.AssertCollectionChangedAddItems([3, 4, 5], startIndex: 3);
        }
        else
        {
            eventAssert.CollectionChangedArgs.Select(e => e.Action).Should().AllBeEquivalentTo(NotifyCollectionChangedAction.Add);
            eventAssert.CollectionChangedArgs.Should().BeEquivalentTo(new[]
            {
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 3, NewItems = new[] { 3 }  },
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 4, NewItems = new[] { 4 }  },
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 5, NewItems = new[] { 5 }  },
            });
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

        // Assert
        collection.ToList().Should().Equal([0, 1, 2, 3, 4, 5]);
        collection.AsObservable.ToList().Should().Equal([0, 1, 2, 3, 4, 5]);

        if (supportRangeNotifications)
        {
            eventAssert.AssertCollectionChangedAddItems([2, 3, 4], startIndex: 2);
        }
        else
        {
            eventAssert.CollectionChangedArgs.Select(e => e.Action).Should().AllBeEquivalentTo(NotifyCollectionChangedAction.Add);
            eventAssert.CollectionChangedArgs.Should().BeEquivalentTo(new[]
            {
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 2, NewItems = new[] { 2 }  },
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 3, NewItems = new[] { 3 }  },
                new { Action = NotifyCollectionChangedAction.Add, NewStartingIndex = 4, NewItems = new[] { 4 }  },
            });
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

        // Assert
        collection.ToList().Should().Equal([0, 1, 2]);
        collection.AsObservable.ToList().Should().Equal([0, 1, 2]);
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

        // Assert
        collection.ToList().Should().Equal([0, 1, 2]);
        collection.AsObservable.ToList().Should().Equal([0, 1, 2]);
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
        using (new AssertionScope())
        {
            collection.Should().BeInAscendingOrder(item => item.Index);
            eventAssert.AssertPropertyChanged("Item[]");
            eventAssert.AssertCollectionChangedReset();
        }
    }

    [Fact]
    public void AddWrongItemType()
    {
        var collection = (IList)new ConcurrentObservableCollection<string>();
        collection.Add(null);
        collection.Add("");

        new Action(() => collection.Add(10)).Should().ThrowExactly<ArgumentException>();
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

        public List<NotifyCollectionChangedEventArgs> CollectionChangedArgs { get; } = new();
        public List<PropertyChangedEventArgs> PropertyChangedArgs { get; } = new();

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
            PropertyChangedArgs.Select(e => e.PropertyName).ToList().Should().Equal(propertyNames);
        }

        public void AssertCollectionChangedAddItem(object obj)
        {
            CollectionChangedArgs.Should().ContainSingle();
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Add);

            args.NewItems[0].Should().Be(obj);

            args.NewStartingIndex.Should().Be(0);
            args.OldStartingIndex.Should().Be(-1);
            args.OldItems.Should().BeNull();
        }

        public void AssertCollectionChangedAddItems(object[] obj, int startIndex)
        {
            CollectionChangedArgs.Should().ContainSingle();
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Add);

            args.NewItems.OfType<object>().Should().Equal(obj);

            args.NewStartingIndex.Should().Be(startIndex);
            args.OldStartingIndex.Should().Be(-1);
            args.OldItems.Should().BeNull();
        }

        public void AssertCollectionChangedRemoveItem(object obj)
        {
            CollectionChangedArgs.Should().ContainSingle();
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Remove);

            args.OldItems[0].Should().Be(obj);

            args.NewStartingIndex.Should().Be(-1);
            args.OldStartingIndex.Should().Be(0);
            args.NewItems.Should().BeNull();
        }

        public void AssertCollectionChangedReset()
        {
            CollectionChangedArgs.Should().ContainSingle();
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Reset);

            args.NewStartingIndex.Should().Be(-1);
            args.OldStartingIndex.Should().Be(-1);
            args.NewItems.Should().BeNull();
            args.OldItems.Should().BeNull();
        }

        public void AssertCollectionChangedReplace(object oldValue, object newValue)
        {
            CollectionChangedArgs.Should().ContainSingle();
            var args = CollectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Replace);

            args.NewItems[0].Should().Be(newValue);
            args.OldItems[0].Should().Be(oldValue);

            args.NewStartingIndex.Should().Be(0);
            args.OldStartingIndex.Should().Be(0);
        }
    }
}
