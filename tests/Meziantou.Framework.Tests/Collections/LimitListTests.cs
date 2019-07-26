using System;
using System.Linq;
using Meziantou.Framework.Collections;
using Xunit;

namespace Meziantou.Framework.Tests.Collections
{
    public class LimitListTests
    {
        [Fact]
        public void AddFirst_01()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act
            list.AddFirst(1);
            list.AddFirst(2);

            // Assert
            Assert.Equal(new int[] { 2, 1 }, list.ToList());
        }

        [Fact]
        public void AddFirst_02()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act
            list.AddFirst(1);
            list.AddFirst(2);
            list.AddFirst(3);
            list.AddFirst(4);

            // Assert
            Assert.Equal(new int[] { 4, 3, 2 }, list.ToList());
        }

        [Fact]
        public void AddLast_01()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act
            list.AddLast(1);
            list.AddLast(2);

            // Assert
            Assert.Equal(new int[] { 1, 2 }, list.ToList());
        }

        [Fact]
        public void AddLast_02()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act
            list.AddLast(1);
            list.AddLast(2);
            list.AddLast(3);
            list.AddLast(4);

            // Assert
            Assert.Equal(new int[] { 2, 3, 4 }, list.ToList());
        }

        [Fact]
        public void IndexOf_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);
            list.AddLast(3);

            // Act
            var index = list.IndexOf(2);

            // Assert
            Assert.Equal(1, index);
        }

        [Fact]
        public void IndexOf_02()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);
            list.AddLast(3);
            list.AddLast(4);

            // Act
            var index = list.IndexOf(1);

            // Assert
            Assert.Equal(-1, index);
        }

        [Fact]
        public void Count_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);

            // Act
            var count = list.Count;

            // Assert
            Assert.Equal(2, count);
        }

        [Fact]
        public void Contains_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);

            // Act
            var result = list.Contains(2);

            // Assert
            Assert.Equal(true, result);
        }

        [Fact]
        public void Contains_02()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);

            // Act
            var result = list.Contains(3);

            // Assert
            Assert.Equal(false, result);
        }

        [Fact]
        public void Remove_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);

            // Act
            var result = list.Remove(1);

            // Assert
            Assert.Equal(true, result);
            Assert.Equal(1, list.Count);
            Assert.Equal(2, list[0]);
        }

        [Fact]
        public void Remove_02()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);
            list.AddLast(3);

            // Act
            var result = list.Remove(2);

            // Assert
            Assert.True(result);
            Assert.Equal(2, list.Count);
            Assert.Equal(1, list[0]);
            Assert.Equal(3, list[1]);
        }

        [Fact]
        public void Remove_03()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);

            // Act
            var result = list.Remove(4);

            // Assert
            Assert.False(result);
            Assert.Equal(1, list.Count);
            Assert.Equal(1, list[0]);
        }

        [Fact]
        public void Indexer_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);

            // Act
            list[0] = 10;

            // Assert
            Assert.Equal(1, list.Count);
            Assert.Equal(10, list[0]);
        }

        [Fact]
        public void Indexer_02()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);

            // Act
            list[1] = 10;

            // Assert
            Assert.Equal(2, list.Count);
            Assert.Equal(1, list[0]);
            Assert.Equal(10, list[1]);
        }

        [Fact]
        public void Indexer_03()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act/Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => list[1] = 10);
        }

        [Fact]
        public void RemoveAt()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);
            list.AddLast(3);

            // Act
            list.RemoveAt(1);

            // Assert
            Assert.Equal(2, list.Count);
            Assert.Equal(1, list[0]);
            Assert.Equal(3, list[1]);
        }
    }
}
