using Xunit;

namespace Meziantou.Framework.CommandLineTests
{
    public class CommandLineBuilderTests
    {
        [Theory]
        [InlineData("a", "a")]
        [InlineData("arg 1", @"""arg 1""")]
        [InlineData(@"\some\path with\spaces", @"""\some\path with\spaces""")]
        [InlineData(@"a\\b", @"a\\b")]
        [InlineData(@"a\\\\b", @"a\\\\b")]
        [InlineData(@"""a", @"""\""a""")]
        public void WindowsQuotedArgument_Test(string value, string expected)
        {
            var args = CommandLineBuilder.WindowsQuotedArgument(value);
            Assert.Equal(expected, args);
        }

        [Theory]
        [InlineData("a", "a")]
        [InlineData("arg 1", @"""arg 1""")]
        [InlineData("a^b", @"""a^^b""")]
        [InlineData("a|b", @"""a^|b""")]
        public void WindowsCmdArgument_Test(string value, string expected)
        {
            var args = CommandLineBuilder.WindowsCmdArgument(value);
            Assert.Equal(expected, args);
        }
    }
}
