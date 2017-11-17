using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Meziantou.Framework.CommandLineTests
{
    [TestClass]
    public class CommandLineBuilderTests
    {
        [DataTestMethod]
        [DataRow("a", "a")]
        [DataRow("arg 1", @"""arg 1""")]
        [DataRow(@"\some\path with\spaces", @"""\some\path with\spaces""")]
        [DataRow(@"a\\b", @"a\\b")]
        [DataRow(@"a\\\\b", @"a\\\\b")]
        [DataRow(@"""a", @"""\""a""")]
        public void WindowsQuotedArgument_Test(string value, string expected)
        {
            var builder = new CommandLineBuilder();
            var args = builder.WindowsQuotedArgument(value);
            Assert.AreEqual(expected, args);
        }

        [DataTestMethod]
        [DataRow("a", "a")]
        [DataRow("arg 1", @"""arg 1""")]
        [DataRow("a^b", @"""a^^b""")]
        [DataRow("a|b", @"""a^|b""")]
        public void WindowsCmdArgument_Test(string value, string expected)
        {
            var builder = new CommandLineBuilder();
            var args = builder.WindowsCmdArgument(value);
            Assert.AreEqual(expected, args);
        }
    }
}
