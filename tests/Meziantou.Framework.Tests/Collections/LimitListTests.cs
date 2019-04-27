using System;
using System.Linq;
using Meziantou.Framework.Collections;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Collections
{
    [TestClass]
    public class LimitListTests
    {
        [TestMethod]
        public void AddFirst_01()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act
            list.AddFirst(1);
            list.AddFirst(2);

            // Assert
            CollectionAssert.AreEquivalent(new int[] { 2, 1 }, list.ToList());
        }

        [TestMethod]
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
            CollectionAssert.AreEquivalent(new int[] { 4, 3, 2 }, list.ToList());
        }

        [TestMethod]
        public void AddLast_01()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act
            list.AddLast(1);
            list.AddLast(2);

            // Assert
            CollectionAssert.AreEquivalent(new int[] { 1, 2 }, list.ToList());
        }

        [TestMethod]
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
            CollectionAssert.AreEquivalent(new int[] { 2, 3, 4 }, list.ToList());
        }

        [TestMethod]
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
            Assert.AreEqual(1, index);
        }

        [TestMethod]
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
            Assert.AreEqual(-1, index);
        }

        [TestMethod]
        public void Count_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);

            // Act
            var count = list.Count;

            // Assert
            Assert.AreEqual(2, count);
        }

        [TestMethod]
        public void Contains_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);

            // Act
            var result = list.Contains(2);

            // Assert
            Assert.AreEqual(true, result);
        }

        [TestMethod]
        public void Contains_02()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);

            // Act
            var result = list.Contains(3);

            // Assert
            Assert.AreEqual(false, result);
        }

        [TestMethod]
        public void Remove_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);
            list.AddLast(2);

            // Act
            var result = list.Remove(1);

            // Assert
            Assert.AreEqual(true, result);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(2, list[0]);
        }

        [TestMethod]
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
            Assert.IsTrue(result);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(1, list[0]);
            Assert.AreEqual(3, list[1]);
        }

        [TestMethod]
        public void Remove_03()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);

            // Act
            var result = list.Remove(4);

            // Assert
            Assert.IsFalse(result);
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(1, list[0]);
        }

        [TestMethod]
        public void Indexer_01()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);

            // Act
            list[0] = 10;

            // Assert
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(10, list[0]);
        }

        [TestMethod]
        public void Indexer_02()
        {
            // Arrange
            var list = new LimitList<int>(3);
            list.AddLast(1);

            // Act
            list[1] = 10;

            // Assert
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(1, list[0]);
            Assert.AreEqual(10, list[1]);
        }

        [TestMethod]
        public void Indexer_03()
        {
            // Arrange
            var list = new LimitList<int>(3);

            // Act/Assert
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => list[1] = 10);
        }

        [TestMethod]
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
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual(1, list[0]);
            Assert.AreEqual(3, list[1]);
        }
    }
}
