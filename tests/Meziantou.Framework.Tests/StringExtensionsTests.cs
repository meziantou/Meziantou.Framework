using System;
using System.Collections.Generic;
using System.Linq;
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

        [Theory]
        [MemberData(nameof(SplitLineData))]
        public void SplitLine(string str, (string Line, string Separator)[] expected)
        {
            var actual = str.SplitLines();

            Assert.Equal(expected.Select(t => t.Line).ToList(), actual.ToList());
        }

        [Theory]
        [MemberData(nameof(SplitLineData))]
        public void SplitLine_Span(string str, (string Line, string Separator)[] expected)
        {
            var actual = new List<(string, string)>();
            str.SplitLines((line, separator) => actual.Add((line.ToString(), separator.ToString())));

            Assert.Equal(expected, actual);
        }

        [Theory]
        [MemberData(nameof(SplitLineData))]
        public void SplitLine_SpanFunc(string str, (string Line, string Separator)[] expected)
        {
            var actual = new List<(string, string)>();
            str.SplitLines((line, separator) =>
            {
                actual.Add((line.ToString(), separator.ToString()));
                return true;
            });

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SplitLine_SpanFunc_Stop()
        {
            var actual = new List<(string, string)>();
            "a\nb\nc\nd".SplitLines((line, separator) =>
            {
                actual.Add((line.ToString(), separator.ToString()));
                return !line.Equals("b", StringComparison.Ordinal);
            });

            Assert.Equal(new[] { ("a", "\n"), ("b", "\n") }, actual);
        }

        public static TheoryData<string, (string Line, string Separator)[]> SplitLineData()
        {
            return new TheoryData<string, (string Line, string Separator)[]>
            {
                { "", Array.Empty<(string, string)>() },
                { "ab", new[] { ("ab", "") } },
                { "ab\r\n", new[] { ("ab", "\r\n") } },
                { "ab\r\ncd", new[] { ("ab", "\r\n"), ("cd", "") } },
                { "ab\rcd", new[] { ("ab", "\r"), ("cd", "") } },
                { "ab\ncd", new[] { ("ab", "\n"), ("cd", "") } },
                { "\ncd", new[] { ("", "\n"), ("cd", "") } },
            };
        }
    }
}
