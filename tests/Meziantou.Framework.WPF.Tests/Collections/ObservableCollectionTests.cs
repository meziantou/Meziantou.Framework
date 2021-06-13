﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using FluentAssertions;
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
            collection.ToList().Should().Equal(new[] { 1 });
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
            collection.ToList().Should().Equal(new[] { 2 });
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
            collection.ToList().Should().Equal(new[] { 2, 3 });
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
            collection.ToList().Should().Equal(new[] { 1 });
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
            collection.ToList().Should().Equal(new[] { 2 });
            eventAssert.AssertPropertyChanged("Item[]");
            eventAssert.AssertCollectionChangedReplace(oldValue: 1, newValue: 2);
        }

        [Fact]
        public void AddWrongItemType()
        {
            var collection = (IList)new ConcurrentObservableCollection<string>();
            collection.Add(null);
            collection.Add("");

            new Action(() => collection.Add(10)).Should().ThrowExactly<ArgumentException>();
        }

        private sealed class EventAssert : IDisposable
        {
            private readonly object _obj;

            private readonly List<NotifyCollectionChangedEventArgs> _collectionChangedArgs = new();
            private readonly List<PropertyChangedEventArgs> _propertyChangedArgs = new();

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
                _propertyChangedArgs.Select(e => e.PropertyName).ToList().Should().Equal(propertyNames);
            }

            public void AssertCollectionChangedAddItem(object obj)
            {
                _collectionChangedArgs.Should().ContainSingle();
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Add);

                args.NewItems[0].Should().Be(obj);

                args.NewStartingIndex.Should().Be(0);
                args.OldStartingIndex.Should().Be(-1);
                args.OldItems.Should().BeNull();
            }

            public void AssertCollectionChangedRemoveItem(object obj)
            {
                _collectionChangedArgs.Should().ContainSingle();
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Remove);

                args.OldItems[0].Should().Be(obj);

                args.NewStartingIndex.Should().Be(-1);
                args.OldStartingIndex.Should().Be(0);
                args.NewItems.Should().BeNull();
            }

            public void AssertCollectionChangedReset()
            {
                _collectionChangedArgs.Should().ContainSingle();
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Reset);

                args.NewStartingIndex.Should().Be(-1);
                args.OldStartingIndex.Should().Be(-1);
                args.NewItems.Should().BeNull();
                args.OldItems.Should().BeNull();
            }

            public void AssertCollectionChangedReplace(object oldValue, object newValue)
            {
                _collectionChangedArgs.Should().ContainSingle();
                var args = _collectionChangedArgs.Single(e => e.Action == NotifyCollectionChangedAction.Replace);

                args.NewItems[0].Should().Be(newValue);
                args.OldItems[0].Should().Be(oldValue);

                args.NewStartingIndex.Should().Be(0);
                args.OldStartingIndex.Should().Be(0);
            }
        }
    }
}
