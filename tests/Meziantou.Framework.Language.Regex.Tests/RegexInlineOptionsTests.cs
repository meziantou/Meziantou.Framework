namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// Extended mode decides whether a space is trivia or a literal, and inline options can switch it part-way through a
/// pattern, so the question is answered per position rather than per tree. Each test here is one of those rules.
/// </summary>
public sealed class RegexInlineOptionsTests
{
    private static RegexSyntaxTree ParseExtended(string pattern) =>
        RegexSyntaxAssert.TextIsFaithful(pattern, new RegexParseOptions(RegexFlavor.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace });

    [Fact]
    public void WhitespaceIsLiteralOutsideExtendedMode()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("a b", RegexFlavor.Net);

        Assert.Equal(3, tree.Root.Alternation.Branches[0].Terms.Count);
        Assert.Empty(tree.Root.DescendantTrivia());
    }

    [Fact]
    public void WhitespaceIsTriviaInExtendedMode()
    {
        var tree = ParseExtended("a b");

        Assert.Equal(2, tree.Root.Alternation.Branches[0].Terms.Count);
        Assert.Single(tree.Root.DescendantTrivia(), trivia => trivia.Kind == RegexSyntaxKind.WhitespaceTrivia);
    }

    [Fact]
    public void InlineOptionsTurnExtendedModeOnPartWayThrough()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("a b(?x)c d", RegexFlavor.Net);

        // "a b" is three terms; after "(?x)" the space between "c" and "d" is trivia, so those are two.
        var terms = tree.Root.Alternation.Branches[0].Terms;
        Assert.Equal(6, terms.Count);
        Assert.Single(tree.Root.DescendantTrivia(), trivia => trivia.Kind == RegexSyntaxKind.WhitespaceTrivia);
    }

    [Fact]
    public void ACommentEndsAtALineFeedAndNotAtACarriageReturn()
    {
        var tree = ParseExtended("a#one\rtwo\nb");

        var comment = Assert.Single(tree.Root.DescendantTrivia(), trivia => trivia.Kind == RegexSyntaxKind.PatternCommentTrivia);
        Assert.Equal("#one\rtwo", comment.Text);
    }

    [Fact]
    public void ACommentGroupIsTriviaEvenOutsideExtendedMode()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("a(?#note)b", RegexFlavor.Net);

        var comment = Assert.Single(tree.Root.DescendantTrivia(), trivia => trivia.Kind == RegexSyntaxKind.InlineCommentTrivia);
        Assert.Equal("(?#note)", comment.Text);
        Assert.Equal(2, tree.Root.Alternation.Branches[0].Terms.Count);
    }

    [Fact]
    public void WhitespaceInsideACharacterClassStaysLiteralInExtendedMode()
    {
        var tree = ParseExtended("[ a # b ]");

        var characterClass = Assert.Single(tree.Root.DescendantNodes().OfType<RegexCharacterClassSyntax>());
        Assert.Equal("[ a # b ]", characterClass.ToFullString());
        Assert.Empty(tree.Root.DescendantTrivia());
    }

    [Fact]
    public void ALazyModifierIsSeparatedFromItsQuantifierByTrivia()
    {
        var tree = ParseExtended("a{2,3} ?");

        var quantified = Assert.IsType<RegexQuantifiedSyntax>(tree.Root.Alternation.Branches[0].Terms[0]);
        Assert.Equal(RegexQuantifierMode.Lazy, quantified.Mode);
    }

    [Fact]
    public void OptionsAreRestoredByTheEnclosingCloseParenthesis()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(?:(?i)a)b", RegexFlavor.Net);

        var literals = tree.Root.DescendantNodes().OfType<RegexLiteralSyntax>().ToArray();
        Assert.Equal(RegexPatternOptions.IgnoreCase, literals[0].Options);
        Assert.Equal(RegexPatternOptions.None, literals[1].Options);
    }

    /// <summary>
    /// The .NET engine does not restore options at an alternation bar, so <c>(?:a(?i)b|c)</c> matches an upper-case
    /// <c>C</c>. That is a real difference from some other engines and it deserves to be pinned down.
    /// </summary>
    [Fact]
    public void OptionsAreNotRestoredByAnAlternationBar()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(?:a(?i)b|c)", RegexFlavor.Net);

        var literals = tree.Root.DescendantNodes().OfType<RegexLiteralSyntax>().ToArray();
        Assert.Equal(RegexPatternOptions.None, literals[0].Options);
        Assert.Equal(RegexPatternOptions.IgnoreCase, literals[1].Options);
        Assert.Equal(RegexPatternOptions.IgnoreCase, literals[2].Options);
    }

    [Fact]
    public void AnOptionsGroupScopesItsOptionsToItsBody()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(?i:a)b", RegexFlavor.Net);

        var group = Assert.Single(tree.Root.DescendantNodes().OfType<RegexOptionsGroupSyntax>());
        Assert.Equal("i", group.OptionsText);
        Assert.Equal(RegexPatternOptions.IgnoreCase, group.InnerOptions);

        var literals = tree.Root.DescendantNodes().OfType<RegexLiteralSyntax>().ToArray();
        Assert.Equal(RegexPatternOptions.IgnoreCase, literals[0].Options);
        Assert.Equal(RegexPatternOptions.None, literals[1].Options);
    }

    [Fact]
    public void ExplicitCaptureStopsUnnamedGroupsFromCapturing()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(?n)(a)(?<x>b)", RegexFlavor.Net);

        var capture = Assert.Single(tree.Captures);
        Assert.Equal("x", capture.Name);
        Assert.Equal(1, capture.Number);
    }

    [Fact]
    public void AnInlineOptionSetterCannotBeQuantified()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(?i)*", RegexFlavor.Net);

        Assert.Single(tree.Diagnostics, diagnostic => diagnostic.Id == "REGEX0005");
    }
}
