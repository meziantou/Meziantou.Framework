namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// Cases found by comparing this parser against the engines themselves -- V8 for JavaScript, PCRE2 for PCRE -- rather
/// than against my own reading of the grammars. Each one is a place the parser was wrong.
/// </summary>
public sealed class RegexOracleAuditTests
{
    private static RegexParseOptions Options(RegexDialect dialect, RegexPatternOptions options = RegexPatternOptions.None) =>
        new(dialect) { PatternOptions = options };

    private static RegexParseOptions SetMode =>
        Options(RegexDialect.JavaScript, RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets);

    private static RegexSyntaxTree Accepts(string pattern, RegexParseOptions options)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);
        Assert.Empty(tree.GetDiagnostics(), $"[{pattern}] reported {string.Join(",", tree.GetDiagnostics().Select(d => d.Id))}");

        return tree;
    }

    private static void Rejects(string pattern, RegexParseOptions options)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        Assert.NotEmpty(tree.GetDiagnostics(), $"[{pattern}] should not be accepted");
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
    public void ADoubleDashIsNotAnOperatorOutsideTheSetGrammar(string dialectName)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));

        var tree = RegexSyntaxAssert.TextIsFaithful("[a--b]", dialect);

        Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCharacterRangeSyntax>());
        Assert.Contains(tree.GetDiagnostics(), d => d.Id == "REGEX0009");
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
        Accepts(pattern, Options(RegexDialect.JavaScript));
        Rejects(pattern, Options(RegexDialect.JavaScript, RegexPatternOptions.Unicode));
    }

    /// <summary>A named backreference is only an identity escape while the pattern declares no name at all.</summary>
    [Fact]
    public void ANamedBackreferenceIsStillCheckedOnceThePatternHasNames()
    {
        Accepts(@"(?<n>a)\k<n>", Options(RegexDialect.JavaScript));
        Rejects(@"(?<n>a)\k<other>", Options(RegexDialect.JavaScript));
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
        Rejects(pattern, Options(RegexDialect.JavaScript));
        Rejects(pattern, Options(RegexDialect.JavaScript, RegexPatternOptions.Unicode));
    }

    /// <summary>Lookahead is the one assertion the web-compatibility grammar lets you repeat.</summary>
    [Theory]
    [InlineData("(?=a)*")]
    [InlineData("(?!a)*")]
    public void LookaheadIsQuantifiableOnlyInTheWebCompatibilityGrammar(string pattern)
    {
        Accepts(pattern, Options(RegexDialect.JavaScript));
        Rejects(pattern, Options(RegexDialect.JavaScript, RegexPatternOptions.Unicode));
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
        Accepts(pattern, Options(RegexDialect.JavaScript, RegexPatternOptions.Unicode));
    }

    [Fact]
    public void AnEmptyPropertyNameIsRejectedEverywhere()
    {
        Rejects(@"\p{}", Options(RegexDialect.JavaScript, RegexPatternOptions.Unicode));
        Rejects(@"\p{}", Options(RegexDialect.Net));
        Rejects(@"\p{}", Options(RegexDialect.PcrePerl));
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
    public void PcreAcceptsWhatPcre2Accepts(string pattern) => Accepts(pattern, Options(RegexDialect.PcrePerl));

    /// <summary>A subroutine call has to name a group that exists, whichever way it is spelled.</summary>
    [Theory]
    [InlineData("(?1)")]
    [InlineData(@"\g<1>")]
    [InlineData("(?&n)")]
    [InlineData(@"\g{-1}")]
    public void ARecursionIntoNothingIsReported(string pattern)
    {
        Rejects(pattern, Options(RegexDialect.PcrePerl));
        Accepts("(?<n>a)" + pattern, Options(RegexDialect.PcrePerl));
    }

    /// <summary><c>(?R)</c> restarts the whole pattern, so it never names a group that could be missing.</summary>
    [Fact]
    public void RecursingIntoTheWholePatternIsAlwaysValid() => Accepts("(?R)", Options(RegexDialect.PcrePerl));

    [Theory]
    [InlineData(@"\x{41}", "A")]
    [InlineData(@"\x41", "A")]
    [InlineData(@"\x4", "")]
    public void AHexEscapeMayBeShortOrBraced(string pattern, string value)
    {
        var escape = Assert.Single(Accepts(pattern, Options(RegexDialect.PcrePerl)).GetRoot().DescendantNodes().OfType<RegexCharacterEscapeSyntax>());

        Assert.Equal(value, escape.Value);
    }

    [Fact]
    public void ABracelessPropertyNamesOneLetter()
    {
        var category = Assert.Single(Accepts(@"\pL", Options(RegexDialect.PcrePerl)).GetRoot().DescendantNodes().OfType<RegexUnicodeCategorySyntax>());

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
    public void PosixDoesNotInterpretPerlEscapes(string dialectName)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));

        foreach (var pattern in new[] { @"\x41", @"A", @"\cA", @"\n", @"\k<n>", @"\k", @"\e" })
        {
            var tree = Accepts(pattern, Options(dialect));

            Assert.Empty(tree.GetRoot().DescendantNodes().OfType<RegexNamedBackreferenceSyntax>());
            foreach (var escape in tree.GetRoot().DescendantNodes().OfType<RegexCharacterEscapeSyntax>())
            {
                // The value is the letter itself, not what Perl would have made of it.
                Assert.Equal(escape.EscapeToken.Text[1..], escape.Value);
            }
        }
    }

    /// <summary>The shorthand classes POSIX dialects do have are the GNU ones, and they still work.</summary>
    [Theory]
    [InlineData(@"\w")]
    [InlineData(@"\S")]
    [InlineData(@"\b")]
    public void PosixKeepsTheGnuShorthands(string pattern) => Accepts(pattern, Options(RegexDialect.PosixExtended));

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

        Assert.Contains(tree.GetDiagnostics(), d => d.Id == "REGEX0072");
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

        Assert.Contains(tree.GetDiagnostics(), d => d.Id == "REGEX0071");
    }

    /// <summary>A backslash escapes the brace too, so the scan cannot stop at the first one it sees.</summary>
    [Theory]
    [InlineData(@"[\q{\}}]")]
    [InlineData(@"[\q{a\}b}]")]
    public void AnEscapedBraceDoesNotCloseAStringDisjunction(string pattern)
    {
        var literal = Assert.Single(Accepts(pattern, SetMode).GetRoot().DescendantNodes().OfType<RegexClassStringLiteralSyntax>());

        Assert.Contains('}', literal.Value);
    }

    /// <summary>
    /// Two patterns read with different options are different trees whatever their text, so the structural fallback
    /// has to check the options as well as the fast path did.
    /// </summary>
    [Fact]
    public void EquivalenceComparesOptionsEvenWhenTheTextMatches()
    {
        var plain = RegexSyntaxTree.ParseText("a", RegexDialect.Net);
        var ignoringCase = RegexSyntaxTree.ParseText("a", Options(RegexDialect.Net, RegexPatternOptions.IgnoreCase));

        Assert.False(plain.IsEquivalentTo(ignoringCase));
        Assert.False(ignoringCase.IsEquivalentTo(plain));
        Assert.True(plain.IsEquivalentTo(RegexSyntaxTree.ParseText("a", RegexDialect.Net)));
    }

    /// <summary>
    /// Building a node from a token that already belongs to a tree must not take it: the other tree would go on
    /// reporting text its own nodes no longer own.
    /// </summary>
    [Fact]
    public void BuildingANodeDoesNotStealATokenFromAnotherTree()
    {
        var source = RegexSyntaxTree.ParseText("ab", RegexDialect.Net);
        var borrowed = source.GetRoot().DescendantTokens().First(t => t.Text == "a");
        var owner = borrowed.Parent;

        _ = SyntaxFactory.Literal(borrowed, RegexPatternOptions.None);

        Assert.Same(owner, borrowed.Parent);
        Assert.Equal("ab", source.GetRoot().ToFullString());
    }

    /// <summary>Attaching to a tree is what records ownership, and it still does.</summary>
    [Fact]
    public void AParsedTreeStillReportsTheOwnerOfEveryToken()
    {
        var tree = RegexSyntaxTree.ParseText(@"(?<n>a|[b-d])\k<n>{2,3}?", RegexDialect.Net);

        foreach (var token in tree.GetRoot().DescendantTokens())
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

        Assert.Contains(tree.GetDiagnostics(), d => d.Id == "REGEX0209");
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

        Assert.DoesNotContain(tree.GetDiagnostics(), d => d.Id == "REGEX0209");
    }

    /// <summary>Only .NET spells a named backreference <c>\&lt;name&gt;</c>.</summary>
    [Fact]
    public void TheAngleBackreferenceIsNetOnly()
    {
        Assert.Single(Accepts(@"(?<n>a)\<n>", Options(RegexDialect.Net)).GetRoot().DescendantNodes().OfType<RegexNamedBackreferenceSyntax>());

        foreach (var dialect in new[] { RegexDialect.PcrePerl, RegexDialect.PosixExtended, RegexDialect.PosixBasic })
        {
            var tree = RegexSyntaxAssert.TextIsFaithful(@"\<n>", Options(dialect));

            Assert.Empty(tree.GetRoot().DescendantNodes().OfType<RegexNamedBackreferenceSyntax>());
        }
    }

    // ---- the second audit: every dialect replayed against its engine ----

    private static void RejectsWith(string pattern, RegexParseOptions options, string id)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, options);

        Assert.Contains(tree.GetDiagnostics(), d => d.Id == id, $"[{pattern}] reported {string.Join(",", tree.GetDiagnostics().Select(d => d.Id))}, expected {id}");
    }

    private static RegexParseOptions UnicodeMode => Options(RegexDialect.JavaScript, RegexPatternOptions.Unicode);

    private static RegexParseOptions Pcre => Options(RegexDialect.PcrePerl);

    private static RegexParseOptions Ere => Options(RegexDialect.PosixExtended);

    private static RegexParseOptions Bre => Options(RegexDialect.PosixBasic);

    /// <summary>
    /// .NET numbers named groups after every unnamed one. JavaScript and PCRE number every group where it stands,
    /// which decides what <c>\2</c> refers to.
    /// </summary>
    [Theory]
    [InlineData("net", "1,2,3=x")]
    [InlineData("javascript", "1,2=x,3")]
    [InlineData("pcre", "1,2=x,3")]
    public void NamedGroupsAreNumberedTheWayTheEngineNumbersThem(string dialectName, string expected)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));

        var tree = Accepts("(a)(?<x>b)(c)", Options(dialect));

        Assert.Equal(expected, string.Join(",", tree.Captures.Select(c => c.Name == c.Number.ToString(CultureInfo.InvariantCulture) ? c.Name : $"{c.Number}={c.Name}")));
    }

    [Fact]
    public void JavaScriptAllowsADuplicateNameOnlyInAnotherAlternative()
    {
        var tree = Accepts("(?<a>x)|(?<a>y)", Options(RegexDialect.JavaScript));
        Assert.Equal([1, 2], tree.Captures.Select(c => c.Number));
        Assert.All(tree.Captures, c => Assert.Equal("a", c.Name));

        Accepts(@"(?:(?<a>x)|(?<a>y))\k<a>", Options(RegexDialect.JavaScript));
        RejectsWith("(?<a>x)(?<a>y)", Options(RegexDialect.JavaScript), "REGEX0035");
        RejectsWith("((?<a>x)|(?<a>y))(?<a>z)", Options(RegexDialect.JavaScript), "REGEX0035");

        // V8 accepts these two, but both groups can take part in one match, which the specification forbids.
        RejectsWith("(?<a>x)(?:y|(?<a>z))", Options(RegexDialect.JavaScript), "REGEX0035");
        RejectsWith("(?<b>(?<b>a)|)", Options(RegexDialect.JavaScript), "REGEX0035");
    }

    /// <summary>A decimal escape takes every digit, and names a group when the whole pattern has that many.</summary>
    [Fact]
    public void AJavaScriptDecimalEscapeCountsEveryGroupOfThePattern()
    {
        Assert.Single(Accepts(@"\1(a)", UnicodeMode).GetRoot().DescendantNodes().OfType<RegexBackreferenceSyntax>());
        Assert.Equal(10, Assert.Single(Accepts(@"(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\10", UnicodeMode).GetRoot().DescendantNodes().OfType<RegexBackreferenceSyntax>()).Number);

        // With a single group, "\10" is the legacy octal escape for a backspace, not a reference to group 1.
        var escape = Assert.Single(Accepts(@"(a)\10", Options(RegexDialect.JavaScript)).GetRoot().DescendantNodes().OfType<RegexCharacterEscapeSyntax>());
        Assert.Equal("\b", escape.Value);
        RejectsWith(@"(a)\10", UnicodeMode, "REGEX0030");
        RejectsWith(@"\2(a)", UnicodeMode, "REGEX0030");
        RejectsWith(@"\08", UnicodeMode, "REGEX0015");
    }

    [Theory]
    [InlineData(@"\a")]
    [InlineData(@"\e")]
    [InlineData(@"[\e]")]
    public void JavaScriptHasNoBellOrEscapeCharacterEscape(string pattern)
    {
        RejectsWith(pattern, UnicodeMode, "REGEX0015");

        // The web-compatibility grammar reads the letter itself.
        var escape = Assert.Single(Accepts(pattern, Options(RegexDialect.JavaScript)).GetRoot().DescendantNodes().OfType<RegexCharacterEscapeSyntax>());
        Assert.Equal(pattern[^1..] == "]" ? pattern[^2..^1] : pattern[^1..], escape.Value);
    }

    /// <summary>The <c>v</c> flag is the <c>u</c> flag with more, so it alone is enough for the strict grammar.</summary>
    [Fact]
    public void TheUnicodeSetsOptionAloneSelectsTheStrictGrammar() =>
        RejectsWith(@"\q", Options(RegexDialect.JavaScript, RegexPatternOptions.UnicodeSets), "REGEX0015");

    [Fact]
    public void AModifiersGroupAppliesItsFlagsToItsBody()
    {
        var tree = Accepts("(?i-m:a)b", Options(RegexDialect.JavaScript, RegexPatternOptions.Multiline));
        var group = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexOptionsGroupSyntax>());
        var literals = tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().ToArray();

        Assert.Equal("i-m", group.OptionsText);
        Assert.Equal(RegexPatternOptions.IgnoreCase, literals[0].Options);
        Assert.Equal(RegexPatternOptions.Multiline, literals[1].Options);
    }

    [Theory]
    [InlineData("(?ii:a)", "REGEX0075")]
    [InlineData("(?i-i:a)", "REGEX0075")]
    [InlineData("(?-:a)", "REGEX0075")]
    [InlineData("(?x:a)", "REGEX0075")]
    [InlineData("(?i)a", "REGEX0010")]
    public void AMalformedModifiersGroupIsReported(string pattern, string id) => RejectsWith(pattern, Options(RegexDialect.JavaScript), id);

    [Theory]
    [InlineData(@"(?<$>x)", "$")]
    [InlineData(@"(?<\u0061>x)\k<a>", "a")]
    [InlineData(@"(?<\u{61}b>x)", "ab")]
    [InlineData("(?<\U0001D49C>x)", "\U0001D49C")]
    [InlineData("(?<a\u00B7>x)", "a\u00B7")]
    public void AJavaScriptGroupNameIsAnIdentifier(string pattern, string name)
    {
        var group = Assert.Single(Accepts(pattern, Options(RegexDialect.JavaScript)).GetRoot().DescendantNodes().OfType<RegexNamedGroupSyntax>());

        Assert.Equal(name, group.Name);
    }

    [Theory]
    [InlineData(@"\p{Lu}")]
    [InlineData(@"\p{Uppercase_Letter}")]
    [InlineData(@"\p{gc=Lu}")]
    [InlineData(@"\p{Script_Extensions=Latn}")]
    [InlineData(@"\P{ASCII_Hex_Digit}")]
    public void AJavaScriptPropertyFromTheSpecificationIsAccepted(string pattern) => Accepts(pattern, UnicodeMode);

    [Theory]
    [InlineData(@"\p{lu}")]
    [InlineData(@"\p{Greek}")]
    [InlineData(@"\p{IsGreek}")]
    [InlineData(@"\p{sc=Bogus}")]
    [InlineData(@"\p{^L}")]
    [InlineData(@"\p{Letter=L}")]
    [InlineData(@"\p{RGI_Emoji}")]
    public void AJavaScriptPropertyTheSpecificationLacksIsReported(string pattern) => RejectsWith(pattern, UnicodeMode, "REGEX0034");

    [Theory]
    [InlineData(@"[^\q{ab}]")]
    [InlineData(@"[^\q{}]")]
    [InlineData(@"[^[\q{ab}]]")]
    [InlineData(@"[^\p{RGI_Emoji}]")]
    [InlineData(@"[^\q{ab}--\q{ab}]")]
    [InlineData(@"\P{RGI_Emoji}")]
    public void ANegatedClassCannotContainStrings(string pattern) => RejectsWith(pattern, SetMode, "REGEX0074");

    [Theory]
    [InlineData(@"[^\q{a|b}]")]
    [InlineData(@"[^\q{ab}&&a]")]
    [InlineData(@"[^a--\p{RGI_Emoji}]")]
    [InlineData(@"[\p{RGI_Emoji}]")]
    public void AClassThatOnlyMatchesCharactersMayBeNegated(string pattern) => Accepts(pattern, SetMode);

    [Theory]
    [InlineData("[(]")]
    [InlineData("[|]")]
    [InlineData("[{]")]
    [InlineData("[a-]")]
    [InlineData("[-a]")]
    public void TheSetGrammarWantsItsSyntaxCharactersEscaped(string pattern) => RejectsWith(pattern, SetMode, "REGEX0073");

    [Theory]
    [InlineData(@"[\&]")]
    [InlineData(@"[\!]")]
    [InlineData(@"[\~]")]
    [InlineData(@"[\q{\u{1F600}}]")]
    public void TheSetGrammarLetsItsReservedPunctuatorsBeEscaped(string pattern) => Accepts(pattern, SetMode);

    /// <summary>In Unicode mode a range compares code points, so a surrogate pair is one endpoint rather than two.</summary>
    [Theory]
    [InlineData("[\U0001F600-\U0001F602]", "[\U0001F602-\U0001F600]")]
    [InlineData(@"[\uD83D\uDE00-\uD83D\uDE02]", @"[\uD83D\uDE02-\uD83D\uDE00]")]
    [InlineData(@"[\u{1F600}-\u{1F602}]", @"[\u{1F602}-\u{1F600}]")]
    public void ARangeOfAstralCharactersComparesCodePoints(string pattern, string reversed)
    {
        var range = Assert.Single(Accepts(pattern, UnicodeMode).GetRoot().DescendantNodes().OfType<RegexCharacterRangeSyntax>());

        Assert.NotNull(range.End);
        RejectsWith(reversed, UnicodeMode, "REGEX0009");
    }

    [Fact]
    public void AStrictShorthandClassCannotStartARange()
    {
        RejectsWith(@"[\d-z]", UnicodeMode, "REGEX0016");
        Accepts(@"[\d-z]", Options(RegexDialect.JavaScript));
        Accepts(@"[\d-]", UnicodeMode);
    }

    [Fact]
    public void ABracedSurrogateEscapeIsParsedWithoutThrowing() => Accepts(@"\u{D83D}\u{DE00}[\u{D83D}]", UnicodeMode);

    [Theory]
    [InlineData(@"(?<a>x)\k<a", "REGEX0019")]
    [InlineData(@"(?<a>x)\k", "REGEX0019")]
    public void AMalformedNamedReferenceIsAnErrorOnceThePatternHasNames(string pattern, string id)
    {
        RejectsWith(pattern, Options(RegexDialect.JavaScript), id);
        Accepts(pattern[7..], Options(RegexDialect.JavaScript));
    }

    // ---- PCRE ----

    [Fact]
    public void ABranchResetGroupNumbersEveryBranchFromTheSameStart()
    {
        var tree = Accepts(@"(?|(a)|(b)(c))(d)\3", Pcre);

        Assert.Equal([1, 2, 3], tree.Captures.Select(c => c.Number));
        Assert.Equal(3, Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexBackreferenceSyntax>()).Number);
        Accepts("(?|(?<a>x)|(?<a>y))", Pcre);
        RejectsWith("(?|(?<a>x)|(?<b>y))", Pcre, "REGEX0035");
    }

    [Fact]
    public void PcreAllowsDuplicateNamesOnlyWhereTheJOptionIsInEffect()
    {
        RejectsWith("(?<a>x)(?<a>y)", Pcre, "REGEX0035");
        RejectsWith("(?J:(?<a>x))(?<a>y)", Pcre, "REGEX0035");
        RejectsWith("(?J)(?<a>x)(?-J)(?<a>y)", Pcre, "REGEX0035");

        var tree = Accepts("(?J)(?<a>x)(?<a>y)", Pcre);
        Assert.Equal(["a", "a"], tree.Captures.Select(c => c.Name));
    }

    /// <summary>A PCRE condition names a group without capturing anything, however it spells the name.</summary>
    [Theory]
    [InlineData("(?(<a>)x|y)(?<a>z)", "a")]
    [InlineData("(?('a')x|y)(?<a>z)", "a")]
    [InlineData("(?(a)x|y)(?<a>z)", "a")]
    [InlineData("(?(R1)x|y)(z)", "R1")]
    [InlineData("(?(R&a)x|y)(?<a>z)", "R&a")]
    [InlineData("(?(-1)x|y)", "-1")]
    [InlineData("(?(DEFINE)(?<a>z))", "DEFINE")]
    [InlineData("(?(VERSION>=10.4)x|y)(z)", "VERSION>=10.4")]
    public void APcreConditionIsAReference(string pattern, string name)
    {
        if (name == "-1")
        {
            pattern = "(z)" + pattern;
        }

        var tree = Accepts(pattern, Pcre);

        Assert.Equal(name, Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexConditionalReferenceSyntax>()).Name);
        Assert.Single(tree.Captures);
    }

    [Theory]
    [InlineData("(?(a)x|y)", "REGEX0056")]
    [InlineData("(?(0)x)", "REGEX0056")]
    [InlineData("(?(-1)x|y)", "REGEX0030")]
    [InlineData("(?(DEFINE)a|b)", "REGEX0051")]
    [InlineData("(?(?:a)b)", "REGEX0093")]
    [InlineData("(?(?C1)a|b)", "REGEX0093")]
    [InlineData("(?(VERSION>10)a)", "REGEX0055")]
    public void AMalformedPcreConditionIsReported(string pattern, string id) => RejectsWith(pattern, Pcre, id);

    [Fact]
    public void ARelativeSubroutineCallCountsFromWhereItStands()
    {
        Assert.Equal("-1", Assert.Single(Accepts("(a)(?-1)", Pcre).GetRoot().DescendantNodes().OfType<RegexRecursionSyntax>()).TargetToken.Text);
        Accepts("(?+1)(a)", Pcre);
        RejectsWith("(?-1)(a)", Pcre, "REGEX0030");
        RejectsWith("(a)(?+1)", Pcre, "REGEX0030");
        RejectsWith("(a)(?-0)", Pcre, "REGEX0030");
    }

    [Theory]
    [InlineData("(*MARK)", "REGEX0090")]
    [InlineData("(*:)", "REGEX0090")]
    [InlineData("(*BOGUS)", "REGEX0090")]
    [InlineData("a(*UTF)", "REGEX0090")]
    [InlineData("(*MARK:a)(*UTF)", "REGEX0090")]
    [InlineData("(*FAIL)*", "REGEX0005")]
    [InlineData("(?C256)", "REGEX0091")]
    [InlineData("(?Cx)", "REGEX0091")]
    [InlineData("(?C\"a)", "REGEX0091")]
    [InlineData("(?C1)*", "REGEX0005")]
    public void AMalformedVerbOrCalloutIsReported(string pattern, string id) => RejectsWith(pattern, Pcre, id);

    [Theory]
    [InlineData("(*UTF)(*LIMIT_MATCH=10)a")]
    [InlineData("(*ACCEPT)+")]
    [InlineData("(*MARK:a)(*SKIP:a)")]
    [InlineData("(?C{a})")]
    [InlineData("(?C\"a\"\"b\")")]
    public void AWellFormedVerbOrCalloutIsAccepted(string pattern) => Accepts(pattern, Pcre);

    [Theory]
    [InlineData("(*pla:a)", RegexLookaroundKind.PositiveLookahead)]
    [InlineData("(*negative_lookahead:a)", RegexLookaroundKind.NegativeLookahead)]
    [InlineData("(*plb:a)", RegexLookaroundKind.PositiveLookbehind)]
    [InlineData("(*nlb:a)", RegexLookaroundKind.NegativeLookbehind)]
    public void AnAlphaAssertionIsALookaround(string pattern, RegexLookaroundKind kind)
    {
        var lookaround = Assert.Single(Accepts(pattern, Pcre).GetRoot().DescendantNodes().OfType<RegexLookaroundSyntax>());

        Assert.Equal(kind, lookaround.LookaroundKind);
    }

    [Theory]
    [InlineData("(?<=a+)")]
    [InlineData(@"(?<=\X)")]
    [InlineData("(?<=(?:a|b{300}))")]
    [InlineData(@"(?<=(a+)\1)")]
    [InlineData("(?<=(?1))(a+)")]
    [InlineData("(*plb:a*)")]
    public void AnUnboundedLookbehindIsReported(string pattern) => RejectsWith(pattern, Pcre, "REGEX0092");

    [Theory]
    [InlineData("(?<=a{300})")]
    [InlineData("(?<=a|bc)")]
    [InlineData("(?<=a{0,255})")]
    [InlineData(@"(?<=(a)\1{300})")]
    [InlineData("(?<=(?=a+))b")]
    public void ABoundedLookbehindIsAccepted(string pattern) => Accepts(pattern, Pcre);

    [Theory]
    [InlineData(@"\b*")]
    [InlineData("^*")]
    [InlineData(@"a\K+")]
    [InlineData(@"\Q\E*")]
    [InlineData("{,2}")]
    public void PcreCannotRepeatSomethingThatMatchesNothing(string pattern) => RejectsWith(pattern, Pcre, "REGEX0005");

    [Theory]
    [InlineData("(?^i)")]
    [InlineData("(?aD)")]
    [InlineData("(?xx)")]
    [InlineData("(?r)")]
    [InlineData("(?-)")]
    [InlineData(@"\p{ L u }")]
    [InlineData(@"\p{greek}")]
    [InlineData(@"\p{sc:Latn}")]
    [InlineData(@"\p{L&}")]
    [InlineData(@"\pl")]
    [InlineData(@"\_")]
    [InlineData(@"\é")]
    [InlineData(@"[\8\g\k]")]
    [InlineData(@"[\Q]\E]")]
    [InlineData(@"(?<a>x)\k{a}")]
    [InlineData(@"\8(a)(b)(c)(d)(e)(f)(g)(h)")]
    [InlineData("(?<\U0001D49C>x)")]
    public void PcreAcceptsWhatPcre2AcceptsToo(string pattern) => Accepts(pattern, Pcre);

    [Theory]
    [InlineData("(?u)", "REGEX0010")]
    [InlineData("(?I)", "REGEX0010")]
    [InlineData("(?^-i)", "REGEX0010")]
    [InlineData(@"\p{Letter}", "REGEX0034")]
    [InlineData(@"\p{gc=L}", "REGEX0034")]
    [InlineData(@"\pb", "REGEX0034")]
    [InlineData(@"\u0041", "REGEX0015")]
    [InlineData(@"\ce", "REGEX0014")]
    [InlineData(@"\81", "REGEX0030")]
    [InlineData(@"(?=a\K)", "REGEX0015")]
    [InlineData(@"[\d-z]", "REGEX0016")]
    [InlineData("[[:alpha:]-z]", "REGEX0016")]
    [InlineData("[[:bogus:]]", "REGEX0110")]
    [InlineData("[[.a.]]", "REGEX0110")]
    [InlineData("[:alpha:]", "REGEX0110")]
    [InlineData("a{65536}", "REGEX0008")]
    [InlineData("(?<a>x)(?<a>y)", "REGEX0035")]
    public void PcreRejectsWhatPcre2RejectsToo(string pattern, string id)
    {
        if (id == "REGEX0014")
        {
            pattern = "\\c\u00E9";
        }

        RejectsWith(pattern, Pcre, id);
    }

    [Fact]
    public void APcreBoundMayHaveSpacesAndLeaveOutItsMinimum()
    {
        var quantifiers = Accepts("a{ 2 , 3 }b{,4}", Pcre).GetRoot().DescendantNodes().OfType<RegexRangeQuantifierSyntax>().ToArray();

        Assert.Equal((2, (int?)3), (quantifiers[0].MinCount, quantifiers[0].MaxCount));
        Assert.Equal((0, (int?)4), (quantifiers[1].MinCount, quantifiers[1].MaxCount));
    }

    [Fact]
    public void BackslashNFollowedByABoundIsARepeatedShorthand()
    {
        var tree = Accepts(@"\N{2,3}[\x{41}]", Pcre);

        Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexQuantifiedSyntax>());
        Assert.Equal("A", Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCharacterEscapeSyntax>()).Value);
    }

    // ---- POSIX, as glibc reads it ----

    [Theory]
    [InlineData("ere")]
    [InlineData("bre")]
    public void ABackslashIsOrdinaryInsideABracketExpression(string dialectName)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));

        var tree = Accepts(@"[\]]", Options(dialect));
        var characterClass = Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexCharacterClassSyntax>());

        Assert.Equal(@"[\]", characterClass.ToString());
        Assert.Equal("]", tree.GetRoot().Alternation.Branches[0].Terms[1].ToString());
    }

    [Fact]
    public void AnUnmatchedCloseParenthesisIsACharacterOnlyInAnExtendedExpression()
    {
        Accepts("a)", Ere);
        Accepts(@"a)", Bre);
        RejectsWith(@"a\)", Bre, "REGEX0001");
    }

    [Fact]
    public void OnlyABasicExpressionForbidsStackingStarOrABound()
    {
        Accepts("a**", Ere);
        Accepts("a{2}{3}", Ere);
        RejectsWith("a**", Bre, "REGEX0006");
        RejectsWith(@"a\{2\}*", Bre, "REGEX0006");
        Accepts(@"a*\+", Bre);
    }

    [Theory]
    [InlineData("ere", "a{1", "REGEX0111")]
    [InlineData("ere", "x{}", "REGEX0111")]
    [InlineData("ere", "{1}", "REGEX0005")]
    [InlineData("ere", "a{32768}", "REGEX0008")]
    [InlineData("bre", @"\{1\}", "REGEX0005")]
    [InlineData("bre", @"^\{1\}", "REGEX0005")]
    [InlineData("bre", @"a\{1", "REGEX0111")]
    public void APosixIntervalIsNeverOrdinaryText(string dialectName, string pattern, string id)
    {
        Assert.True(RegexDialect.TryParse(dialectName, out var dialect));

        RejectsWith(pattern, Options(dialect), id);
    }

    [Fact]
    public void APosixBackreferenceIsOneDigitNamingAGroupAlreadyClosed()
    {
        var tree = Accepts(@"(a)\10", Ere);
        Assert.Equal(1, Assert.Single(tree.GetRoot().DescendantNodes().OfType<RegexBackreferenceSyntax>()).Number);

        Accepts("(a)|(b)\\2", Ere);
        RejectsWith(@"(a\1)", Ere, "REGEX0030");
        RejectsWith(@"(a)|\1", Ere, "REGEX0030");
        RejectsWith(@"\1(a)", Ere, "REGEX0030");
        RejectsWith(@"\(a\)\|\1", Bre, "REGEX0030");
    }

    [Fact]
    public void AnExtendedExpressionCannotRepeatAnAssertion()
    {
        RejectsWith("^*", Ere, "REGEX0005");
        RejectsWith(@"\<*", Ere, "REGEX0005");

        // In a basic expression the "*" is the character.
        var tree = Accepts(@"^*\b*", Bre);
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<RegexQuantifiedSyntax>());
    }

    [Theory]
    [InlineData(@"\<", RegexAnchorKind.StartOfWord)]
    [InlineData(@"\>", RegexAnchorKind.EndOfWord)]
    [InlineData(@"\`", RegexAnchorKind.StartOfInput)]
    [InlineData(@"\'", RegexAnchorKind.EndOfInput)]
    public void TheGnuWordAnchorsAreAssertions(string pattern, RegexAnchorKind kind)
    {
        Assert.Equal(kind, Assert.Single(Accepts(pattern, Ere).GetRoot().DescendantNodes().OfType<RegexAnchorSyntax>()).AnchorKind);
    }

    [Theory]
    [InlineData("[[:bogus:]]", "REGEX0110")]
    [InlineData("[[.ab.]]", "REGEX0110")]
    [InlineData("[[:alpha]]", "REGEX0003")]
    [InlineData("[[:alpha:]-z]", "REGEX0016")]
    [InlineData("[a-[=z=]]", "REGEX0016")]
    [InlineData("[z-[.a.]]", "REGEX0009")]
    [InlineData("[a-z-9]", "REGEX0009")]
    public void AMalformedBracketExpressionIsReported(string pattern, string id) => RejectsWith(pattern, Ere, id);

    [Theory]
    [InlineData("[a-[.z.]]")]
    [InlineData("[[.-.]-z]")]
    [InlineData("[%--]")]
    [InlineData("[a-z-]")]
    [InlineData("[[:alpha:]-]")]
    public void AWellFormedBracketExpressionIsAccepted(string pattern) => Accepts(pattern, Ere);
}
