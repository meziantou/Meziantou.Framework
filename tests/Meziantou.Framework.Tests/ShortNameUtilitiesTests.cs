using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class ShortNameUtilitiesTests
    {
        [TestMethod]
        public void CreateShortName_01()
        {
            // Arrange
            var name = "bbb";
            var names = new List<string> { "aaa", "aab" };

            // Act
            var shortName = ShortName.Create(names, 3, name);

            // Assert
            Assert.AreEqual("bbb", shortName);
        }

        [TestMethod]
        public void CreateShortName_02()
        {
            // Arrange
            var name = "aaa";
            var names = new List<string> { "aaa", "aab" };

            // Act
            var shortName = ShortName.Create(names, 3, name);

            // Assert
            Assert.AreEqual("aa0", shortName);
        }

        [TestMethod]
        public void BuildShortNames_01()
        {
            // Arrange
            var names = new List<string> { "aaaa", "aaab", "aaa", "aab", "other" };

            // Act
            var shortNames = ShortName.Create(names, 3);

            // Assert
            Assert.AreEqual("aa0", shortNames["aaaa"]);
            Assert.AreEqual("aa1", shortNames["aaab"]);
            Assert.AreEqual("aaa", shortNames["aaa"]);
            Assert.AreEqual("aab", shortNames["aab"]);
            Assert.AreEqual("oth", shortNames["other"]);
        }
    }
}
