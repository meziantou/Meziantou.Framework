using System;
using System.Collections.Generic;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public class DictionaryExtensionsTests
    {
        [Fact]
        public static void GetValue_KeyExists()
        {
            // Arrange
            var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "test", 42 },
            };

            // Act
            var actual = dictionary.GetValueOrDefault("test", "");

            // Assert
            Assert.Equal("42", actual);
        }

        [Fact]
        public static void GetValue_KeyNotExists()
        {
            // Arrange
            var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "test", 42 },
            };

            // Act
            var actual = dictionary.GetValueOrDefault("unknown", "");

            // Assert
            Assert.Equal("", actual);
        }

        [Fact]
        public static void GetValue_KeyNotConvertible()
        {
            // Arrange
            var dictionary = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "test", "aaa" },
            };

            // Act
            var actual = dictionary.GetValueOrDefault("test", 0);

            // Assert
            Assert.Equal(0, actual);
        }
    }
}
