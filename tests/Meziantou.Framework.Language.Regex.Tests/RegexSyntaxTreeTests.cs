namespace Meziantou.Framework.Language.Regex.Tests;

public sealed class RegexSyntaxTreeTests
{
    public static TheoryData<string> Patterns =>
    [
        "",
        "a",
        "abc",
        "a|b",
        "a|b|c",
        "|",
        "a*",
        "a+?",
        "a{2}",
        "a{2,}",
        "a{2,5}?",
        "a{2 , 3}",
        ".",
        "^abc$",
        @"\bword\b",
        @"\d+\.\d+",
        "[abc]",
        "[^a-z]",
        "[]]",
        "[a-]",
        "[-a]",
        @"[\]]",
        "(a)(b)",
        "(?:a)",
        "(?<name>a)",
        "(?'name'a)",
        "(?=a)",
        "(?!a)",
        "(?<=a)",
        "(?<!a)",
        "(?>a)",
        "(?i)a",
        "(?i:a)",
        "(?#comment)a",
        @"(a)\1",
        @"\1(a)",
        @"(?<n>a)\k<n>",
        @"\p{L}+",
        @"\P{IsGreek}",
        @"\x41A\cA\052",
        "(?(1)a|b)(x)",
        "(?(?=a)b|c)",
        "[a-z-[aeiou]]",
    ];

    [Theory]
    [MemberData(nameof(Patterns))]
    public void ParseText_RoundTripsExactly(string pattern) => RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.Net);

    [Theory]
    [MemberData(nameof(Patterns))]
    public void ParseText_ReportsNothingForValidPatterns(string pattern)
    {
        var tree = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net);

        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ParseText_ExposesTheDialectAndTheText()
    {
        var tree = RegexSyntaxTree.ParseText("a+", RegexDialect.Net);

        Assert.Equal("a+", tree.GetText().Text);
        Assert.Equal(RegexDialect.Net, tree.Dialect);
        Assert.Equal(SyntaxKind.Pattern, tree.GetRoot().Kind());
        Assert.Same(tree.GetRoot(), tree.GetRoot());
    }

    [Fact]
    public void ParseText_TreatsNullAsAnEmptyPattern()
    {
        var tree = RegexSyntaxTree.ParseText(null!, RegexDialect.Net);

        Assert.Equal("", tree.GetText().Text);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void Root_AlwaysHasOneAlternationOfSequences()
    {
        var tree = RegexSyntaxTree.ParseText("ab", RegexDialect.Net);

        var branch = Assert.Single(tree.GetRoot().Alternation.Branches);
        Assert.False(tree.GetRoot().Alternation.HasAlternatives);
        Assert.Equal(2, branch.Terms.Count);
    }

    [Fact]
    public void Alternation_KeepsOneBranchPerBar()
    {
        var tree = RegexSyntaxTree.ParseText("a|b|c", RegexDialect.Net);

        Assert.Equal(3, tree.GetRoot().Alternation.Branches.Count);
        Assert.Equal(2, tree.GetRoot().Alternation.Branches.SeparatorCount);
        Assert.True(tree.GetRoot().Alternation.HasAlternatives);
    }

    [Fact]
    public void Quantifier_ReportsItsBounds()
    {
        var tree = RegexSyntaxTree.ParseText("a{2,5}?", RegexDialect.Net);

        var quantified = Assert.IsType<RegexQuantifiedSyntax>(tree.GetRoot().Alternation.Branches[0].Terms[0]);
        var quantifier = Assert.IsType<RegexRangeQuantifierSyntax>(quantified.Quantifier);
        Assert.Equal(2, quantifier.MinCount);
        Assert.Equal(5, quantifier.MaxCount);
        Assert.Equal(RegexQuantifierMode.Lazy, quantified.Mode);
    }

    [Theory]
    [InlineData("a*", 0, null)]
    [InlineData("a+", 1, null)]
    [InlineData("a?", 0, 1)]
    [InlineData("a{3}", 3, 3)]
    [InlineData("a{3,}", 3, null)]
    public void Quantifier_ReportsSimpleBounds(string pattern, int min, int? max)
    {
        var tree = RegexSyntaxTree.ParseText(pattern, RegexDialect.Net);

        var quantified = Assert.IsType<RegexQuantifiedSyntax>(tree.GetRoot().Alternation.Branches[0].Terms[0]);
        Assert.Equal(min, quantified.Quantifier.MinCount);
        Assert.Equal(max, quantified.Quantifier.MaxCount);
    }

    [Fact]
    public void IsEquivalentTo_IgnoresExtendedModeWhitespace()
    {
        var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
        var spaced = RegexSyntaxTree.ParseText("a  b   # note\n", options);
        var tight = RegexSyntaxTree.ParseText("ab", options);

        Assert.True(spaced.IsEquivalentTo(tight));
    }

    [Fact]
    public void IsEquivalentTo_IsFalseAcrossDialects()
    {
        var net = RegexSyntaxTree.ParseText("a", RegexDialect.Net);
        var javaScript = RegexSyntaxTree.ParseText("a", RegexDialect.JavaScript);

        Assert.False(net.IsEquivalentTo(javaScript));
    }

    [Fact]
    public void GetChanges_TrimsTheCommonPrefixAndSuffix()
    {
        var before = RegexSyntaxTree.ParseText("ab+c", RegexDialect.Net);
        var after = before.WithChanges(new TextChange(new TextSpan(2, 1), "*"));

        var change = Assert.Single(after.GetChanges(before));
        Assert.Equal(new TextSpan(2, 1), change.Span);
        Assert.Equal("*", change.NewText);
        Assert.Equal("ab*c", after.GetText().Text);
    }

    [Fact]
    public void Captures_AreNumberedTheWayTheEngineNumbersThem()
    {
        var tree = RegexSyntaxTree.ParseText("(a)(?<x>b)(c)", RegexDialect.Net);

        Assert.Equal([1, 2, 3], tree.Captures.Select(capture => capture.Number));

        // Named groups take the first free numbers after every numbered one, so "x" is group 3 rather than group 2.
        Assert.Equal(["1", "2", "x"], tree.Captures.Select(capture => capture.Name));
    }
}
