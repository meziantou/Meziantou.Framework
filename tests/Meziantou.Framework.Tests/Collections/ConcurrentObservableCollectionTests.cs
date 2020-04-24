using System.Windows.Threading;
using Meziantou.Framework.WPF.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections
{
    public class ConcurrentObservableCollectionTests
    {
        [Fact]
        public void CollectionChanged_01()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var eventCalled = false;
            collection.AsObservable.CollectionChanged += (sender, args) => eventCalled = true;

            // Act
            collection.Add(1);

            // Assert
            Assert.True(eventCalled);
        }

        [Fact]
        public void CollectionChanged_02()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var count = 0;
            collection.AsObservable.CollectionChanged += (sender, args) => count++;

            // Act
            collection.Add(1);
            collection.Add(2);
            collection.Add(3);
            collection.Remove(2);

            // Assert
            Assert.Equal(4, count);
        }

        [Fact]
        public void CollectionChanged_03()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var count = 0;
            collection.AsObservable.CollectionChanged += (sender, args) => count++;

            // Act
            collection.Add(1);
            collection.Add(2);
            collection.Add(3);
            collection.Remove(4); // Collection does not contain item

            // Assert
            Assert.Equal(3, count);
        }

        [Fact]
        public void CollectionChanged_04()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var count = 0;
            collection.AsObservable.CollectionChanged += (sender, args) => count++;

            // Act
            collection.AddRange(new []{ 1, 2, 3 });

            // Assert
            Assert.Equal(3, count);
        }

        [Fact]
        public void BatchMode_01()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var count = 0;
            collection.AsObservable.CollectionChanged += (sender, args) => count++;

            // Act
            using (collection.BeginBatch())
            {
                collection.AddRange(new[] { 1, 2, 3 });

                // Assert
                Assert.Equal(0, count);
            }

            Assert.Equal(3, count);
        }

        [Fact]
        public void BatchMode_02()
        {
            // Arrange
            var collection = new ConcurrentObservableCollection<int>(Dispatcher.CurrentDispatcher);

            var count = 0;
            collection.AsObservable.CollectionChanged += (sender, args) => count++;

            // Act
            using (collection.BeginBatch(BatchMode.Reset))
            {
                collection.AddRange(new[] { 1, 2, 3 });

                // Assert
                Assert.Equal(0, count);
            }

            Assert.Equal(1, count);
        }
    }
}
