using Xunit;

namespace Meziantou.Framework.Tests
{
    public class StringExtensionsTests
    {
        [Theory]
        [InlineData("abc", "abc")]
        [InlineData("abcé", "abce")]
        public void RemoveDiacritics_Test(string str, string expected)
        {
            var actual = str.RemoveDiacritics();
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("", 'a', false)]
        [InlineData("abc", 'c', true)]
        [InlineData("abc", 'd', false)]
        public void EndsWith_Test(string str, char c, bool expected)
        {
            var actual = str.EndsWith(c);
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("", 'a', false)]
        [InlineData("abc", 'a', true)]
        [InlineData("abc", 'c', false)]
        public void StartsWith_Test(string str, char c, bool expected)
        {
            var actual = str.StartsWith(c);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void Replace_ShouldReplaceAllOccurences()
        {
            var actual = "abcABC".Replace("ab", "ba", System.StringComparison.OrdinalIgnoreCase);
            Assert.Equal("bacbaC", actual);
        }

        [Fact]
        public void Replace_ShouldUseStringComparison()
        {
            var actual = "abcABC".Replace("ab", "ba", System.StringComparison.Ordinal);
            Assert.Equal("bacABC", actual);
        }

        [Theory]
        [InlineData(null, null, true)]
        [InlineData("", "", true)]
        [InlineData("abc", "abc", true)]
        [InlineData("abc", "aBc", true)]
        [InlineData("aabc", "abc", false)]
        public void EqualsIgnoreCase(string left, string right, bool expectedResult)
        {
            Assert.Equal(expectedResult, left.EqualsIgnoreCase(right));
        }

        [Theory]
        [InlineData(null, null, true)]
        [InlineData("", "", true)]
        [InlineData("abc", "abc", true)]
        [InlineData("abc", "aBc", true)]
        [InlineData("aabc", "abc", true)]
        [InlineData("bc", "abc", false)]
        public void ContainsIgnoreCase(string left, string right, bool expectedResult)
        {
            Assert.Equal(expectedResult, left.ContainsIgnoreCase(right));
        }
    }
}
