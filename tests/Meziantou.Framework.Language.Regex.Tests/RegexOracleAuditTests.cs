namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// Cases found by comparing this parser against the engines themselves -- V8 for JavaScript, PCRE2 for PCRE -- rather
/// than against my own reading of the grammars. Each one is a place the parser was wrong.
/// </summary>
public sealed class RegexOracleAuditTests
{
    private static RegexParseOptions Options(RegexFlavor flavor, RegexPatternOptions options = RegexPatternOptions.None) =>
        new(flavor) { PatternOptions = options };

    private static RegexParseOptions SetMode =>
        Options(RegexFlavor.JavaScript, RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets);

    private static RegexSyntaxTree Accepts(string pattern, RegexParseOptions options)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);
        Assert.Empty(tree.Diagnostics, $"[{pattern}] reported {string.Join(",", tree.Diagnostics.Select(d => d.Id))}");

        return tree;
    }

    private static void Rejects(string pattern, RegexParseOptions options)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        Assert.NotEmpty(tree.Diagnostics, $"[{pattern}] should not be accepted");
    }

    // ---- a regression the class set work introduced ----

    /// <summary>
    /// Only the class set grammar has a <c>--</c> operator. Everywhere else those are two dashes, and suppressing the
    /// range look-ahead for them turned <c>[a--b]</c> from a reversed range into three quiet members.
    /// </summary>
    [Theory]
    [InlineData("net")]
    [InlineData("javascript")]
    [InlineData("pcre")]
    public void ADoubleDashIsNotAnOperatorOutsideTheSetGrammar(string flavorName)
    {
        Assert.True(RegexFlavor.TryParse(flavorName, out var flavor));

        var tree = RegexSyntaxAssert.TextIsFaithful("[a--b]", flavor);

        Assert.Single(tree.Root.DescendantNodes().OfType<RegexCharacterRangeSyntax>());
        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0009");
    }

    // ---- JavaScript: two grammars, chosen by the flag ----

    /// <summary>
    /// Without <c>u</c> the web-compatibility grammar applies, and an escape that is not well formed stands for its
    /// own letter instead of being an error.
    /// </summary>
    [Theory]
    [InlineData(@"\x4")]
    [InlineData(@"\u00")]
    [InlineData(@"\c")]
    [InlineData(@"\c1")]
    [InlineData(@"\k")]
    [InlineData(@"\k<n>")]
    [InlineData(@"[a-\d]")]
    [InlineData("]")]
    [InlineData("}")]
    [InlineData("{a}")]
    [InlineData(@"\1")]
    [InlineData(@"\01")]
    [InlineData(@"\-")]
    public void TheWebCompatibilityGrammarAcceptsWhatTheStrictOneDoesNot(string pattern)
    {
        Accepts(pattern, Options(RegexFlavor.JavaScript));
        Rejects(pattern, Options(RegexFlavor.JavaScript, RegexPatternOptions.Unicode));
    }

    /// <summary>A named backreference is only an identity escape while the pattern declares no name at all.</summary>
    [Fact]
    public void ANamedBackreferenceIsStillCheckedOnceThePatternHasNames()
    {
        Accepts(@"(?<n>a)\k<n>", Options(RegexFlavor.JavaScript));
        Rejects(@"(?<n>a)\k<other>", Options(RegexFlavor.JavaScript));
    }

    /// <summary>An assertion matches nothing, so repeating it means nothing.</summary>
    [Theory]
    [InlineData(@"\b*")]
    [InlineData(@"\B?")]
    [InlineData("^*")]
    [InlineData("$*")]
    [InlineData("(?<=a)*")]
    public void AnAssertionCannotBeQuantified(string pattern)
    {
        Rejects(pattern, Options(RegexFlavor.JavaScript));
        Rejects(pattern, Options(RegexFlavor.JavaScript, RegexPatternOptions.Unicode));
    }

    /// <summary>Lookahead is the one assertion the web-compatibility grammar lets you repeat.</summary>
    [Theory]
    [InlineData("(?=a)*")]
    [InlineData("(?!a)*")]
    public void LookaheadIsQuantifiableOnlyInTheWebCompatibilityGrammar(string pattern)
    {
        Accepts(pattern, Options(RegexFlavor.JavaScript));
        Rejects(pattern, Options(RegexFlavor.JavaScript, RegexPatternOptions.Unicode));
    }

    [Theory]
    [InlineData(@"\0")]
    [InlineData("[a-]")]
    [InlineData("a{2,3}")]
    [InlineData(@"[\-]")]
    [InlineData(@"\/")]
    [InlineData(@"\$")]
    public void TheStrictGrammarStillAcceptsWhatItShould(string pattern)
    {
        Accepts(pattern, Options(RegexFlavor.JavaScript, RegexPatternOptions.Unicode));
    }

    [Fact]
    public void AnEmptyPropertyNameIsRejectedEverywhere()
    {
        Rejects(@"\p{}", Options(RegexFlavor.JavaScript, RegexPatternOptions.Unicode));
        Rejects(@"\p{}", Options(RegexFlavor.Net));
        Rejects(@"\p{}", Options(RegexFlavor.PcrePerl));
    }

    // ---- PCRE ----

    [Theory]
    [InlineData("(?)")]
    [InlineData(@"\x4")]
    [InlineData(@"\x{41}")]
    [InlineData(@"\pL")]
    [InlineData(@"\E")]
    [InlineData(@"\<n>")]
    [InlineData(@"\c(")]
    public void PcreAcceptsWhatPcre2Accepts(string pattern) => Accepts(pattern, Options(RegexFlavor.PcrePerl));

    /// <summary>A subroutine call has to name a group that exists, whichever way it is spelled.</summary>
    [Theory]
    [InlineData("(?1)")]
    [InlineData(@"\g<1>")]
    [InlineData("(?&n)")]
    [InlineData(@"\g{-1}")]
    public void ARecursionIntoNothingIsReported(string pattern)
    {
        Rejects(pattern, Options(RegexFlavor.PcrePerl));
        Accepts("(?<n>a)" + pattern, Options(RegexFlavor.PcrePerl));
    }

    /// <summary><c>(?R)</c> restarts the whole pattern, so it never names a group that could be missing.</summary>
    [Fact]
    public void RecursingIntoTheWholePatternIsAlwaysValid() => Accepts("(?R)", Options(RegexFlavor.PcrePerl));

    [Theory]
    [InlineData(@"\x{41}", "A")]
    [InlineData(@"\x41", "A")]
    [InlineData(@"\x4", "")]
    public void AHexEscapeMayBeShortOrBraced(string pattern, string value)
    {
        var escape = Assert.Single(Accepts(pattern, Options(RegexFlavor.PcrePerl)).Root.DescendantNodes().OfType<RegexCharacterEscapeSyntax>());

        Assert.Equal(value, escape.Value);
    }

    [Fact]
    public void ABracelessPropertyNamesOneLetter()
    {
        var category = Assert.Single(Accepts(@"\pL", Options(RegexFlavor.PcrePerl)).Root.DescendantNodes().OfType<RegexUnicodeCategorySyntax>());

        Assert.Equal("L", category.Name);
    }

    // ---- POSIX has none of the Perl escapes ----

    /// <summary>
    /// A backslash before an ordinary character means that character. Reading <c>\x41</c> as an "A" would describe a
    /// pattern the engine never sees.
    /// </summary>
    [Theory]
    [InlineData("ere")]
    [InlineData("bre")]
    public void PosixDoesNotInterpretPerlEscapes(string flavorName)
    {
        Assert.True(RegexFlavor.TryParse(flavorName, out var flavor));

        foreach (var pattern in new[] { @"\x41", @"A", @"\cA", @"\n", @"\k<n>", @"\k", @"\e" })
        {
            var tree = Accepts(pattern, Options(flavor));

            Assert.Empty(tree.Root.DescendantNodes().OfType<RegexNamedBackreferenceSyntax>());
            foreach (var escape in tree.Root.DescendantNodes().OfType<RegexCharacterEscapeSyntax>())
            {
                // The value is the letter itself, not what Perl would have made of it.
                Assert.Equal(escape.EscapeToken.Text[1..], escape.Value);
            }
        }
    }

    /// <summary>The shorthand classes POSIX flavors do have are the GNU ones, and they still work.</summary>
    [Theory]
    [InlineData(@"\w")]
    [InlineData(@"\S")]
    [InlineData(@"\b")]
    public void PosixKeepsTheGnuShorthands(string pattern) => Accepts(pattern, Options(RegexFlavor.PosixExtended));

    // ---- review follow-ups ----

    /// <summary>
    /// A <c>\q{…}</c> body is not free text: the class set syntax characters have to be escaped, and the doubled
    /// punctuators the grammar reserves may not appear at all.
    /// </summary>
    [Theory]
    [InlineData(@"[\q{a}]")]
    [InlineData(@"[\q{ab|cd}]")]
    [InlineData(@"[\q{}]")]
    [InlineData(@"[\q{|}]")]
    [InlineData(@"[\q{a b}]")]
    [InlineData(@"[\q{\[}]")]
    [InlineData(@"[\q{\]}]")]
    [InlineData(@"[\q{\-}]")]
    [InlineData(@"[\q{\/}]")]
    [InlineData(@"[\q{a&b}]")]
    public void AWellFormedStringDisjunctionIsAccepted(string pattern) => Accepts(pattern, SetMode);

    [Theory]
    [InlineData(@"[\q{[}]")]
    [InlineData(@"[\q{]}]")]
    [InlineData(@"[\q{(}]")]
    [InlineData(@"[\q{)}]")]
    [InlineData(@"[\q{{}]")]
    [InlineData(@"[\q{-}]")]
    [InlineData(@"[\q{/}]")]
    public void AnUnescapedSyntaxCharacterInAStringDisjunctionIsReported(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, SetMode);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0072");
    }

    [Theory]
    [InlineData(@"[\q{&&}]")]
    [InlineData(@"[\q{!!}]")]
    [InlineData(@"[\q{~~}]")]
    [InlineData("[!!]")]
    [InlineData("[a!!b]")]
    public void AReservedDoublePunctuatorIsReported(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, SetMode);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0071");
    }

    /// <summary>A backslash escapes the brace too, so the scan cannot stop at the first one it sees.</summary>
    [Theory]
    [InlineData(@"[\q{\}}]")]
    [InlineData(@"[\q{a\}b}]")]
    public void AnEscapedBraceDoesNotCloseAStringDisjunction(string pattern)
    {
        var literal = Assert.Single(Accepts(pattern, SetMode).Root.DescendantNodes().OfType<RegexClassStringLiteralSyntax>());

        Assert.Contains('}', literal.Value);
    }

    /// <summary>
    /// Two patterns read with different options are different trees whatever their text, so the structural fallback
    /// has to check the options as well as the fast path did.
    /// </summary>
    [Fact]
    public void EquivalenceComparesOptionsEvenWhenTheTextMatches()
    {
        var plain = RegexSyntaxTree.ParseText("a", RegexFlavor.Net);
        var ignoringCase = RegexSyntaxTree.ParseText("a", Options(RegexFlavor.Net, RegexPatternOptions.IgnoreCase));

        Assert.False(plain.IsEquivalentTo(ignoringCase));
        Assert.False(ignoringCase.IsEquivalentTo(plain));
        Assert.True(plain.IsEquivalentTo(RegexSyntaxTree.ParseText("a", RegexFlavor.Net)));
    }

    /// <summary>
    /// Building a node from a token that already belongs to a tree must not take it: the other tree would go on
    /// reporting text its own nodes no longer own.
    /// </summary>
    [Fact]
    public void BuildingANodeDoesNotStealATokenFromAnotherTree()
    {
        var source = RegexSyntaxTree.ParseText("ab", RegexFlavor.Net);
        var borrowed = source.Root.DescendantTokens().First(t => t.Text == "a");
        var owner = borrowed.Parent;

        _ = new RegexLiteralSyntax(borrowed);

        Assert.Same(owner, borrowed.Parent);
        Assert.Equal("ab", source.Root.ToFullString());
    }

    /// <summary>Attaching to a tree is what records ownership, and it still does.</summary>
    [Fact]
    public void AParsedTreeStillReportsTheOwnerOfEveryToken()
    {
        var tree = RegexSyntaxTree.ParseText(@"(?<n>a|[b-d])\k<n>{2,3}?", RegexFlavor.Net);

        foreach (var token in tree.Root.DescendantTokens())
        {
            Assert.NotNull(token.Parent);
        }
    }

    [Theory]
    [InlineData("/a")]
    [InlineData("/")]
    [InlineData("/[/")]
    public void AnUnterminatedLiteralIsReported(string literal)
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(literal);
        RegexSyntaxAssert.TextIsFaithful(literal, tree);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0209");
    }

    /// <summary>Text that never claimed to be a literal is a bare pattern, not an unterminated one.</summary>
    [Theory]
    [InlineData("a+")]
    [InlineData("/a/")]
    [InlineData("/a/g")]
    public void AWellFormedOrBareInputIsNotReportedAsUnterminated(string literal)
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(literal);
        RegexSyntaxAssert.TextIsFaithful(literal, tree);

        Assert.DoesNotContain(tree.Diagnostics, d => d.Id == "REGEX0209");
    }

    /// <summary>Only .NET spells a named backreference <c>\&lt;name&gt;</c>.</summary>
    [Fact]
    public void TheAngleBackreferenceIsNetOnly()
    {
        Assert.Single(Accepts(@"(?<n>a)\<n>", Options(RegexFlavor.Net)).Root.DescendantNodes().OfType<RegexNamedBackreferenceSyntax>());

        foreach (var flavor in new[] { RegexFlavor.PcrePerl, RegexFlavor.PosixExtended, RegexFlavor.PosixBasic })
        {
            var tree = RegexSyntaxAssert.TextIsFaithful(@"\<n>", Options(flavor));

            Assert.Empty(tree.Root.DescendantNodes().OfType<RegexNamedBackreferenceSyntax>());
        }
    }
}
