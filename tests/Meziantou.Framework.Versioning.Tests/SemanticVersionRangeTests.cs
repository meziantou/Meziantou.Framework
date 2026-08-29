namespace Meziantou.Framework.Versioning.Tests;

public class SemanticVersionRangeTests
{
    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("1.5.0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("0.9.0", false)]
    public void Satisfies_GreaterThanOrEqual(string versionStr, bool expected)
    {
        var range = SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("1.0.0"));
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData("1.0.0", false)]
    [InlineData("1.0.1", true)]
    [InlineData("2.0.0", true)]
    [InlineData("0.9.0", false)]
    public void Satisfies_GreaterThan(string versionStr, bool expected)
    {
        var range = SemanticVersionRange.GreaterThan(SemanticVersion.Parse("1.0.0"));
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("0.5.0", true)]
    [InlineData("1.0.1", false)]
    public void Satisfies_LessThanOrEqual(string versionStr, bool expected)
    {
        var range = SemanticVersionRange.LessThanOrEqual(SemanticVersion.Parse("1.0.0"));
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData("1.0.0", false)]
    [InlineData("0.5.0", true)]
    [InlineData("0.9.9", true)]
    public void Satisfies_LessThan(string versionStr, bool expected)
    {
        var range = SemanticVersionRange.LessThan(SemanticVersion.Parse("1.0.0"));
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData("1.0.0", true)]
    [InlineData("0.9.9", false)]
    [InlineData("1.0.1", false)]
    public void Satisfies_Exact(string versionStr, bool expected)
    {
        var range = SemanticVersionRange.Exact(SemanticVersion.Parse("1.0.0"));
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData("0.9.0", false)]
    [InlineData("1.0.0", true)]
    [InlineData("1.5.0", true)]
    [InlineData("2.0.0", false)]
    [InlineData("2.0.1", false)]
    public void Satisfies_RangeInclusiveExclusive(string versionStr, bool expected)
    {
        var range = new SemanticVersionRange(
            SemanticVersion.Parse("1.0.0"),
            SemanticVersion.Parse("2.0.0"),
            isMinInclusive: true,
            isMaxInclusive: false);
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Fact]
    public void Satisfies_All_MatchesAnyVersion()
    {
        var range = SemanticVersionRange.All;
        Assert.True(range.Satisfies(SemanticVersion.Parse("0.0.1")));
        Assert.True(range.Satisfies(SemanticVersion.Parse("100.200.300")));
    }

    [Theory]
    [MemberData(nameof(ParseNuGet_ValidData))]
    public void ParseNuGet_ValidFormats(string input, SemanticVersionRange expected)
    {
        var result = SemanticVersionRange.ParseNuGet(input);
        Assert.Equal(expected, result);
    }

