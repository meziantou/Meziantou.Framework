namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// The JavaScript <c>v</c> flag's class set grammar: nested classes, intersection, difference, and string
/// disjunctions.
/// </summary>
public sealed class RegexClassSetTests
{
    private static RegexParseOptions SetMode => new(RegexFlavor.JavaScript)
    {
        PatternOptions = RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets,
    };

    private static RegexSyntaxTree Parse(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, SetMode);
        Assert.Empty(tree.Diagnostics, $"[{pattern}] reported {string.Join(",", tree.Diagnostics.Select(d => d.Id))}");

        return tree;
    }

    [Theory]
    [InlineData("[[a][b]]", 3)]
    [InlineData("[[[a]]]", 3)]
    [InlineData("[[a-z][0-9]]", 3)]
    public void AClassMayContainAnother(string pattern, int classes)
    {
        Assert.HasCount(classes, Parse(pattern).Root.DescendantNodes().OfType<RegexCharacterClassSyntax>().ToArray());
    }

    [Theory]
    [InlineData(@"[\w&&\p{L}]", "&&", 2)]
    [InlineData("[a&&b&&c]", "&&", 3)]
    [InlineData("[[a-z]--[aeiou]]", "--", 2)]
    [InlineData(@"[\w--\d]", "--", 2)]
    [InlineData("[a--b]", "--", 2)]
    [InlineData("[a--b--c]", "--", 3)]
    [InlineData(@"[\w--a-z]", "--", 2)]
    [InlineData(@"[^\w--\d]", "--", 2)]
    public void AnOperationKeepsItsOperatorAndOperands(string pattern, string op, int operands)
    {
        var operation = Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexClassSetOperationSyntax>());

        Assert.Equal(op, operation.OperatorText);
        Assert.HasCount(operands, operation.Operands);
        Assert.Equal(op == "&&", operation.IsIntersection);
    }

    [Theory]
    [InlineData(@"[\q{abc|def}]", new[] { "abc", "def" })]
    [InlineData(@"[\q{a}]", new[] { "a" })]
    [InlineData(@"[\q{a|b|c}]", new[] { "a", "b", "c" })]
    public void AStringDisjunctionListsItsAlternatives(string pattern, string[] alternatives)
    {
        var literal = Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexClassStringLiteralSyntax>());

        Assert.Equal(alternatives, literal.Alternatives);
    }

    [Fact]
    public void AStringDisjunctionMayBeAnOperand()
    {
        var tree = Parse(@"[\q{ab}--\q{b}]");

        var operation = Assert.Single(tree.Root.DescendantNodes().OfType<RegexClassSetOperationSyntax>());
        Assert.HasCount(2, operation.Operands);
        Assert.HasCount(2, tree.Root.DescendantNodes().OfType<RegexClassStringLiteralSyntax>().ToArray());
    }

    /// <summary>
    /// An operand is a single thing and the operators may not be mixed, which is what makes <c>[abc--d]</c> and
    /// <c>[a&amp;&amp;b--c]</c> errors rather than groupings the reader has to guess at.
    /// </summary>
    [Theory]
    [InlineData("[abc--d]")]
    [InlineData("[a&&b--c]")]
    [InlineData("[[a][b]--[c]]")]
    [InlineData("[a&&]")]
    public void AMalformedOperationIsReported(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, SetMode);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0070");
    }

    /// <summary>A single dash is still a range, so the doubled form is the only operator.</summary>
    [Theory]
    [InlineData("[a-z]")]
    [InlineData("[a-b]")]
    public void ASingleDashIsStillARange(string pattern)
    {
        var tree = Parse(pattern);

        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexClassSetOperationSyntax>());
        Assert.Single(tree.Root.DescendantNodes().OfType<RegexCharacterRangeSyntax>());
    }

    /// <summary>Without the flag the grammar is off, so the same text is an ordinary class.</summary>
    [Fact]
    public void TheSetGrammarNeedsTheFlag()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("[a--b]", RegexFlavor.JavaScript);

        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexClassSetOperationSyntax>());
    }

    [Fact]
    public void NetDoesNotHaveTheSetGrammar()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"[\w&&\p{L}]", RegexFlavor.Net);

        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexClassSetOperationSyntax>());
    }
}
