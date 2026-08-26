namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// What each flavor does with the constructs the others have. Shape is asserted as well as round-tripping, because
/// skipped text round-trips perfectly while being structurally wrong.
/// </summary>
public sealed class RegexFlavorTests
{
    [Theory]
    [InlineData("net", "net")]
    [InlineData("dotnet", "net")]
    [InlineData(".NET", "net")]
    [InlineData("js", "javascript")]
    [InlineData("ECMAScript", "javascript")]
    [InlineData("pcre", "pcre")]
    [InlineData("perl", "pcre")]
    [InlineData("ere", "ere")]
    [InlineData("posix", "ere")]
    [InlineData("bre", "bre")]
    [InlineData("grep", "bre")]
    public void TryParse_ResolvesNamesAndAliases(string name, string expected)
    {
        Assert.True(RegexFlavor.TryParse(name, out var flavor));
        Assert.Equal(expected, flavor.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData(null)]
    public void TryParse_RejectsUnknownNames(string? name)
    {
        Assert.False(RegexFlavor.TryParse(name, out var flavor));
        Assert.Null(flavor);
    }

    /// <summary>
    /// A basic expression's escaped parentheses are read as escapes, not as grouping. That is a known limitation, and
    /// pinning it down here means a later change that does model the grouping has to say so.
    /// </summary>
    [Fact]
    public void PosixBasicReadsEscapedParenthesesAsEscapes()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"\(ab\)c", RegexFlavor.PosixBasic);