    public static TheoryData<string, SemanticVersionRange> ParseNuGet_ValidData()
    {
        return new TheoryData<string, SemanticVersionRange>
        {
            { "1.0.0", SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("1.0.0")) },
            { "[1.0.0]", SemanticVersionRange.Exact(SemanticVersion.Parse("1.0.0")) },
            { "(1.0.0,)", SemanticVersionRange.GreaterThan(SemanticVersion.Parse("1.0.0")) },
            { "[1.0.0,)", SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("1.0.0")) },
            { "(,1.0.0]", SemanticVersionRange.LessThanOrEqual(SemanticVersion.Parse("1.0.0")) },
            { "(,1.0.0)", SemanticVersionRange.LessThan(SemanticVersion.Parse("1.0.0")) },
            {
                "[1.0.0,2.0.0]",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: true)
            },
            {
                "[1.0.0,2.0.0)",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "(1.0.0,2.0.0)",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: false,
                    isMaxInclusive: false)
            },
        };
    }

    [Theory]
    [InlineData("")]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData("[1.0.0")]
    [InlineData("1.0.0]")]
    [InlineData("[invalid]")]
    [InlineData("[1.0.0)")] // A single version needs two inclusive bounds
    [InlineData("(1.0.0]")]
    [InlineData("(1.0.0)")]
    [InlineData("[2.0.0,1.0.0]")] // Inverted bounds
    [InlineData("(2.0.0,1.0.0)")]
    [InlineData("[1.0.1,1.0.0]")]
    [InlineData("[1.0.0,1.0.0)")] // One inclusive and one exclusive bound on a single version
    [InlineData("(1.0.0,1.0.0]")]
    public void ParseNuGet_InvalidFormats_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => SemanticVersionRange.ParseNuGet(input));
    }

    // The strings below are fed to both this library and NuGet's own range parser. NuGet.Versioning
    // is the reference implementation of this syntax, so any disagreement is either a bug here or a
    // deliberate difference that belongs in ParseNuGet_KnownDivergencesFromNuGetVersioning.
    public static TheoryData<string> NuGetRangeSyntax()
    {
        return
        [
            // Valid forms
            "1.0.0",
            "[1.0.0]",
            "[1.0.0,)",
            "(1.0.0,)",
            "(,1.0.0]",
            "(,1.0.0)",
            "[1.0.0,2.0.0]",
            "[1.0.0,2.0.0)",
            "(1.0.0,2.0.0)",
            "(1.0.0,2.0.0]",
            "[1.0.0-alpha,2.0.0)",
            "[1.0.0+build]",
            "[1.0.0,1.0.0]",
            "(1.0.0,1.0.0)",
            // A single version cannot carry one inclusive and one exclusive bound
            "[1.0.0)",
            "(1.0.0]",
            "(1.0.0)",
            "[1.0.0,1.0.0)",
            "(1.0.0,1.0.0]",
            // Inverted bounds
            "[2.0.0,1.0.0]",
            "(2.0.0,1.0.0)",
            "[1.0.1,1.0.0]",
            // Malformed
            "",
            "[",
            "]",
            "[1.0.0",
            "1.0.0]",
            "[invalid]",
            "[1.0.0,2.0.0",
        ];
    }

    [Theory]
    [MemberData(nameof(NuGetRangeSyntax))]
    public void ParseNuGet_AcceptsTheSameStringsAsNuGetVersioning(string input)
    {
        var mine = SemanticVersionRange.TryParseNuGet(input, out _);
        var reference = NuGet.Versioning.VersionRange.TryParse(input, out _);

        Assert.Equal(reference, mine);
    }

    [Theory]
    [MemberData(nameof(NuGetRangeSyntax))]
    public void ParseNuGet_ProducesTheSameBoundsAsNuGetVersioning(string input)
    {
        if (!NuGet.Versioning.VersionRange.TryParse(input, out var reference))
        {
            return;
        }

        var range = SemanticVersionRange.ParseNuGet(input);

        Assert.Equal(reference.MinVersion?.ToFullString(), range.MinVersion?.ToString());
        Assert.Equal(reference.MaxVersion?.ToFullString(), range.MaxVersion?.ToString());
        Assert.Equal(reference.IsMinInclusive, range.IsMinInclusive);
        Assert.Equal(reference.IsMaxInclusive, range.IsMaxInclusive);
    }

    [Theory]
    [MemberData(nameof(NuGetRangeSyntax))]
    public void ParseNuGet_SatisfiesTheSameVersionsAsNuGetVersioning(string input)
    {
        if (!NuGet.Versioning.VersionRange.TryParse(input, out var reference))
        {
            return;
        }

        var range = SemanticVersionRange.ParseNuGet(input);
        string[] versions =
        [
            "0.9.0", "1.0.0", "1.0.0-alpha", "1.0.0-beta.2", "1.0.0+build",
            "1.0.1", "1.5.0", "1.9.9", "2.0.0-rc.1", "2.0.0", "2.0.1", "10.0.0",
        ];

        // Comparing the two sets of matched versions rather than asserting one version at a time
        // means a failure names every version the two parsers disagree about.
        var expected = versions.Where(version => reference.Satisfies(NuGet.Versioning.NuGetVersion.Parse(version))).ToArray();
        var actual = versions.Where(version => range.Satisfies(SemanticVersion.Parse(version))).ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    // NuGet accepts version formats that are not Semantic Versioning 2.0.0. This library parses
    // semantic versions only, so it rejects them by design.
    [InlineData("1.0.0.0")] // Four-part version
    [InlineData("[1.0.0.0,)")]
    [InlineData("[1.0,)")] // Two-part version
    [InlineData("*")] // Floating version
    [InlineData("1.*")]
    // NuGet's parser wants whitespace around an omitted bound; this one does not care.
    [InlineData("(,)")]
    [InlineData("(,]")]
    public void ParseNuGet_KnownDivergencesFromNuGetVersioning(string input)
    {
        var mine = SemanticVersionRange.TryParseNuGet(input, out _);
        var reference = NuGet.Versioning.VersionRange.TryParse(input, out _);

        Assert.NotEqual(reference, mine);
    }

    [Fact]
    public void TryParseNuGet_Null_ReturnsFalse()
    {
        Assert.False(SemanticVersionRange.TryParseNuGet((string?)null, out _));
    }

    [Theory]
    [MemberData(nameof(ParseNpm_ValidData))]
    public void ParseNpm_ValidFormats(string input, SemanticVersionRange expected)
    {
        var result = SemanticVersionRange.ParseNpm(input);
        Assert.Equal(expected, result);
    }

    public static TheoryData<string, SemanticVersionRange> ParseNpm_ValidData()
    {
        return new TheoryData<string, SemanticVersionRange>
        {
            { "1.0.0", SemanticVersionRange.Exact(SemanticVersion.Parse("1.0.0")) },
            { "=1.0.0", SemanticVersionRange.Exact(SemanticVersion.Parse("1.0.0")) },
            { ">1.0.0", SemanticVersionRange.GreaterThan(SemanticVersion.Parse("1.0.0")) },
            { ">=1.0.0", SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("1.0.0")) },
            { "<1.0.0", SemanticVersionRange.LessThan(SemanticVersion.Parse("1.0.0")) },
            { "<=1.0.0", SemanticVersionRange.LessThanOrEqual(SemanticVersion.Parse("1.0.0")) },
            // Wildcards
            { "*", SemanticVersionRange.All },
            { "x", SemanticVersionRange.All },
            { "X", SemanticVersionRange.All },

            // Combined ranges
            {
                ">=1.0.0 <2.0.0",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },

            // Tilde ranges: ~1.2.3 := >=1.2.3 <1.3.0
            {
                "~1.2.3",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.2.3"),
                    SemanticVersion.Parse("1.3.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "~1.2",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.2.0"),
                    SemanticVersion.Parse("1.3.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "~1",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "~0.2.3",
                new SemanticVersionRange(
                    SemanticVersion.Parse("0.2.3"),
                    SemanticVersion.Parse("0.3.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },

            // Caret ranges: ^1.2.3 := >=1.2.3 <2.0.0
            {
                "^1.2.3",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.2.3"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "^0.2.3",
                new SemanticVersionRange(
                    SemanticVersion.Parse("0.2.3"),
                    SemanticVersion.Parse("0.3.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "^0.0.3",
                new SemanticVersionRange(
                    SemanticVersion.Parse("0.0.3"),
                    SemanticVersion.Parse("0.0.4"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "^1.2",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.2.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "^0.0",
                new SemanticVersionRange(
                    SemanticVersion.Parse("0.0.0"),
                    SemanticVersion.Parse("0.1.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "^1",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "^0",
                new SemanticVersionRange(
                    SemanticVersion.Parse("0.0.0"),
                    SemanticVersion.Parse("1.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },

            // Hyphen ranges: 1.0.0 - 2.0.0 := >=1.0.0 <=2.0.0
            {
                "1.0.0 - 2.0.0",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: true)
            },
            {
                "1.0.0 - 2.0",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.1.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "1.0.0 - 2",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("3.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },

            // X-ranges: 1.x := >=1.0.0 <2.0.0
            {
                "1.x",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "1.2.x",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.2.0"),
                    SemanticVersion.Parse("1.3.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "1.X",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "1.*",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.0.0"),
                    SemanticVersion.Parse("2.0.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
            {
                "1.2.*",
                new SemanticVersionRange(
                    SemanticVersion.Parse("1.2.0"),
                    SemanticVersion.Parse("1.3.0"),
                    isMinInclusive: true,
                    isMaxInclusive: false)
            },
        };
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData(">")]
    [InlineData(">=")]
    public void ParseNpm_InvalidFormats_ThrowsFormatException(string input)
    {
        Assert.Throws<FormatException>(() => SemanticVersionRange.ParseNpm(input));
    }

    [Fact]
    public void TryParseNpm_Null_ReturnsFalse()
    {
        Assert.False(SemanticVersionRange.TryParseNpm((string?)null, out _));
    }

    [Fact]
    public void ToString_All_ReturnsWildcard()
    {
        Assert.Equal("*", SemanticVersionRange.All.ToString());
    }

    [Fact]
    public void ToString_Exact_ReturnsBracketed()
    {
        var range = SemanticVersionRange.Exact(SemanticVersion.Parse("1.0.0"));
        Assert.Equal("[1.0.0]", range.ToString());
    }

    [Fact]
    public void ToString_Range_ReturnsIntervalNotation()
    {
        var range = new SemanticVersionRange(
            SemanticVersion.Parse("1.0.0"),
            SemanticVersion.Parse("2.0.0"),
            isMinInclusive: true,
            isMaxInclusive: false);
        Assert.Equal("[1.0.0, 2.0.0)", range.ToString());
    }

    [Fact]
    public void Equality_SameRange_AreEqual()
    {
        var range1 = SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("1.0.0"));
        var range2 = SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("1.0.0"));
        Assert.Equal(range1, range2);
        Assert.True(range1 == range2);
        Assert.False(range1 != range2);
        Assert.Equal(range1.GetHashCode(), range2.GetHashCode());
    }

    [Theory]
    [InlineData("(,)")]
    [InlineData("[,]")]
    [InlineData("[,)")]
    [InlineData("(,]")]
    public void UnboundedRanges_AreEqualToAll(string input)
    {
        var range = SemanticVersionRange.ParseNuGet(input);

        Assert.Equal(SemanticVersionRange.All, range);
        Assert.Equal(SemanticVersionRange.All.GetHashCode(), range.GetHashCode());
        Assert.False(range.IsMinInclusive);
        Assert.False(range.IsMaxInclusive);
    }

    [Fact]
    public void MissingUpperBound_IsNotInclusive()
    {
        var range = SemanticVersionRange.ParseNuGet("[1.0.0,]");

        Assert.Equal(SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("1.0.0")), range);
        Assert.True(range.IsMinInclusive);
        Assert.False(range.IsMaxInclusive);
    }

    [Fact]
    public void Equality_DifferentRange_AreNotEqual()
    {
        var range1 = SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("1.0.0"));
        var range2 = SemanticVersionRange.GreaterThanOrEqual(SemanticVersion.Parse("2.0.0"));
        Assert.NotEqual(range1, range2);
        Assert.False(range1 == range2);
        Assert.True(range1 != range2);
    }

    [Fact]
    public void Satisfies_ThrowsOnNullVersion()
    {
        var range = SemanticVersionRange.All;
        Assert.Throws<ArgumentNullException>(() => range.Satisfies(null!));
    }

    [Theory]
    [InlineData("~1.2.3", "1.2.3", true)]
    [InlineData("~1.2.3", "1.2.4", true)]
    [InlineData("~1.2.3", "1.2.99", true)]
    [InlineData("~1.2.3", "1.3.0", false)]
    [InlineData("~1.2.3", "1.2.2", false)]
    [InlineData("~1.2.3", "2.0.0", false)]
    public void ParseNpm_TildeRange_Satisfies(string rangeStr, string versionStr, bool expected)
    {
        var range = SemanticVersionRange.ParseNpm(rangeStr);
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData("^1.2.3", "1.2.3", true)]
    [InlineData("^1.2.3", "1.9.9", true)]
    [InlineData("^1.2.3", "1.99.99", true)]
    [InlineData("^1.2.3", "2.0.0", false)]
    [InlineData("^1.2.3", "1.2.2", false)]
    [InlineData("^0.2.3", "0.2.3", true)]
    [InlineData("^0.2.3", "0.2.9", true)]
    [InlineData("^0.2.3", "0.3.0", false)]
    [InlineData("^0.0.3", "0.0.3", true)]
    [InlineData("^0.0.3", "0.0.4", false)]
    [InlineData("^0.0.3", "0.0.2", false)]
    public void ParseNpm_CaretRange_Satisfies(string rangeStr, string versionStr, bool expected)
    {
        var range = SemanticVersionRange.ParseNpm(rangeStr);
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData("1.0.0 - 2.0.0", "1.0.0", true)]
    [InlineData("1.0.0 - 2.0.0", "1.5.0", true)]
    [InlineData("1.0.0 - 2.0.0", "2.0.0", true)]
    [InlineData("1.0.0 - 2.0.0", "0.9.9", false)]
    [InlineData("1.0.0 - 2.0.0", "2.0.1", false)]
    public void ParseNpm_HyphenRange_Satisfies(string rangeStr, string versionStr, bool expected)
    {
        var range = SemanticVersionRange.ParseNpm(rangeStr);
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }

    [Theory]
    [InlineData("1.x", "1.0.0", true)]
    [InlineData("1.x", "1.9.9", true)]
    [InlineData("1.x", "2.0.0", false)]
    [InlineData("1.x", "0.9.9", false)]
    [InlineData("1.2.x", "1.2.0", true)]
    [InlineData("1.2.x", "1.2.9", true)]
    [InlineData("1.2.x", "1.3.0", false)]
    [InlineData("1.2.x", "1.1.9", false)]
    public void ParseNpm_XRange_Satisfies(string rangeStr, string versionStr, bool expected)
    {
        var range = SemanticVersionRange.ParseNpm(rangeStr);
        var version = SemanticVersion.Parse(versionStr);
        Assert.Equal(expected, range.Satisfies(version));
    }
}
