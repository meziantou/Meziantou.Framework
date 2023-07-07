using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("abcé", "abce")]
    [InlineData("abce\u0301", "abce")]
    public void RemoveDiacritics_Test(string str, string expected)
    {
        var actual = str.RemoveDiacritics();
        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", "", true)]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "aBc", true)]
    [InlineData("aabc", "abc", false)]
    public void EqualsIgnoreCase(string left, string right, bool expectedResult)
    {
        left.EqualsIgnoreCase(right).Should().Be(expectedResult);
    }

    [Theory]
    [InlineData("", "", true)]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "aBc", true)]
    [InlineData("aabc", "abc", true)]
    [InlineData("bc", "abc", false)]
    public void ContainsIgnoreCase(string left, string right, bool expectedResult)
    {
        left.ContainsIgnoreCase(right).Should().Be(expectedResult);
    }

    [Fact]
    public void SplitLine_Stop()
    {
        var actual = new List<(string, string)>();
        foreach (var (line, separator) in "a\nb\nc\nd".SplitLines())
        {
            actual.Add((line.ToString(), separator.ToString()));
            if (line.Equals("b", StringComparison.Ordinal))
                break;
        }

        actual.Should().Equal(new[] { ("a", "\n"), ("b", "\n") });
    }

    [Theory]
    [MemberData(nameof(SplitLineData))]
    public void SplitLineSpan(string str, (string Line, string Separator)[] expected)
    {
        var actual = new List<(string, string)>();
        foreach (var (line, separator) in str.SplitLines())
        {
            actual.Add((line.ToString(), separator.ToString()));
        }

        actual.Should().Equal(expected);
    }

    [Theory]
    [MemberData(nameof(SplitLineData))]
    public void SplitLineSpan2(string str, (string Line, string Separator)[] expected)
    {
        var actual = new List<string>();
        foreach (ReadOnlySpan<char> line in str.SplitLines())
        {
            actual.Add(line.ToString());
        }

        actual.Should().Equal(expected.Select(item => item.Line).ToArray());
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

    [Theory]
    [InlineData("", "", StringComparison.Ordinal, "")]
    [InlineData("abc", "c", StringComparison.Ordinal, "ab")]
    [InlineData("abcc", "c", StringComparison.Ordinal, "abc")]
    [InlineData("abcc", "cc", StringComparison.Ordinal, "ab")]
    [InlineData("abcC", "c", StringComparison.Ordinal, "abcC")]
    [InlineData("abC", "c", StringComparison.OrdinalIgnoreCase, "ab")]
    [InlineData("abC", "C", StringComparison.OrdinalIgnoreCase, "ab")]
    [InlineData("abc", "C", StringComparison.OrdinalIgnoreCase, "ab")]
    public void RemoveSuffix(string str, string suffx, StringComparison comparison, string expected)
    {
        str.RemoveSuffix(suffx, comparison).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "", StringComparison.Ordinal, "")]
    [InlineData("abc", "a", StringComparison.Ordinal, "bc")]
    [InlineData("aabc", "a", StringComparison.Ordinal, "abc")]
    [InlineData("aabc", "aa", StringComparison.Ordinal, "bc")]
    [InlineData("Aabc", "a", StringComparison.Ordinal, "Aabc")]
    [InlineData("Abc", "a", StringComparison.OrdinalIgnoreCase, "bc")]
    [InlineData("Abc", "A", StringComparison.OrdinalIgnoreCase, "bc")]
    [InlineData("abc", "A", StringComparison.OrdinalIgnoreCase, "bc")]
    public void RemovePrefix(string str, string suffx, StringComparison comparison, string expected)
    {
        str.RemovePrefix(suffx, comparison).Should().Be(expected);
    }
}
