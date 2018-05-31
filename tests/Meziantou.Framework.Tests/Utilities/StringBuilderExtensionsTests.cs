using System.Text;
using Meziantou.Framework.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.Tests.Utilities
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
    }
}
