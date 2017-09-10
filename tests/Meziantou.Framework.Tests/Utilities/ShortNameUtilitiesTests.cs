using System.Collections.Generic;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class ShortNameUtilitiesTests
    {
        [TestMethod]
        public void CreateShortName_01()
        {
            // Arrange
            string name = "bbb";
            var names = new List<string> { "aaa", "aab" };

            // Act
            var shortName = ShortNameUtilities.CreateShortName(names, 3, name);

            // Assert
            Assert.AreEqual("bbb", shortName);
        }

        [TestMethod]
        public void CreateShortName_02()
        {
            // Arrange
            string name = "aaa";
            var names = new List<string> { "aaa", "aab" };

            // Act
            var shortName = ShortNameUtilities.CreateShortName(names, 3, name);

            // Assert
            Assert.AreEqual("aa0", shortName);
        }

        [TestMethod]
        public void BuildShortNames_01()
        {
            // Arrange
            var names = new List<string> { "aaaa", "aaab", "aaa", "aab", "other" };

            // Act
            var shortNames = ShortNameUtilities.BuildShortNames(names, 3);

            // Assert
            Assert.AreEqual("aa0", shortNames["aaaa"]);
            Assert.AreEqual("aa1", shortNames["aaab"]);
            Assert.AreEqual("aaa", shortNames["aaa"]);
            Assert.AreEqual("aab", shortNames["aab"]);
            Assert.AreEqual("oth", shortNames["other"]);
        }
    }
}
