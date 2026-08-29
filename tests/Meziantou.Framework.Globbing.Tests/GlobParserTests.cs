using Meziantou.Framework.Globbing.Internals;
using Meziantou.Framework.Globbing.Internals.Segments;

namespace Meziantou.Framework.Globbing.Tests;

public class GlobParserTests
{
    private static Segment[] GetSegments(string pattern)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        return glob._segments;
    }

    private static Segment[] GetSubSegments(string pattern)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.All(glob._segments, item => Assert.IsType<RaggedSegment>(item));
        return ((RaggedSegment)glob._segments[0])._segments;
    }

    [Theory]
    [InlineData("*")]
    public void ValidPatterns(string content)
    {
        Glob.Parse(content, GlobDialect.Standard);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void InvalidPatterns(string? content)
    {
        Assert.Throws<ArgumentException>(() => Glob.Parse(content!, GlobDialect.Standard));
    }

    [Fact]
    public void OptimizeSegmentEndsWith()
    {
        var segments = GetSegments("*.txt");
        Assert.Collection(segments, item => Assert.IsType<EndsWithSegment>(item));
    }

    [Fact]
    public void OptimizeSegmentEndsWithWithPrefix()
    {
        var segments = GetSubSegments("p*.txt");
        Assert.Collection(segments,
            item => Assert.IsType<LiteralSegment>(item),
            item => Assert.IsType<EndsWithSegment>(item));
    }

    [Fact]
    public void OptimizeSegmentStartsWith()
    {
        var segments = GetSegments("file*");
        Assert.Collection(segments, item => Assert.IsType<StartsWithSegment>(item));
    }

    [Fact]
    public void OptimizeSegmentContains()
    {
        var segments = GetSegments("*file*");
        Assert.Collection(segments, item => Assert.IsType<ContainsSegment>(item));
    }

    [Fact]
    public void OptimizeSegmentConsecutiveStarts()
    {
        var segments = GetSegments("*file**");
        Assert.Collection(segments, item => Assert.IsType<ContainsSegment>(item));
    }

    [Fact]
    public void OptimizeSegmentStartsWithAndContains()
    {
        var segments = GetSubSegments("file*test*");
        Assert.Collection(segments,
            item => Assert.IsType<LiteralSegment>(item),
            item => Assert.IsType<ContainsSegment>(item));
    }

    [Fact]
    public void OptimizeSegmentLiteral()
    {
        var segments = GetSegments("test");
        Assert.Collection(segments, item => Assert.IsType<LiteralSegment>(item));
    }

    [Fact]
    public void OptimizeCombineTwoConsecutiveRecursiveMatchAll()
    {
        var segments = GetSegments("src/**/**/a/b");
        Assert.Collection(segments,
            item => Assert.IsType<LiteralSegment>(item),
            item => Assert.IsType<PathSuffixSegment>(item));
    }

    [Fact]
    public void OptimizeMatchLast()
    {
        var segments = GetSegments("a/**/b");
        Assert.Collection(segments,
            item => Assert.IsType<LiteralSegment>(item),
            item => Assert.IsType<LastSegment>(item));
    }

    [Fact]
    public void OptimizeMatchPathSuffix()
    {
        var segments = GetSegments("a/**/b/c");
        Assert.Collection(segments,
            item => Assert.IsType<LiteralSegment>(item),
            item => Assert.IsType<PathSuffixSegment>(item));
    }

    [Fact]
    public void OptimizeMatchNonEmpty()
    {
        var segments = GetSegments("a/**/*");
        Assert.Collection(segments,
            item => Assert.IsType<LiteralSegment>(item),
            item => Assert.IsType<MatchNonEmptyTextSegment>(item));
    }

    [Fact]
    public void OptimizeStarConsumeUntil()
    {
        var segments = GetSubSegments("*[abc][b-c]");
        Assert.Collection(segments,
            item => Assert.IsType<ConsumeSegmentUntilSegment>(item),
            item => Assert.IsType<MatchAllSubSegment>(item),
            item => Assert.IsType<CharacterSetSegment>(item),
            item => Assert.IsType<CharacterRangeSegment>(item));
    }

    [Fact]
    public void OptimizeSingleCharSet()
    {
        var segments = GetSubSegments("*[a-b]def[a][b][c]abc[a][a-b]");

        Assert.Collection(segments,
            item => Assert.IsType<ConsumeSegmentUntilSegment>(item),
            item => Assert.IsType<MatchAllSubSegment>(item),
            item => Assert.IsType<CharacterRangeSegment>(item),
            item => Assert.IsType<LiteralSegment>(item),
            item => Assert.IsType<CharacterRangeSegment>(item));
    }

    [Theory]
    [InlineData("a/**/b")]
    [InlineData("a/**/*.txt")]
    [InlineData("a/**/*")]
    [InlineData("**/b/c")]
    [InlineData("a*")]
    [InlineData("*.txt")]
    [InlineData("*file*")]
    [InlineData("a/b")]
    [InlineData("a/**/b/c")]
    [InlineData("{a,b}.cs")]
    [InlineData("[a-z].cs")]
    [InlineData("p?th/a")]
    public void ToStringRoundTripsToAnEquivalentPattern(string pattern)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        var text = glob.ToString();

        Assert.DoesNotContain("Meziantou.Framework.Globbing", text);

        // The text must parse back into a glob that matches exactly the same paths.
        var roundTripped = Glob.Parse(text, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.Equal(text, roundTripped.ToString());
    }

    [Fact]
    public void OptimizeSingleCharSet2()
    {
        var segments = GetSubSegments("*[!a]def[a][b][c]abc[a][a-b]");

        Assert.Collection(segments,
            item => Assert.IsType<MatchAllSubSegment>(item),
            item => Assert.IsType<CharacterSetInverseSegment>(item),
            item => Assert.IsType<LiteralSegment>(item),
            item => Assert.IsType<CharacterRangeSegment>(item));
    }
}
