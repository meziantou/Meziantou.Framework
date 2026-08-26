namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// Cases found by comparing this parser against the engines themselves -- V8 for JavaScript, PCRE2 for PCRE -- rather
/// than against my own reading of the grammars. Each one is a place the parser was wrong.
/// </summary>
public sealed class RegexOracleAuditTests
{
    private static RegexParseOptions Options(RegexFlavor flavor, RegexPatternOptions options = RegexPatternOptions.None) =>
        new(flavor) { PatternOptions = options };

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
