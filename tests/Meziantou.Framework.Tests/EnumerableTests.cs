﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class EnumerableTests
    {
        [Fact]
        public void ReplaceTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 2, 3 };

            // Act
            list.Replace(2, 5);

            // Assert
            Assert.Equal(new List<int> { 1, 5, 3 }, list);
        }

        [Fact]
        public void ReplaceTests_02()
        {
            // Arrange
            var list = new List<int>() { 1, 2, 3 };

            // Act
            Assert.Throws<ArgumentOutOfRangeException>(() => list.Replace(10, 5));
        }

        [Fact]
        public void AddOrReplaceTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 2, 3 };

            // Act
            list.AddOrReplace(10, 5);

            // Assert
            Assert.Equal(new[] { 1, 2, 3, 5 }, list);
        }

        [Fact]
        public void AddOrReplaceTests_02()
        {
            // Arrange
            var list = new List<string>();

            // Act
            list.AddOrReplace(null, "");

            // Assert
            Assert.Equal(new[] { "" }, list);
        }

        [Fact]
        public void AddOrReplaceTests_03()
        {
            // Arrange
            var list = new List<int> { 1, 2, 3 };

            // Act
            list.AddOrReplace(2, 5);

            // Assert
            Assert.Equal(new[] { 1, 5, 3 }, list);
        }

        [Fact]
        public async Task ForEachAsync()
        {
            var bag = new ConcurrentBag<int>();
            await Enumerable.Range(1, 100).ForEachAsync(async i =>
            {
                await Task.Yield();
                bag.Add(i);
            }).ConfigureAwait(false);

            Assert.Equal(100, bag.Count);
        }

        [Fact]
        public async Task ParallelForEachAsync()
        {
            var bag = new ConcurrentBag<int>();
            await Enumerable.Range(1, 100).ParallelForEachAsync(async i =>
            {
                await Task.Yield();
                bag.Add(i);
            }).ConfigureAwait(false);

            Assert.Equal(100, bag.Count);
        }

        [Fact]
        public void MaxTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var max = list.Max(Comparer<int>.Default);

            // Assert
            Assert.Equal(10, max);
        }

        [Fact]
        public void MaxByTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var max = list.MaxBy(i => i * 2);

            // Assert
            Assert.Equal(10, max);
        }

        [Fact]
        public void MaxByTests_02()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var max = list.MaxBy(i => i * 2, Comparer<int>.Default);

            // Assert
            Assert.Equal(10, max);
        }

        [Fact]
        public void MaxByTests_Nullable()
        {
#nullable enable
            // Arrange
            var list = new List<string?>() { "a", "b" };

            // Act
            var max = list.MaxBy(i => i, Comparer<string>.Default);

            // Assert
            Assert.Equal("b", max);
#nullable disable
        }

        [Fact]
        public void MaxByTests_NotNullable()
        {
#nullable enable
            // Arrange
            var list = new List<string>() { "a", "b" };

            // Act
            var max = list.MaxBy(i => i, Comparer<string>.Default);

            // Assert
            Assert.Equal("b", max);
#nullable disable
        }

        /// <summary>
        /// ////
        /// </summary>
        [Fact]
        public void MinTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var min = list.Min(Comparer<int>.Default);

            // Assert
            Assert.Equal(1, min);
        }

        [Fact]
        public void MinByTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var min = list.MinBy(i => i * 2);

            // Assert
            Assert.Equal(1, min);
        }

        [Fact]
        public void MinByTests_02()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var min = list.MinBy(i => i * 2, Comparer<int>.Default);

            // Assert
            Assert.Equal(1, min);
        }

        [Fact]
        public void TimeSpan_Sum()
        {
            // Arrange
            var list = new List<TimeSpan>() { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(20) };

            // Act
            var sum = list.Sum();

            // Assert
            Assert.Equal(TimeSpan.FromSeconds(23), sum);
        }

        [Fact]
        public void TimeSpan_Average()
        {
            // Arrange
            var list = new List<TimeSpan>() { TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20) };

            // Act
            var sum = list.Average();

            // Assert
            Assert.Equal(TimeSpan.FromSeconds(9), sum);
        }

        [Fact]
        public void EmptyIfNull_Null()
        {
            IEnumerable<string> items = null;
            Assert.Equal(Array.Empty<string>(), items.EmptyIfNull());
        }

        [Fact]
        public void EmptyIfNull_NotNull()
        {
            var items = new string[] { "" };
            Assert.Same(items, items.EmptyIfNull());
        }

#nullable enable
        [Fact]
        [SuppressMessage("Style", "IDE0007:Use implicit type", Justification = "Ensure the type is non nullable")]
        public void WhereNotNull()
        {
            // Arrange
            var list = new List<string?>() { "", null, "a" };

            // Act
            List<string> actual = list.WhereNotNull().ToList();

            // Assert
            Assert.Equal(new[] { "", "a" }, actual);
        }
#nullable disable

        [Fact]
        public void ForeachEnumerator()
        {
            var items = new List<int>();
            foreach (var item in CustomEnumerator())
            {
                items.Add(item);
            }

            Assert.Equal(new[] { 1, 2 }, items);

            static IEnumerator<int> CustomEnumerator()
            {
                yield return 1;
                yield return 2;
            }
        }

        [Fact]
        public async Task ForeachAsyncEnumerator()
        {
            var items = new List<int>();
            await foreach (var item in CustomEnumerator())
            {
                items.Add(item);
            }

            Assert.Equal(new[] { 1, 2 }, items);

            static async IAsyncEnumerator<int> CustomEnumerator()
            {
                await Task.Yield();
                yield return 1;
                yield return 2;
            }
        }
    }
}
