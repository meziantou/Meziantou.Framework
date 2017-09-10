using System;
using System.Collections.Generic;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class EnumerableTests
    {
        public void ReplaceTests_01()
        {
            // Arrange
            var list = new List<int>() { 1, 2, 3 };

            // Act
            list.Replace(2, 5);

            // Assert
            CollectionAssert.AreEqual(new List<int> { 1, 5, 3 }, list);
        }

        public void ReplaceTests_02()
        {
            // Arrange
            var list = new List<int>() { 1, 2, 3 };

            // Act
            Assert.ThrowsException<ArgumentOutOfRangeException>(() => list.Replace(10, 5));
        }
    }
}
