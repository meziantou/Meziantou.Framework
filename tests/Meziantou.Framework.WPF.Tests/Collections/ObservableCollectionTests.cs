using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows.Threading;
using Meziantou.Framework.WPF.Collections;
using Xunit;

namespace Meziantou.Framework.Windows.Tests
{
    public sealed class ObservableCollectionTests
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
            Assert.Equal(new[] { 1 }, collection.ToList());
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
            Assert.Equal(new[] { 2 }, collection.ToList());
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
            Assert.Equal(new[] { 2, 3 }, collection.ToList());
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
            Assert.Equal(new[] { 1 }, collection.ToList());
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
            Assert.Equal(Array.Empty<int>(), collection.ToList());
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
            Assert.Equal(new[] { 2 }, collection.ToList());
            eventAssert.AssertPropertyChanged("Item[]");
            eventAssert.AssertCollectionChangedReplace(oldValue: 1, newValue: 2);
        }

        [Fact]
        public void AddWrongItemType()
        {
            var collection = (IList)new ConcurrentObservableCollection<string>();
            collection.Add(null);
            collection.Add("");

            Assert.Throws<ArgumentException>(() => collection.Add(10));
        }

        [Fact]
        public void CollectionChanged_01()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var events = new List<NotifyCollectionChangedEventArgs>();
            collection.AsObservable.CollectionChanged += (sender, args) => events.Add(args);

            // Act
            collection.Add(1);

            // Assert
            Assert.Single(events);
            Assert.Equal(NotifyCollectionChangedAction.Add, events[0].Action);
        }

        [Fact]
        public void CollectionChanged_02()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var events = new List<NotifyCollectionChangedEventArgs>();
            collection.AsObservable.CollectionChanged += (sender, args) => events.Add(args);

            // Act
            collection.Add(1);
            collection.Add(2);
            collection.Add(3);
            collection.Remove(2);

            // Assert
            Assert.Equal(4, events.Count);
            foreach (var e in events.Take(3))
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
            }

            Assert.Equal(NotifyCollectionChangedAction.Remove, events[3].Action);
        }

        [Fact]
        public void CollectionChanged_03()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var events = new List<NotifyCollectionChangedEventArgs>();
            collection.AsObservable.CollectionChanged += (sender, args) => events.Add(args);

            // Act
            collection.Add(1);
            collection.Add(2);
            collection.Add(3);
            collection.Remove(4); // Collection does not contain item

            // Assert
            Assert.Equal(3, events.Count);
            foreach (var e in events)
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
            }
        }

        [Fact]
        public void CollectionChanged_04()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var events = new List<NotifyCollectionChangedEventArgs>();
            collection.AsObservable.CollectionChanged += (sender, args) => events.Add(args);

            // Act
            collection.AddRange(new[] { 1, 2, 3 });

            // Assert
            Assert.Equal(3, events.Count);
            foreach (var e in events)
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
            }
        }

        [Fact]
        public void BatchMode_01()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var events = new List<NotifyCollectionChangedEventArgs>();
            collection.AsObservable.CollectionChanged += (sender, args) => events.Add(args);

            // Act
            using (collection.BeginBatch())
            {
                collection.AddRange(new[] { 1, 2, 3 });

                // Assert
                Assert.Empty(events);
            }

            Assert.Equal(3, events.Count);
            foreach (var e in events)
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
            }
        }

        [Fact]
        public void BatchMode_02()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var events = new List<NotifyCollectionChangedEventArgs>();
            collection.AsObservable.CollectionChanged += (sender, args) => events.Add(args);

            // Act
            using (collection.BeginBatch(BatchMode.Reset))
            {
                collection.AddRange(new[] { 1, 2, 3 });

                // Assert
                Assert.Empty(events);
            }

            Assert.Single(events);
            Assert.Equal(NotifyCollectionChangedAction.Reset, events[0].Action);
        }

        [Fact]
        public void BatchMode_03()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var events = new List<NotifyCollectionChangedEventArgs>();
            collection.AsObservable.CollectionChanged += (sender, args) => events.Add(args);

            // Act
            using (collection.BeginBatch(BatchMode.Optimized))
            {
                collection.AddRange(new[] { 1, 2, 3 });
                collection.Remove(2);

                // Assert
                Assert.Empty(events);
            }

            Assert.Equal(2, events.Count);
            foreach (var e in events)
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
            }
        }

        [Fact]
        public void BatchMode_04()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var events = new List<NotifyCollectionChangedEventArgs>();
            collection.AsObservable.CollectionChanged += (sender, args) => events.Add(args);

            // Act
            collection.AddRange(new[] { 2 });

            using (collection.BeginBatch(BatchMode.Optimized))
            {
                collection.AddRange(new[] { 1, 2, 3 });
                collection.Remove(2);

                // Assert
                Assert.Single(events);
            }

            Assert.Equal(5, events.Count); // Cannot be optimized
            foreach (var e in events.Take(4))
            {
                Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
            }

            Assert.Equal(NotifyCollectionChangedAction.Remove, events[4].Action);
        }

        private sealed class EventAssert : IDisposable
        {
            private readonly object _obj;

            private readonly List<NotifyCollectionChangedEventArgs> _collectionChangedArgs = new List<NotifyCollectionChangedEventArgs>();
            private readonly List<PropertyChangedEventArgs> _propertyChangedArgs = new List<PropertyChangedEventArgs>();

            public EventAssert(object obj)
            {
                _obj = obj;
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
                if (_obj is INotifyPropertyChanged notifyPropertyChanged)
                {
                    notifyPropertyChanged.PropertyChanged -= NotifyPropertyChanged_PropertyChanged;
                }

                if (_obj is INotifyCollectionChanged notifyCollectionChanged)
                {
                    notifyCollectionChanged.CollectionChanged -= NotifyCollectionChanged_CollectionChanged;
                }
            }

            private void NotifyCollectionChanged_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
            {
                _collectionChangedArgs.Add(e);
            }

            private void NotifyPropertyChanged_PropertyChanged(object sender, PropertyChangedEventArgs e)
            {
                _propertyChangedArgs.Add(e);
            }

            public void AssertPropertyChanged(params string[] propertyNames)
            {
                Assert.Equal(propertyNames, _propertyChangedArgs.Select(e => e.PropertyName).ToList());
            }

            public void AssertCollectionChangedAddItem(object obj)
            {
                Assert.Single(_collectionChangedArgs);
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Add);

                if (!Equals(args.NewItems[0], obj))
                {
                    Assert.True(false, "The item was not added");
                }

                Assert.Equal(0, args.NewStartingIndex);
                Assert.Equal(-1, args.OldStartingIndex);
                Assert.Null(args.OldItems);
            }

            public void AssertCollectionChangedRemoveItem(object obj)
            {
                Assert.Single(_collectionChangedArgs);
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Remove);

                if (!Equals(args.OldItems[0], obj))
                {
                    Assert.True(false, "The item was not removed");
                }

                Assert.Equal(-1, args.NewStartingIndex);
                Assert.Equal(0, args.OldStartingIndex);
                Assert.Null(args.NewItems);
            }

            public void AssertCollectionChangedReset()
            {
                Assert.Single(_collectionChangedArgs);
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Reset);

                Assert.Equal(-1, args.NewStartingIndex);
                Assert.Equal(-1, args.OldStartingIndex);
                Assert.Null(args.NewItems);
                Assert.Null(args.OldItems);
            }

            public void AssertCollectionChangedReplace(object oldValue, object newValue)
            {
                Assert.Single(_collectionChangedArgs);
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Replace);

                if (!Equals(args.NewItems[0], newValue))
                {
                    Assert.True(false, "The item was not added");
                }

                if (!Equals(args.OldItems[0], oldValue))
                {
                    Assert.True(false, "The item was not added");
                }

                Assert.Equal(0, args.NewStartingIndex);
                Assert.Equal(0, args.OldStartingIndex);
            }
        }
    }
}
