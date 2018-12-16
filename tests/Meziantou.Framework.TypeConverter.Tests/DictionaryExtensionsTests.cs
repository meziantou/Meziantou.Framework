using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class DictionaryExtensionsTests
    {
        public void GetValue_KeyExists()
        {
            // Arrange
            var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "test", 42 }
            };

            // Act
            var actual = dictionary.GetValueOrDefault("test", "");

            // Assert
            Assert.AreEqual("42", actual);
        }

        public void GetValue_KeyNotExists()
        {
            // Arrange
            var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "test", 42 }
            };

            // Act
            var actual = dictionary.GetValueOrDefault("unknown", "");

            // Assert
            Assert.AreEqual("", actual);
        }

        public void GetValue_KeyNotConvertible()
        {
            // Arrange
            var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "test", "aaa" }
            };

            // Act
            var actual = dictionary.GetValueOrDefault("test", 0);

            // Assert
            Assert.AreEqual(0, actual);
        }
    }
}
