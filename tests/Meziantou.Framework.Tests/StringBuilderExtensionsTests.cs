using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests
{
    [TestClass]
    public class StringBuilderExtensionsTests
    {
        [DataTestMethod]
        [DataRow("", 'a', false)]
        [DataRow("abc", 'c', true)]
        [DataRow("abc", 'd', false)]
        public void EndsWith_Test(string str, char c, bool expected)
        {
            var actual = new StringBuilder(str).EndsWith(c);
            Assert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow("", 'a', false)]
        [DataRow("abc", 'a', true)]
        [DataRow("abc", 'c', false)]
        public void StartsWith_Test(string str, char c, bool expected)
        {
            var actual = new StringBuilder(str).StartsWith(c);
            Assert.AreEqual(expected, actual);
        }

        [DataTestMethod]
        [DataRow("", 'a', "")]
        [DataRow("abc", 'a', "bc")]
        [DataRow("aaabc", 'a', "bc")]
        [DataRow("abc", 'b', "abc")]
        public void TrimStart(string str, char c, string expected)
        {
            var actual = new StringBuilder(str);
            actual.TrimStart(c);
            Assert.AreEqual(expected, actual.ToString());
        }

        [DataTestMethod]
        [DataRow("", 'a', "")]
        [DataRow("abc", 'c', "ab")]
        [DataRow("abccc", 'c', "ab")]
        [DataRow("abc", 'b', "abc")]
        public void TrimEnd(string str, char c, string expected)
        {
            var actual = new StringBuilder(str);
            actual.TrimEnd(c);
            Assert.AreEqual(expected, actual.ToString());
        }

        [DataTestMethod]
        [DataRow("", 'a', "")]
        [DataRow("abc", 'c', "ab")]
        [DataRow("cccabccc", 'c', "ab")]
        [DataRow("cccacbccc", 'c', "acb")]
        [DataRow("abc", 'b', "abc")]
        public void Trim(string str, char c, string expected)
        {
            var actual = new StringBuilder(str);
            actual.Trim(c);
            Assert.AreEqual(expected, actual.ToString());
        }
    }
}
