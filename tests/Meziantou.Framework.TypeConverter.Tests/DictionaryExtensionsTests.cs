using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;
using Meziantou.Framework.Utilities;

namespace Meziantou.Framework.TypeConverter.Tests
{
    [TestClass]
    public class DictionaryExtensionsTests
    {
        public void GetValue_KeyExists()
        {
            // Arrange
            var dictionary = new Dictionary<string, object>
            {
                { "test", 42 }
            };

            // Act
            var actual = dictionary.GetValueOrDefault("test", "");

            // Assert
            Assert.AreEqual("42", actual);
        }
    }
}
