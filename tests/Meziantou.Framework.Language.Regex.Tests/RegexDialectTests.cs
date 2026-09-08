namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// What each dialect does with the constructs the others have. Shape is asserted as well as round-tripping, because
/// skipped text round-trips perfectly while being structurally wrong.
/// </summary>
public sealed class RegexDialectTests
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
        Assert.True(RegexDialect.TryParse(name, out var dialect));
        Assert.Equal(expected, dialect.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData(null)]
    public void TryParse_RejectsUnknownNames(string? name)
    {
        Assert.False(RegexDialect.TryParse(name, out var dialect));
        Assert.Null(dialect);
    }

    /// <summary>A basic expression spells its delimiters escaped, so <c>\(…\)</c> is the group.</summary>
    [Fact]
    public void PosixBasicReadsEscapedParenthesesAsAGroup()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"\(ab\)c", RegexDialect.PosixBasic);

        Assert.Empty(tree.Diagnostics);
        var group = Assert.Single(tree.Root.DescendantNodes().OfType<RegexCapturingGroupSyntax>());
        Assert.Equal(@"\(ab\)", group.ToFullString());
        Assert.Equal(1, group.Number);
    }

    [Fact]
    public void PosixBasicReadsBareParenthesesAsLiterals()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(ab)c", RegexDialect.PosixBasic);

        Assert.Empty(tree.Diagnostics);
        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexGroupSyntax>());
    }

    [Theory]
    [InlineData(@"a\{2,3\}", 2, 3)]
    [InlineData(@"a\{2\}", 2, 2)]
    [InlineData(@"a\{2,\}", 2, null)]
    public void PosixBasicReadsEscapedBracesAsABound(string pattern, int min, int? max)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.PosixBasic);

        Assert.Empty(tree.Diagnostics);
        var quantified = Assert.IsType<RegexQuantifiedSyntax>(tree.Root.Alternation.Branches[0].Terms[0]);
        Assert.Equal(min, quantified.Quantifier.MinCount);
        Assert.Equal(max, quantified.Quantifier.MaxCount);
    }

    [Fact]
    public void PosixBasicSupportsTheGnuAlternationAndQuantifiers()
    {
        var alternation = RegexSyntaxAssert.TextIsFaithful(@"a\|b", RegexDialect.PosixBasic);
        Assert.Empty(alternation.Diagnostics);
        Assert.Equal(2, alternation.Root.Alternation.Branches.Count);

        foreach (var pattern in new[] { @"a\+", @"a\?" })
        {
            var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.PosixBasic);
            Assert.Empty(tree.Diagnostics);
            Assert.IsType<RegexQuantifiedSyntax>(tree.Root.Alternation.Branches[0].Terms[0]);
        }
    }

    /// <summary>
    /// In a basic expression these are special only where they can be: <c>^</c> where a branch starts, <c>$</c> where
    /// one ends, and <c>*</c> only after something to repeat.
    /// </summary>
    [Theory]
    [InlineData("^ab$", 2, 4)]
    [InlineData("a^b", 0, 3)]
    [InlineData("a$b", 0, 3)]
    [InlineData("*ab", 0, 3)]
    public void PosixBasicTreatsSpecialCharactersPositionally(string pattern, int anchors, int terms)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.PosixBasic);

        Assert.Empty(tree.Diagnostics);
        Assert.HasCount(anchors, tree.Root.DescendantNodes().OfType<RegexAnchorSyntax>().ToArray());
        Assert.HasCount(terms, tree.Root.Alternation.Branches[0].Terms);
    }

    [Fact]
    public void PosixBasicSupportsBackreferences()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"\(a\)\1", RegexDialect.PosixBasic);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(1, Assert.Single(tree.Root.DescendantNodes().OfType<RegexBackreferenceSyntax>()).Number);
    }

    [Fact]
    public void PosixBasicTreatsBareParenthesesAsLiterals()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(ab)c", RegexDialect.PosixBasic);

        Assert.Empty(tree.Diagnostics);
        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexGroupSyntax>());
        Assert.Equal(5, tree.Root.Alternation.Branches[0].Terms.Count);
    }

    [Fact]
    public void PosixBasicHasNoAlternation()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("a|b", RegexDialect.PosixBasic);

        Assert.Single(tree.Root.Alternation.Branches);
        Assert.Empty(tree.Root.Alternation.BarTokens);
    }

    [Fact]
    public void PosixExtendedHasAlternationAndGroups()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(a|b)+", RegexDialect.PosixExtended);

        Assert.Empty(tree.Diagnostics);
        var group = Assert.Single(tree.Root.DescendantNodes().OfType<RegexCapturingGroupSyntax>());
        Assert.Equal(2, group.Alternation.Branches.Count);
    }

    [Theory]
    [InlineData("[[:alpha:]]", true)]
    [InlineData("[[:^digit:]]", true)]
    public void PosixBracketExpressionsAreRecognizedWhereTheDialectHasThem(string pattern, bool recognized)
    {
        var posix = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.PosixExtended);
        Assert.Equal(recognized, posix.Root.DescendantNodes().OfType<RegexPosixCharacterClassSyntax>().Any());

        // .NET has no bracket expressions, so the same text is an ordinary class of the characters it contains.
        var net = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.Net);
        Assert.Empty(net.Root.DescendantNodes().OfType<RegexPosixCharacterClassSyntax>());
    }

    [Fact]
    public void PosixBracketExpressionsReportTheirNameAndNegation()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("[[:^alpha:]]", RegexDialect.PosixExtended);

        var bracket = Assert.Single(tree.Root.DescendantNodes().OfType<RegexPosixCharacterClassSyntax>());
        Assert.Equal("alpha", bracket.Name);
        Assert.True(bracket.IsNegated);
    }

    [Fact]
    public void CharacterClassSubtractionIsRecognizedOnlyByNet()
    {
        var net = RegexSyntaxAssert.TextIsFaithful("[a-z-[aeiou]]", RegexDialect.Net);
        Assert.Single(net.Root.DescendantNodes().OfType<RegexClassSubtractionSyntax>());

        // PCRE reads the same text as the class "[a-z-[aeiou]" followed by the literal "]".
        var pcre = RegexSyntaxAssert.TextIsFaithful("[a-z-[aeiou]]", RegexDialect.PcrePerl);
        Assert.Empty(pcre.Root.DescendantNodes().OfType<RegexClassSubtractionSyntax>());
    }

    [Fact]
    public void PossessiveQuantifiersAreRecognizedOnlyByPcre()
    {
        var pcre = RegexSyntaxAssert.TextIsFaithful("a*+", RegexDialect.PcrePerl);
        var quantified = Assert.IsType<RegexQuantifiedSyntax>(pcre.Root.Alternation.Branches[0].Terms[0]);
        Assert.Equal(RegexQuantifierMode.Possessive, quantified.Mode);
        Assert.Empty(pcre.Diagnostics);

        // .NET has no possessive quantifiers, so the same text is a quantifier applied to a quantifier.
        var net = RegexSyntaxTree.ParseText("a*+", RegexDialect.Net);
        Assert.Single(net.Diagnostics, diagnostic => diagnostic.Id == "REGEX0006");
    }

    [Fact]
    public void JavaScriptHasNoStartOfInputAnchor()
    {
        var net = RegexSyntaxAssert.TextIsFaithful(@"\A", RegexDialect.Net);
        Assert.Single(net.Root.DescendantNodes().OfType<RegexAnchorSyntax>());

        var javaScript = RegexSyntaxAssert.TextIsFaithful(@"\A", RegexDialect.JavaScript);
        Assert.Empty(javaScript.Root.DescendantNodes().OfType<RegexAnchorSyntax>());
    }

    /// <summary>
    /// <c>[]</c> matches nothing and <c>[^]</c> matches anything. Only ECMAScript has them: .NET reads the <c>]</c> as
    /// a member and then runs out of pattern looking for the real one.
    /// </summary>
    [Theory]
    [InlineData("[]", false)]
    [InlineData("[^]", true)]
    [InlineData("[]a", false)]
    [InlineData("a[]b", false)]
    public void JavaScriptAllowsAnEmptyCharacterClass(string pattern, bool negated)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.JavaScript);

        Assert.Empty(tree.Diagnostics);
        var characterClass = Assert.Single(tree.Root.DescendantNodes().OfType<RegexCharacterClassSyntax>());
        Assert.Equal(negated, characterClass.IsNegated);
        Assert.Empty(characterClass.Members);
    }

    [Fact]
    public void NetStillRejectsAnEmptyCharacterClass()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("[]", RegexDialect.Net);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0003");
    }

    /// <summary>
    /// In Unicode mode <c>\u{10FFFF}</c> names a code point directly, so the braces belong to the escape rather than
    /// being a bound applied to the letter.
    /// </summary>
    [Theory]
    [InlineData(@"\u{41}", "A")]
    [InlineData(@"\u{1F600}", "\U0001F600")]
    [InlineData(@"\u{10FFFF}", "\U0010FFFF")]
    public void ACodePointEscapeNamesItsCharacter(string pattern, string value)
    {
        var options = new RegexParseOptions(RegexDialect.JavaScript) { PatternOptions = RegexPatternOptions.Unicode };
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        Assert.Empty(tree.Diagnostics);
        var escape = Assert.Single(tree.Root.DescendantNodes().OfType<RegexCharacterEscapeSyntax>());
        Assert.Equal(value, escape.Value);
    }

    [Theory]
    [InlineData(@"\u{}")]
    [InlineData(@"\u{41")]
    [InlineData(@"\u{110000}")]
    [InlineData(@"\u{ZZ}")]
    public void AMalformedCodePointEscapeIsReported(string pattern)
    {
        var options = new RegexParseOptions(RegexDialect.JavaScript) { PatternOptions = RegexPatternOptions.Unicode };
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0012");
    }

    /// <summary>Without Unicode mode the braces are a bound, not part of the escape, which is what the engines do.</summary>
    [Fact]
    public void ACodePointEscapeIsNotOneOutsideUnicodeMode()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"\u{41}", RegexDialect.JavaScript);

        Assert.DoesNotContain(tree.Root.DescendantNodes().OfType<RegexCharacterEscapeSyntax>(), e => e.Value == "A");
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
