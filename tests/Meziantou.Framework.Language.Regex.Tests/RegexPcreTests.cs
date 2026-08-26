namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>The PCRE constructs the other flavors do not have.</summary>
public sealed class RegexPcreTests
{
    private static RegexSyntaxTree Parse(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.PcrePerl);
        Assert.Empty(tree.Diagnostics, $"[{pattern}] reported {string.Join(",", tree.Diagnostics.Select(d => d.Id))}");

        return tree;
    }

    [Theory]
    [InlineData("(?R)")]
    [InlineData("(a)(?1)")]
    [InlineData("(?<n>a)(?&n)")]
    [InlineData("(?<n>a)(?P>n)")]
    [InlineData(@"(a)\g<1>")]
    [InlineData(@"(?<n>a)\g<n>")]
    [InlineData(@"(?<n>a)\g'n'")]
    public void EverySubroutineSpellingIsARecursionNode(string pattern)
    {
        Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexRecursionSyntax>());
    }

    [Theory]
    [InlineData(@"(a)\g{1}", "1")]
    [InlineData(@"(a)\g1", "1")]
    [InlineData(@"(a)\g{-1}", "-1")]
    [InlineData(@"(?<n>a)\g{n}", "n")]
    [InlineData(@"(?<n>a)(?P=n)", "n")]
    public void EveryReferenceSpellingIsANamedBackreferenceNode(string pattern, string name)
    {
        var reference = Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexNamedBackreferenceSyntax>());

        Assert.Equal(name, reference.Name);
    }

    [Theory]
    [InlineData("(?C)", "")]
    [InlineData("(?C1)", "1")]
    [InlineData("(?C255)", "255")]
    [InlineData(@"(?C""text"")", @"""text""")]
    public void ACalloutKeepsItsBody(string pattern, string value)
    {
        var callout = Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexCalloutSyntax>());

        Assert.Equal(value, callout.Value);
    }

    [Theory]
    [InlineData(@"\h")]
    [InlineData(@"\H")]
    [InlineData(@"\v")]
    [InlineData(@"\V")]
    [InlineData(@"\R")]
    [InlineData(@"\X")]
    [InlineData(@"\N")]
    [InlineData(@"[\h\v]")]
    public void TheExtraShorthandClassesAreClassEscapes(string pattern)
    {
        Assert.NotEmpty(Parse(pattern).Root.DescendantNodes().OfType<RegexCharacterClassEscapeSyntax>());
    }

    /// <summary><c>\R</c> and <c>\X</c> are capitals without a lower-case counterpart, so they negate nothing.</summary>
    [Theory]
    [InlineData(@"\H", true)]
    [InlineData(@"\V", true)]
    [InlineData(@"\h", false)]
    [InlineData(@"\R", false)]
    [InlineData(@"\X", false)]
    public void OnlyTheLettersThatComeInPairsAreNegations(string pattern, bool negated)
    {
        var escape = Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexCharacterClassEscapeSyntax>());

        Assert.Equal(negated, escape.IsNegated);
    }

    [Theory]
    [InlineData(@"\o{101}", "A")]
    [InlineData(@"\N{U+0041}", "A")]
    [InlineData(@"\N{U+1F600}", "\U0001F600")]
    public void ABracedNumericEscapeNamesItsCharacter(string pattern, string value)
    {
        var escape = Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexCharacterEscapeSyntax>());

        Assert.Equal(value, escape.Value);
    }

    [Theory]
    [InlineData(@"\o{99}")]
    [InlineData(@"\o{}")]
    [InlineData(@"\N{X+41}")]
    [InlineData(@"\N{U+110000}")]
    [InlineData(@"\N{U+D800}")]
    public void AMalformedBracedNumericEscapeIsReported(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.PcrePerl);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0012");
    }

    /// <summary><c>\N</c> alone is "any character except a newline"; <c>\N{…}</c> names a code point.</summary>
    [Fact]
    public void TheBraceDecidesWhichEscapeBackslashNIs()
    {
        Assert.NotEmpty(Parse(@"\Na").Root.DescendantNodes().OfType<RegexCharacterClassEscapeSyntax>());
        Assert.NotEmpty(Parse(@"\N{U+0041}").Root.DescendantNodes().OfType<RegexCharacterEscapeSyntax>());
    }

    [Theory]
    [InlineData(@"\p{^L}", "L", true)]
    [InlineData(@"\P{L}", "L", true)]
    [InlineData(@"\p{L}", "L", false)]
    public void APropertyMayBeNegatedEitherWay(string pattern, string name, bool negated)
    {
        var category = Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexUnicodeCategorySyntax>());

        Assert.Equal(name, category.Name);
        Assert.Equal(negated, category.IsNegated);
    }

    [Theory]
    [InlineData("(?J)a")]
    [InlineData("(?U)a")]
    [InlineData("(?J:a)")]
    public void ThePcreOptionLettersAreAccepted(string pattern) => Parse(pattern);

    [Theory]
    [InlineData("(*SKIP)a", "SKIP")]
    [InlineData("(*MARK:name)a", "MARK:name")]
    [InlineData("(*PRUNE)a", "PRUNE")]
    public void ABacktrackingVerbKeepsItsName(string pattern, string name)
    {
        var verb = Assert.Single(Parse(pattern).Root.DescendantNodes().OfType<RegexBacktrackingVerbSyntax>());

        Assert.Equal(name, verb.Name);
    }

    [Theory]
    [InlineData(@"(a)\g{9}", "REGEX0030")]
    [InlineData(@"(?<n>a)\g{other}", "REGEX0031")]
    [InlineData(@"\g", "REGEX0019")]
    public void AReferenceToNothingIsReported(string pattern, string id)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.PcrePerl);

        Assert.Contains(tree.Diagnostics, d => d.Id == id);
    }

    /// <summary>None of this belongs to .NET, so the .NET flavor must not quietly accept it.</summary>
    [Theory]
    [InlineData("(?&n)")]
    [InlineData("(?C1)")]
    [InlineData(@"\o{101}")]
    public void NetDoesNotAcceptThePcreConstructs(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.Net);

        Assert.NotEmpty(tree.Diagnostics);
    }
}
