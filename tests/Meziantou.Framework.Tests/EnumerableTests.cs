using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class EnumerableTests
    {
        [TestMethod]
        public void ReplaceTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 2, 3 };

            // Act
            list.Replace(2, 5);

            // Assert
            CollectionAssert.AreEqual(new List<int> { 1, 5, 3 }, list);
        }

        [TestMethod]
        public void ReplaceTests_02()
        {
            // Arrange
            var list = new List<int>() { 1, 2, 3 };

            // Act
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Replace(10, 5));
        }

        [TestMethod]
        public async Task ForEachAsync()
        {
            var bag = new ConcurrentBag<int>();
            await Enumerable.Range(1, 100).ForEachAsync(async i =>
            {
                await Task.Yield();
                bag.Add(i);
            });

            Assert.AreEqual(100, bag.Count);
        }

        [TestMethod]
        public void MaxTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var max = list.Max(Comparer<int>.Default);

            // Assert
            Assert.AreEqual(10, max);
        }

        [TestMethod]
        public void MaxByTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var max = list.MaxBy(i => i * 2);

            // Assert
            Assert.AreEqual(10, max);
        }

        [TestMethod]
        public void MaxByTests_02()
        {
            // Arrange
            var list = new List<int>() { 1, 10, 2, 3 };

            // Act
            var max = list.MaxBy(i => i * 2, Comparer<int>.Default);

            // Assert
            Assert.AreEqual(10, max);
        }
    }
}
