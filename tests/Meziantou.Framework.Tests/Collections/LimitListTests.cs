using System;
using System.Linq;
using FluentAssertions;
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
            list.ToList().Should().Equal(new int[] { 2, 1 });
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
            list.ToList().Should().Equal(new int[] { 4, 3, 2 });
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
            list.ToList().Should().Equal(new int[] { 1, 2 });
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
            list.ToList().Should().Equal(new int[] { 2, 3, 4 });
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
            index.Should().Be(1);
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
            index.Should().Be(-1);
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
            count.Should().Be(2);
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
            result.Should().BeTrue();
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
            result.Should().BeFalse();
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
            result.Should().BeTrue();
            list.Should().Equal(2);
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
            result.Should().BeTrue();
            list.Should().Equal(1, 3);
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
            result.Should().BeFalse();
            list.Should().Equal(1);
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
            list.Should().Equal(10);
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
            list.Should().Equal(1, 10);
        }

        [Fact]
        public void Indexer_03()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act/Assert
            new Func<object>(() => list[1] = 10).Should().ThrowExactly<ArgumentOutOfRangeException>();
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
            list.Should().Equal(1, 3);
        }
    }
}
