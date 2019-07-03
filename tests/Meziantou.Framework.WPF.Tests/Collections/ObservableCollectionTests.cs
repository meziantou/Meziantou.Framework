using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Meziantou.Framework.WPF.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Windows.Tests
{
    [TestClass]
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

        [DataTestMethod]
        [DynamicData(nameof(GetCollections))]
        public void Add(IList<int> collection)
        {
            // Arrange
            using var eventAssert = new EventAssert(GetObservableCollection(collection));

            // Act
            collection.Add(1);

            // Assert
            CollectionAssert.AreEqual(new[] { 1 }, collection.ToList());
            eventAssert.AssertPropertyChanged("Count", "Item[]");
            eventAssert.AssertCollectionChangedAddItem(1);
        }

        [DataTestMethod]
        [DynamicData(nameof(GetCollections))]
        public void Remove(IList<int> collection)
        {
            // Arrange       
            collection.Add(1);
            collection.Add(2);
            using var eventAssert = new EventAssert(GetObservableCollection(collection));

            // Act
            collection.Remove(1);

            // Assert
            CollectionAssert.AreEqual(new[] { 2 }, collection.ToList());
            eventAssert.AssertPropertyChanged("Count", "Item[]");
            eventAssert.AssertCollectionChangedRemoveItem(1);
        }

        [DataTestMethod]
        [DynamicData(nameof(GetCollections))]
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
            CollectionAssert.AreEqual(new[] { 2, 3 }, collection.ToList());
            eventAssert.AssertPropertyChanged("Count", "Item[]");
            eventAssert.AssertCollectionChangedRemoveItem(1);
        }

        [DataTestMethod]
        [DynamicData(nameof(GetCollections))]
        public void Insert(IList<int> collection)
        {
            // Arrange       
            using var eventAssert = new EventAssert(GetObservableCollection(collection));

            // Act
            collection.Insert(index: 0, item: 1);

            // Assert
            CollectionAssert.AreEqual(new[] { 1 }, collection.ToList());
            eventAssert.AssertPropertyChanged("Count", "Item[]");
            eventAssert.AssertCollectionChangedAddItem(1);
        }

        [DataTestMethod]
        [DynamicData(nameof(GetCollections))]
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
            CollectionAssert.AreEqual(Array.Empty<int>(), collection.ToList());
            eventAssert.AssertPropertyChanged("Count", "Item[]");
            eventAssert.AssertCollectionChangedReset();
        }

        [DataTestMethod]
        [DynamicData(nameof(GetCollections))]
        public void Indexer_Set(IList<int> collection)
        {
            // Arrange
            collection.Add(1);
            using var eventAssert = new EventAssert(GetObservableCollection(collection));

            // Act
            collection[0] = 2;

            // Assert
            CollectionAssert.AreEqual(new[] { 2 }, collection.ToList());
            eventAssert.AssertPropertyChanged("Item[]");
            eventAssert.AssertCollectionChangedReplace(oldValue: 1, newValue: 2);
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
                CollectionAssert.AreEqual(propertyNames, _propertyChangedArgs.Select(e => e.PropertyName).ToList());
            }

            public void AssertCollectionChangedAddItem(object obj)
            {
                Assert.AreEqual(1, _collectionChangedArgs.Count);
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Add);

                if (!Equals(args.NewItems[0], obj))
                {
                    Assert.Fail("The item was not added");
                }

                Assert.AreEqual(0, args.NewStartingIndex);
                Assert.AreEqual(-1, args.OldStartingIndex);
                Assert.AreEqual(null, args.OldItems);
            }

            public void AssertCollectionChangedRemoveItem(object obj)
            {
                Assert.AreEqual(1, _collectionChangedArgs.Count);
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Remove);

                if (!Equals(args.OldItems[0], obj))
                {
                    Assert.Fail("The item was not removed");
                }

                Assert.AreEqual(-1, args.NewStartingIndex);
                Assert.AreEqual(0, args.OldStartingIndex);
                Assert.AreEqual(null, args.NewItems);
            }

            public void AssertCollectionChangedReset()
            {
                Assert.AreEqual(1, _collectionChangedArgs.Count);
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Reset);

                Assert.AreEqual(-1, args.NewStartingIndex);
                Assert.AreEqual(-1, args.OldStartingIndex);
                Assert.AreEqual(null, args.NewItems);
                Assert.AreEqual(null, args.OldItems);
            }

            public void AssertCollectionChangedReplace(object oldValue, object newValue)
            {
                Assert.AreEqual(1, _collectionChangedArgs.Count);
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Replace);

                if (!Equals(args.NewItems[0], newValue))
                {
                    Assert.Fail("The item was not added");
                }

                if (!Equals(args.OldItems[0], oldValue))
                {
                    Assert.Fail("The item was not added");
                }

                Assert.AreEqual(0, args.NewStartingIndex);
                Assert.AreEqual(0, args.OldStartingIndex);
            }

        }
    }
}
