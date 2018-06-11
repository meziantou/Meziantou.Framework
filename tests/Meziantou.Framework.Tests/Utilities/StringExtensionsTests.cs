using Microsoft.VisualStudio.TestTools.UnitTesting;
using Meziantou.Framework.Utilities;

namespace Meziantou.Framework.Tests.Utilities
{
    [TestClass]
    public class StringExtensionsTests
    {
        [DataTestMethod]
        [DataRow("abc", "abc")]
        [DataRow("abcé", "abce")]
        public void RemoveDiacritics_Test(string str, string expected)
        {
            var actual = str.RemoveDiacritics();
            Assert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow("", 'a', false)]
        [DataRow("abc", 'c', true)]
        [DataRow("abc", 'd', false)]
        public void EndsWith_Test(string str, char c, bool expected)
        {
            var actual = str.EndsWith(c);
            Assert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow("", 'a', false)]
        [DataRow("abc", 'a', true)]
        [DataRow("abc", 'c', false)]
        public void StartsWith_Test(string str, char c, bool expected)
        {
            var actual = str.StartsWith(c);
            Assert.AreEqual(expected, actual);
        }

        [TestMethod]
        public void Replace_ShouldReplaceAllOccurences()
        {
            var actual = "abcABC".Replace("ab", "ba", System.StringComparison.OrdinalIgnoreCase);
            Assert.AreEqual("bacbaC", actual);
        }

        [TestMethod]
        public void Replace_ShouldUseStringComparison()
        {
            var actual = "abcABC".Replace("ab", "ba", System.StringComparison.Ordinal);
            Assert.AreEqual("bacABC", actual);
        }

        [DataTestMethod]
        [DataRow(null, null, true)]
        [DataRow("", "", true)]
        [DataRow("abc", "abc", true)]
        [DataRow("abc", "aBc", true)]
        [DataRow("aabc", "abc", false)]
        public void EqualsIgnoreCase(string left, string right, bool expectedResult)
        {
            Assert.AreEqual(expectedResult, left.EqualsIgnoreCase(right));
        }

        [DataTestMethod]
        [DataRow(null, null, true)]
        [DataRow("", "", true)]
        [DataRow("abc", "abc", true)]
        [DataRow("abc", "aBc", true)]
        [DataRow("aabc", "abc", true)]
        [DataRow("bc", "abc", false)]
        public void ContainsIgnoreCase(string left, string right, bool expectedResult)
        {
            Assert.AreEqual(expectedResult, left.ContainsIgnoreCase(right));
        }
    }
}