        Assert.Empty(tree.Diagnostics);
        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexGroupSyntax>());
        Assert.HasCount(2, tree.Root.DescendantNodes().OfType<RegexCharacterEscapeSyntax>().ToArray());
    }

    [Fact]
    public void PosixBasicTreatsBareParenthesesAsLiterals()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(ab)c", RegexFlavor.PosixBasic);

        Assert.Empty(tree.Diagnostics);
        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexGroupSyntax>());
        Assert.Equal(5, tree.Root.Alternation.Branches[0].Terms.Count);
    }

    [Fact]
    public void PosixBasicHasNoAlternation()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("a|b", RegexFlavor.PosixBasic);

        Assert.Single(tree.Root.Alternation.Branches);
        Assert.Empty(tree.Root.Alternation.BarTokens);
    }

    [Fact]
    public void PosixExtendedHasAlternationAndGroups()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(a|b)+", RegexFlavor.PosixExtended);

        Assert.Empty(tree.Diagnostics);
        var group = Assert.Single(tree.Root.DescendantNodes().OfType<RegexCapturingGroupSyntax>());
        Assert.Equal(2, group.Alternation.Branches.Count);
    }

    [Theory]
    [InlineData("[[:alpha:]]", true)]
    [InlineData("[[:^digit:]]", true)]
    public void PosixBracketExpressionsAreRecognizedWhereTheFlavorHasThem(string pattern, bool recognized)
    {
        var posix = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.PosixExtended);
        Assert.Equal(recognized, posix.Root.DescendantNodes().OfType<RegexPosixCharacterClassSyntax>().Any());

        // .NET has no bracket expressions, so the same text is an ordinary class of the characters it contains.
        var net = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.Net);
        Assert.Empty(net.Root.DescendantNodes().OfType<RegexPosixCharacterClassSyntax>());
    }

    [Fact]
    public void PosixBracketExpressionsReportTheirNameAndNegation()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("[[:^alpha:]]", RegexFlavor.PosixExtended);

        var bracket = Assert.Single(tree.Root.DescendantNodes().OfType<RegexPosixCharacterClassSyntax>());
        Assert.Equal("alpha", bracket.Name);
        Assert.True(bracket.IsNegated);
    }

    [Fact]
    public void CharacterClassSubtractionIsRecognizedOnlyByNet()
    {
        var net = RegexSyntaxAssert.TextIsFaithful("[a-z-[aeiou]]", RegexFlavor.Net);
        Assert.Single(net.Root.DescendantNodes().OfType<RegexClassSubtractionSyntax>());

        // PCRE reads the same text as the class "[a-z-[aeiou]" followed by the literal "]".
        var pcre = RegexSyntaxAssert.TextIsFaithful("[a-z-[aeiou]]", RegexFlavor.PcrePerl);
        Assert.Empty(pcre.Root.DescendantNodes().OfType<RegexClassSubtractionSyntax>());
    }

    [Fact]
    public void PossessiveQuantifiersAreRecognizedOnlyByPcre()
    {
        var pcre = RegexSyntaxAssert.TextIsFaithful("a*+", RegexFlavor.PcrePerl);
        var quantified = Assert.IsType<RegexQuantifiedSyntax>(pcre.Root.Alternation.Branches[0].Terms[0]);
        Assert.Equal(RegexQuantifierMode.Possessive, quantified.Mode);
        Assert.Empty(pcre.Diagnostics);

        // .NET has no possessive quantifiers, so the same text is a quantifier applied to a quantifier.
        var net = RegexSyntaxTree.ParseText("a*+", RegexFlavor.Net);
        Assert.Single(net.Diagnostics, diagnostic => diagnostic.Id == "REGEX0006");
    }

    [Fact]
    public void JavaScriptHasNoStartOfInputAnchor()
    {
        var net = RegexSyntaxAssert.TextIsFaithful(@"\A", RegexFlavor.Net);
        Assert.Single(net.Root.DescendantNodes().OfType<RegexAnchorSyntax>());

        var javaScript = RegexSyntaxAssert.TextIsFaithful(@"\A", RegexFlavor.JavaScript);
        Assert.Empty(javaScript.Root.DescendantNodes().OfType<RegexAnchorSyntax>());
    }

    [Fact]
    public void ParseJavaScriptLiteral_ReadsTheDelimitersAndFlags()
    {
        const string Literal = "/a+b/giu";
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(Literal);
        RegexSyntaxAssert.TextIsFaithful(Literal, tree);

        Assert.True(tree.Root.IsJavaScriptLiteral);
        Assert.Equal("/", tree.Root.OpenSlashToken?.Text);
        Assert.Equal("/", tree.Root.CloseSlashToken?.Text);
        Assert.Equal("giu", tree.Root.FlagsToken?.Text);
        Assert.Equal(RegexPatternOptions.Global | RegexPatternOptions.IgnoreCase | RegexPatternOptions.Unicode, tree.PatternOptions);
        Assert.Equal(2, tree.Root.Alternation.Branches[0].Terms.Count);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void ParseJavaScriptLiteral_IgnoresASlashInsideACharacterClass()
    {
        const string Literal = "/[/]/g";
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(Literal);
        RegexSyntaxAssert.TextIsFaithful(Literal, tree);

        Assert.Equal("g", tree.Root.FlagsToken?.Text);
        Assert.Single(tree.Root.DescendantNodes().OfType<RegexCharacterClassSyntax>());
    }

    [Fact]
    public void ParseJavaScriptLiteral_ReportsAnUnknownFlag()
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral("/a/gq");
        RegexSyntaxAssert.TextIsFaithful("/a/gq", tree);

        Assert.Single(tree.Diagnostics, diagnostic => diagnostic.Id == "REGEX0205");
    }

    [Fact]
    public void ParseJavaScriptLiteral_KeepsContentThatFollowsTheLiteral()
    {
        const string Literal = "/a/g;";
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(Literal);
        RegexSyntaxAssert.TextIsFaithful(Literal, tree);

        Assert.Equal(";", tree.Root.TrailingToken?.Text);
        Assert.Single(tree.Diagnostics, diagnostic => diagnostic.Id == "REGEX0204");
    }

    [Fact]
    public void ParseJavaScriptLiteral_AcceptsABarePattern()
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral("a+");
        RegexSyntaxAssert.TextIsFaithful("a+", tree);

        Assert.False(tree.Root.IsJavaScriptLiteral);
        Assert.Empty(tree.Diagnostics);
    }
}
