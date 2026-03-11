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
        Assert.Equal(expected, actual);
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
    [InlineData("", "", true)]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "aBc", true)]
    [InlineData("aabc", "abc", true)]
    [InlineData("bc", "abc", false)]
    public void ContainsIgnoreCase(string left, string right, bool expectedResult)
    {
        Assert.Equal(expectedResult, left.ContainsIgnoreCase(right));
    }

    [Fact]
    public void SplitLine_Stop()
    {
        var actual = new List<(string, string)>();
        foreach (var (line, separator) in "a\nb\nc\nd".SplitLines())
        {
            actual.Add((line.ToString(), separator.ToString()));
            if (line is "b")
                break;
        }

        Assert.Equal([("a", "\n"), ("b", "\n")], actual);
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

        Assert.Equal(expected, actual);
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

        Assert.Equal(expected.Select(item => item.Line).ToArray(), actual);
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
            { "ab\u0085cd", new[] { ("ab", "\u0085"), ("cd", "") } },
            { "ab\u2028cd", new[] { ("ab", "\u2028"), ("cd", "") } },
            { "ab\u2029cd", new[] { ("ab", "\u2029"), ("cd", "") } },
            { "\ncd", new[] { ("", "\n"), ("cd", "") } },
        };
    }

    [Theory]
    [MemberData(nameof(SplitLineWithLineBreakModeData))]
    public void SplitLineSpan_WithLineBreakMode(string str, LineBreakMode lineBreakMode, (string Line, string Separator)[] expected)
    {
        var actual = new List<(string, string)>();
        foreach (var (line, separator) in str.SplitLines(lineBreakMode))
        {
            actual.Add((line.ToString(), separator.ToString()));
        }

        Assert.Equal(expected, actual);
    }

    public static TheoryData<string, LineBreakMode, (string Line, string Separator)[]> SplitLineWithLineBreakModeData()
    {
        return new TheoryData<string, LineBreakMode, (string Line, string Separator)[]>
        {
            { "ab\u0085cd\u2028ef\u2029gh\vij\fkl", LineBreakMode.Standard, new[] { ("ab\u0085cd\u2028ef\u2029gh\vij\fkl", "") } },
            { "ab\u0085cd\u2028ef\u2029gh\vij\fkl", LineBreakMode.Unicode, new[] { ("ab", "\u0085"), ("cd", "\u2028"), ("ef", "\u2029"), ("gh\vij\fkl", "") } },
            { "ab\u0085cd\u2028ef\u2029gh\vij\fkl", LineBreakMode.UnicodeWithLegacyControls, new[] { ("ab", "\u0085"), ("cd", "\u2028"), ("ef", "\u2029"), ("gh", "\v"), ("ij", "\f"), ("kl", "") } },
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
        Assert.Equal(expected, str.RemoveSuffix(suffx, comparison));
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
        Assert.Equal(expected, str.RemovePrefix(suffx, comparison));
    }
}
