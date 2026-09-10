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

        Assert.Empty(tree.GetDiagnostics());
        var group = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCapturingGroupSyntax>());
        Assert.Equal(@"\(ab\)", group.ToFullString());
        Assert.Equal(1, group.Number);
    }

    [Fact]
    public void PosixBasicReadsBareParenthesesAsLiterals()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(ab)c", RegexDialect.PosixBasic);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<RegexGroupSyntax>());
    }

    [Theory]
    [InlineData(@"a\{2,3\}", 2, 3)]
    [InlineData(@"a\{2\}", 2, 2)]
    [InlineData(@"a\{2,\}", 2, null)]
    public void PosixBasicReadsEscapedBracesAsABound(string pattern, int min, int? max)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.PosixBasic);

        Assert.Empty(tree.GetDiagnostics());
        var quantified = Assert.IsType<RegexQuantifiedSyntax>(tree.GetRoot().Alternation.Branches[0].Terms[0]);
        Assert.Equal(min, quantified.Quantifier.MinCount);
        Assert.Equal(max, quantified.Quantifier.MaxCount);
    }

    [Fact]
    public void PosixBasicSupportsTheGnuAlternationAndQuantifiers()
    {
        var alternation = RegexSyntaxAssert.TextIsFaithful(@"a\|b", RegexDialect.PosixBasic);
        Assert.Empty(alternation.GetDiagnostics());
        Assert.Equal(2, alternation.GetRoot().Alternation.Branches.Count);

        foreach (var pattern in new[] { @"a\+", @"a\?" })
        {
            var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.PosixBasic);
            Assert.Empty(tree.GetDiagnostics());
            Assert.IsType<RegexQuantifiedSyntax>(tree.GetRoot().Alternation.Branches[0].Terms[0]);
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

        Assert.Empty(tree.GetDiagnostics());
        Assert.HasCount(anchors, tree.GetRoot().DescendantNodes().OfType<RegexAnchorSyntax>().ToArray());
        Assert.HasCount(terms, tree.GetRoot().Alternation.Branches[0].Terms);
    }

    [Fact]
    public void PosixBasicSupportsBackreferences()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"\(a\)\1", RegexDialect.PosixBasic);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(1, Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexBackreferenceSyntax>()).Number);
    }

    [Fact]
    public void PosixBasicTreatsBareParenthesesAsLiterals()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(ab)c", RegexDialect.PosixBasic);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<RegexGroupSyntax>());
        Assert.Equal(5, tree.GetRoot().Alternation.Branches[0].Terms.Count);
    }

    [Fact]
    public void PosixBasicHasNoAlternation()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("a|b", RegexDialect.PosixBasic);

        Assert.Single(tree.GetRoot().Alternation.Branches);
        Assert.Empty(tree.GetRoot().Alternation.BarTokens);
    }

    [Fact]
    public void PosixExtendedHasAlternationAndGroups()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(a|b)+", RegexDialect.PosixExtended);

        Assert.Empty(tree.GetDiagnostics());
        var group = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCapturingGroupSyntax>());
        Assert.Equal(2, group.Alternation.Branches.Count);
    }

    [Theory]
    [InlineData("[[:alpha:]]", true)]
    [InlineData("[[:^digit:]]", true)]
    public void PosixBracketExpressionsAreRecognizedWhereTheDialectHasThem(string pattern, bool recognized)
    {
        var posix = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.PosixExtended);
        Assert.Equal(recognized, posix.GetRoot().DescendantNodes().OfType<RegexPosixCharacterClassSyntax>().Any());

        // .NET has no bracket expressions, so the same text is an ordinary class of the characters it contains.
        var net = RegexSyntaxAssert.TextIsFaithful(pattern, RegexDialect.Net);
        Assert.Empty(net.GetRoot().DescendantNodes().OfType<RegexPosixCharacterClassSyntax>());
    }

    [Fact]
    public void PosixBracketExpressionsReportTheirNameAndNegation()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("[[:^alpha:]]", RegexDialect.PosixExtended);

        var bracket = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexPosixCharacterClassSyntax>());
        Assert.Equal("alpha", bracket.Name);
        Assert.True(bracket.IsNegated);
    }

    [Fact]
    public void CharacterClassSubtractionIsRecognizedOnlyByNet()
    {
        var net = RegexSyntaxAssert.TextIsFaithful("[a-z-[aeiou]]", RegexDialect.Net);
        Assert.Single(net.GetRoot().DescendantNodes().OfType<RegexClassSubtractionSyntax>());

        // PCRE reads the same text as the class "[a-z-[aeiou]" followed by the literal "]".
        var pcre = RegexSyntaxAssert.TextIsFaithful("[a-z-[aeiou]]", RegexDialect.PcrePerl);
        Assert.Empty(pcre.GetRoot().DescendantNodes().OfType<RegexClassSubtractionSyntax>());
    }

    [Fact]
    public void PossessiveQuantifiersAreRecognizedOnlyByPcre()
    {
        var pcre = RegexSyntaxAssert.TextIsFaithful("a*+", RegexDialect.PcrePerl);
        var quantified = Assert.IsType<RegexQuantifiedSyntax>(pcre.GetRoot().Alternation.Branches[0].Terms[0]);
        Assert.Equal(RegexQuantifierMode.Possessive, quantified.Mode);
        Assert.Empty(pcre.GetDiagnostics());

        // .NET has no possessive quantifiers, so the same text is a quantifier applied to a quantifier.
        var net = RegexSyntaxTree.ParseText("a*+", RegexDialect.Net);
        Assert.Single(net.GetDiagnostics(), diagnostic => diagnostic.Id == "REGEX0006");
    }

    [Fact]
    public void JavaScriptHasNoStartOfInputAnchor()
    {
        var net = RegexSyntaxAssert.TextIsFaithful(@"\A", RegexDialect.Net);
        Assert.Single(net.GetRoot().DescendantNodes().OfType<RegexAnchorSyntax>());

        var javaScript = RegexSyntaxAssert.TextIsFaithful(@"\A", RegexDialect.JavaScript);
        Assert.Empty(javaScript.GetRoot().DescendantNodes().OfType<RegexAnchorSyntax>());
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

        Assert.Empty(tree.GetDiagnostics());
        var characterClass = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCharacterClassSyntax>());
        Assert.Equal(negated, characterClass.IsNegated);
        Assert.Empty(characterClass.Members);
    }

    [Fact]
    public void NetStillRejectsAnEmptyCharacterClass()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("[]", RegexDialect.Net);

        Assert.Contains(tree.GetDiagnostics(), d => d.Id == "REGEX0003");
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

        Assert.Empty(tree.GetDiagnostics());
        var escape = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCharacterEscapeSyntax>());
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

        Assert.Contains(tree.GetDiagnostics(), d => d.Id == "REGEX0012");
    }

    /// <summary>Without Unicode mode the braces are a bound, not part of the escape, which is what the engines do.</summary>
    [Fact]
    public void ACodePointEscapeIsNotOneOutsideUnicodeMode()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"\u{41}", RegexDialect.JavaScript);

        Assert.DoesNotContain(tree.GetRoot().DescendantNodes().OfType<RegexCharacterEscapeSyntax>(), e => e.Value == "A");
    }

    [Fact]
    public void ParseJavaScriptLiteral_ReadsTheDelimitersAndFlags()
    {
        const string Literal = "/a+b/giu";
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(Literal);
        RegexSyntaxAssert.TextIsFaithful(Literal, tree);

        Assert.True(tree.GetRoot().IsJavaScriptLiteral);
        Assert.Equal("/", tree.GetRoot().OpenSlashToken.Text);
        Assert.Equal("/", tree.GetRoot().CloseSlashToken.Text);
        Assert.Equal("giu", tree.GetRoot().FlagsToken.Text);
        Assert.Equal(RegexPatternOptions.Global | RegexPatternOptions.IgnoreCase | RegexPatternOptions.Unicode, tree.PatternOptions);
        Assert.Equal(2, tree.GetRoot().Alternation.Branches[0].Terms.Count);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void ParseJavaScriptLiteral_IgnoresASlashInsideACharacterClass()
    {
        const string Literal = "/[/]/g";
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(Literal);
        RegexSyntaxAssert.TextIsFaithful(Literal, tree);

        Assert.Equal("g", tree.GetRoot().FlagsToken.Text);
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCharacterClassSyntax>());
    }

    [Fact]
    public void ParseJavaScriptLiteral_ReportsAnUnknownFlag()
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral("/a/gq");
        RegexSyntaxAssert.TextIsFaithful("/a/gq", tree);

        Assert.Single(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "REGEX0205");
    }

    [Fact]
    public void ParseJavaScriptLiteral_KeepsContentThatFollowsTheLiteral()
    {
        const string Literal = "/a/g;";
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(Literal);
        RegexSyntaxAssert.TextIsFaithful(Literal, tree);

        Assert.Equal(";", tree.GetRoot().TrailingToken.Text);
        Assert.Single(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "REGEX0204");
    }

    [Fact]
    public void ParseJavaScriptLiteral_AcceptsABarePattern()
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral("a+");
        RegexSyntaxAssert.TextIsFaithful("a+", tree);

        Assert.False(tree.GetRoot().IsJavaScriptLiteral);
        Assert.Empty(tree.GetDiagnostics());
    }
}
