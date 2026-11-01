using System.Buffers;
using Meziantou.Xunit;

namespace Meziantou.Framework.Tests;

public class StringExtensionsTests
{
    [Theory]
    [InlineData("abc", "abc")]
    [InlineData("abcé", "abce")]
    [InlineData("abce\u0301", "abce")]
    [InlineData("\u00c0\u1ea0\u01d5", "AAU")]
    [InlineData("\u0623\u0929\uf900", "\u0627\u0928\u8c48")] // Lo
    [InlineData("\u2260\u226e", "=<")] // Sm
    [InlineData("\u1fed\u0385", "\u00a8\u00a8")] // Sk
    [InlineData("\u037e", ";")] // Po
    public void RemoveDiacritics_Test(string str, string expected)
    {
        var actual = str.RemoveDiacritics();
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("Hello, World! 0123456789 ~\u007F\0")]
    public void RemoveDiacritics_Ascii_ReturnsSameInstance(string str)
    {
        Assert.Same(str, str.RemoveDiacritics());
    }

    [Theory]
    [InlineData("abcde\u0301", "abcde")]
    [InlineData("abcd\u00e9fgh", "abcdefgh")]
    [InlineData("\u00e9abc", "eabc")]
    [InlineData("abc\u00e9", "abce")]
    [InlineData("abc\u0301\u00e9", "abce")]
    [InlineData("abc d\u0308e\u0301", "abc de")]
    public void RemoveDiacritics_AsciiPrefix(string str, string expected)
    {
        Assert.Equal(expected, str.RemoveDiacritics());
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void RemoveDiacritic_MatchesNormalization()
    {
        for (var i = 0; i <= char.MaxValue; i++)
        {
            var c = (char)i;
            // string.Normalize rejects the noncharacters
            if (char.IsSurrogate(c) || c is (>= '\uFDD0' and <= '\uFDEF') or '\uFFFE' or '\uFFFF')
                continue;

            // A character the runtime does not decompose may come from a newer version of Unicode than the one of the runtime
            var expected = c.ToString().RemoveDiacritics();
            if (expected == c.ToString())
                continue;

            var expectedChar = expected.Length == 1 ? expected[0] : c;
            Assert.Equal(expectedChar, StringExtensions.RemoveDiacritic(c));
        }
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("", "", true)]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "aBc", true)]
    [InlineData("aabc", "abc", false)]
    public void EqualsIgnoreCase(string? left, string? right, bool expectedResult)
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
    [MemberData(nameof(SplitLineData))]
    public void SplitLineSpan_FromReadOnlySpan(string str, (string Line, string Separator)[] expected)
    {
        var actual = new List<(string, string)>();
        foreach (var (line, separator) in str.AsSpan().SplitLines())
        {
            actual.Add((line.ToString(), separator.ToString()));
        }

        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(SplitLineWithLineBreakModeData))]
    public void SplitLineSpan_FromReadOnlySpan_WithLineBreakMode(string str, LineBreakMode lineBreakMode, (string Line, string Separator)[] expected)
    {
        var actual = new List<(string, string)>();
        foreach (var (line, separator) in str.AsSpan().SplitLines(lineBreakMode))
        {
            actual.Add((line.ToString(), separator.ToString()));
        }

        Assert.Equal(expected, actual);
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
    [InlineData("", "", '_')]
    [InlineData("abc", "abc", '_')]
    [InlineData("a,b.c", "a_b_c", '_')]
    [InlineData("a-b/c", "a_b_c", '_')]
    [InlineData("..a..", "__a__", '_')]
    public void ReplaceAny(string input, string expected, char newValue)
    {
        var actual = input.ReplaceAny(SearchValues.Create(".,-/"), newValue);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ReplaceAny_NoMatch_ReturnsSameInstance()
    {
        var input = "abcdef";
        var actual = input.ReplaceAny(SearchValues.Create(".,-/"), '_');
        Assert.Same(input, actual);
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

    [Theory]
    // A wholly ignorable affix stands for nothing, so it removes nothing
    [InlineData("abc", "\u200D", "abc")]
    [InlineData("abc", "", "abc")]
    // The suffix is ordinally identical to the end of the string, so the match is 2 chars long
    [InlineData("cafe\u0301", "e\u0301", "caf")]
    [InlineData("abc", "c", "ab")]
    [InlineData("abc", "d", "abc")]
    public void RemoveSuffix_CultureSensitive(string str, string suffix, string expected)
    {
        Assert.Equal(expected, RemoveSuffixInInvariantCulture(str, suffix));
    }

    [Theory]
    [InlineData("abc", "\u200D", "abc")]
    [InlineData("abc", "", "abc")]
    [InlineData("abc", "a", "bc")]
    [InlineData("abc", "d", "abc")]
    public void RemovePrefix_CultureSensitive(string str, string prefix, string expected)
    {
        Assert.Equal(expected, RemovePrefixInInvariantCulture(str, prefix));
    }

    // "e" followed by a combining acute accent is canonically equivalent to the precomposed "\u00E9",
    // but recognising that needs ICU. These two tests pin both supported configurations.
    [Fact, RunIf(globalizationMode: TestGlobalizationMode.NotInvariant)]
    public void RemoveAffix_CanonicalEquivalence()
    {
        Assert.Equal("caf", RemoveSuffixInInvariantCulture("cafe\u0301", "\u00E9"));
        Assert.Equal("cole", RemovePrefixInInvariantCulture("e\u0301cole", "\u00E9"));
    }

    [Fact, RunIf(globalizationMode: TestGlobalizationMode.Invariant)]
    public void RemoveAffix_CanonicalEquivalence_InvariantGlobalization()
    {
        // Without ICU a linguistic comparison degrades to ordinal, so the decomposed form does not
        // match the precomposed one and nothing is removed
        Assert.Equal("cafe\u0301", RemoveSuffixInInvariantCulture("cafe\u0301", "\u00E9"));
        Assert.Equal("e\u0301cole", RemovePrefixInInvariantCulture("e\u0301cole", "\u00E9"));
    }

    private static string RemoveSuffixInInvariantCulture(string str, string suffix)
        => CultureInfoUtilities.UseCulture(CultureInfo.InvariantCulture, () => str.RemoveSuffix(suffix, StringComparison.CurrentCulture));

    private static string RemovePrefixInInvariantCulture(string str, string prefix)
        => CultureInfoUtilities.UseCulture(CultureInfo.InvariantCulture, () => str.RemovePrefix(prefix, StringComparison.CurrentCulture));

    [Fact]
    public void RemoveSuffix_UnsupportedComparisonThrows()
    {
        Assert.Throws<ArgumentException>(() => "abc".RemoveSuffix("c", (StringComparison)42));
    }

    [Fact]
    public void RemovePrefix_UnsupportedComparisonThrows()
    {
        Assert.Throws<ArgumentException>(() => "abc".RemovePrefix("a", (StringComparison)42));
    }

    [Fact]
    public void Documentation_Comparisons()
    {
        #region StringExtensions_Comparisons
        // Ordinal comparisons, with or without case
        Assert.True("Content-Type".EqualsIgnoreCase("content-type"));
        Assert.False("Content-Type".EqualsOrdinal("content-type"));
        Assert.True("Content-Type: text/html".ContainsIgnoreCase("TEXT/HTML"));

        // EqualsIgnoreCase and EqualsOrdinal accept null values
        string? missing = null;
        Assert.False(missing.EqualsIgnoreCase("value"));
        Assert.True(missing.EqualsOrdinal(null));
        #endregion
    }

    [Fact]
    public void Documentation_RemovePrefixAndSuffix()
    {
        #region StringExtensions_RemovePrefixAndSuffix
        Assert.Equal("Home", "HomeController".RemoveSuffix("Controller", StringComparison.Ordinal));
        Assert.Equal("Repository", "IRepository".RemovePrefix("I", StringComparison.Ordinal));

        // Nothing is removed when the string does not start or end with the value
        Assert.Equal("Repository", "Repository".RemoveSuffix("Controller", StringComparison.Ordinal));

        // Choose the comparison, for instance to ignore case
        Assert.Equal("Home", "HomeCONTROLLER".RemoveSuffix("Controller", StringComparison.OrdinalIgnoreCase));
        #endregion
    }

    [Fact]
    public void Documentation_Nullify()
    {
        #region StringExtensions_Nullify
        // Turn empty (and optionally whitespace-only) strings into null
        Assert.Null("".Nullify(trim: false));
        Assert.Null("   ".Nullify(trim: true));
        Assert.Equal("   ", "   ".Nullify(trim: false));

        // With trim: true, the returned value is trimmed
        Assert.Equal("value", "  value  ".Nullify(trim: true));
        #endregion
    }

    [Fact]
    public void Documentation_RemoveDiacritics()
    {
        #region StringExtensions_RemoveDiacritics
        Assert.Equal("Creme brulee a l'Ile-de-France", "Crème brûlée à l'Île-de-France".RemoveDiacritics());
        #endregion
    }

    [Fact]
    public void Documentation_ReplaceAny()
    {
        #region StringExtensions_ReplaceAny
        var invalidCharacters = SearchValues.Create(@"/\:*?");
        Assert.Equal("reports_2024_q1_draft_", @"reports/2024\q1:draft?".ReplaceAny(invalidCharacters, '_'));
        #endregion
    }

    [Fact]
    public void Documentation_SplitLines()
    {
        #region StringExtensions_SplitLines
        var lines = new List<string>();
        var separators = new List<string>();
        foreach (var (line, separator) in "first\r\nsecond\nthird".SplitLines())
        {
            lines.Add(line.ToString());
            separators.Add(separator.ToString());
        }

        Assert.Equal(["first", "second", "third"], lines);
        Assert.Equal(["\r\n", "\n", ""], separators);
        #endregion
    }

    [Fact]
    public void Documentation_SplitLinesMode()
    {
        #region StringExtensions_SplitLinesMode
        var unicodeLines = new List<string>();
        foreach (var line in "a\u2028b".SplitLines())
        {
            unicodeLines.Add(line.Line.ToString());
        }

        var standardLines = new List<string>();
        foreach (var line in "a\u2028b".SplitLines(LineBreakMode.Standard))
        {
            standardLines.Add(line.Line.ToString());
        }

        // By default, Unicode line separators such as U+2028 also break lines
        Assert.Equal(["a", "b"], unicodeLines);

        // LineBreakMode.Standard only breaks lines on \r and \n
        Assert.Equal(["a\u2028b"], standardLines);
        #endregion
    }
}
