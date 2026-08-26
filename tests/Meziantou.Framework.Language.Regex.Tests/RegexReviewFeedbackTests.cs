namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// One test per defect found in review, so each stays fixed.
/// </summary>
public sealed class RegexReviewFeedbackTests
{
    // ---- caller-owned collections must not reach into the tree ----

    /// <summary>
    /// A node measures its text and spans once, at construction. Holding the caller's list would let them change what
    /// the tree reports without changing the text those spans describe.
    /// </summary>
    [Fact]
    public void MutatingTheListPassedToASequenceDoesNotChangeTheNode()
    {
        var terms = new List<RegexTermSyntax> { SyntaxFactory.Literal('a', RegexFlavor.Net) };
        var sequence = new RegexSequenceSyntax(terms);

        terms.Add(SyntaxFactory.Literal('b', RegexFlavor.Net));

        Assert.Single(sequence.Terms);
        Assert.Equal("a", sequence.ToFullString());
    }

    [Fact]
    public void MutatingTheListsPassedToAnAlternationDoesNotChangeTheNode()
    {
        var branches = new List<RegexSequenceSyntax> { SyntaxFactory.LiteralText("a", RegexFlavor.Net) };
        var bars = new List<RegexSyntaxToken>();
        var alternation = new RegexAlternationSyntax(branches, bars);

        branches.Add(SyntaxFactory.LiteralText("b", RegexFlavor.Net));
        bars.Add(new RegexSyntaxToken(RegexSyntaxKind.BarToken, "|"));

        Assert.Single(alternation.Branches);
        Assert.Empty(alternation.BarTokens);
        Assert.Equal("a", alternation.ToFullString());
    }

    [Fact]
    public void MutatingTheListPassedToACharacterClassDoesNotChangeTheNode()
    {
        var members = new List<RegexSyntaxNode> { SyntaxFactory.Literal('a', RegexFlavor.Net) };
        var characterClass = SyntaxFactory.CharacterClass(negated: false, members);

        members.Add(SyntaxFactory.Literal('b', RegexFlavor.Net));

        Assert.Single(characterClass.Members);
        Assert.Equal("[a]", characterClass.ToFullString());
    }

    [Fact]
    public void MutatingTheListPassedToSkippedTextDoesNotChangeTheNode()
    {
        var tokens = new List<RegexSyntaxToken> { new(RegexSyntaxKind.BadToken, "a") };
        var skipped = new RegexSkippedTextSyntax(tokens);

        tokens.Add(new RegexSyntaxToken(RegexSyntaxKind.BadToken, "b"));

        Assert.Single(skipped.Tokens);
        Assert.Equal("a", skipped.ToFullString());
    }

    [Fact]
    public void MutatingTheTriviaListPassedToATokenDoesNotChangeItsTextOrSpans()
    {
        var leading = new List<RegexSyntaxTrivia> { new(RegexSyntaxKind.WhitespaceTrivia, " ") };
        var token = new RegexSyntaxToken(RegexSyntaxKind.LiteralToken, "a", leadingTrivia: leading);
        var before = token.FullSpan;

        leading.Add(new RegexSyntaxTrivia(RegexSyntaxKind.WhitespaceTrivia, "   "));

        Assert.Equal(" a", token.ToFullString());
        Assert.Equal(before, token.FullSpan);
    }

    // ---- traversal order ----

    /// <summary>
    /// A rewriter that keeps state, such as one replacing only the first match, has to see the tree in the order the
    /// traversal APIs report it rather than back to front.
    /// </summary>
    [Fact]
    public void TheRewriterVisitsNodesInSourceOrder()
    {
        var tree = RegexSyntaxTree.ParseText("aaa", RegexFlavor.Net);

        var rewritten = new ReplaceFirstLiteral('a', 'z').Visit(tree.Root);

        Assert.Equal("zaa", rewritten?.ToFullString());
    }

    // ---- options validation ----

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    public void ARecursionDepthBelowOneIsRejectedWhereItIsSet(int depth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RegexParseOptions(RegexFlavor.Net) { MaxRecursionDepth = depth });
    }

    // ---- equivalence ----

    /// <summary>
    /// The same characters read with and without extended mode are different trees: a space is a term in one and
    /// trivia in the other. Comparing the text alone would call them equivalent.
    /// </summary>
    [Fact]
    public void TwoTreesWithTheSameTextButDifferentOptionsAreNotEquivalent()
    {
        var plain = RegexSyntaxTree.ParseText("a b", RegexFlavor.Net);
        var extended = RegexSyntaxTree.ParseText("a b", new RegexParseOptions(RegexFlavor.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace });

        Assert.False(plain.IsEquivalentTo(extended));
        Assert.False(extended.IsEquivalentTo(plain));
    }

    // ---- flavor gating of group headers ----

    [Theory]
    [InlineData("javascript", "(?>a)")]
    [InlineData("javascript", "(?'n'a)")]
    [InlineData("javascript", "(?<a-b>x)")]
    [InlineData("javascript", "(?(1)a|b)")]
    [InlineData("javascript", "(?i)a")]
    [InlineData("javascript", "(?i:a)")]
    [InlineData("pcre", "(?<a-b>x)")]
    [InlineData("ere", "(?:a)")]
    [InlineData("ere", "(?=a)")]
    public void AGroupHeaderTheFlavorLacksIsReported(string flavorName, string pattern)
    {
        Assert.True(RegexFlavor.TryParse(flavorName, out var flavor));

        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, flavor);

        Assert.NotEmpty(tree.Diagnostics, $"[{pattern}] should not be accepted by {flavorName}");
    }

    [Theory]
    [InlineData("(?>a)", typeof(RegexAtomicGroupSyntax))]
    [InlineData("(?|a|b)", typeof(RegexBranchResetGroupSyntax))]
    [InlineData("(?P<n>a)", typeof(RegexNamedGroupSyntax))]
    [InlineData("(?R)", typeof(RegexRecursionSyntax))]
    [InlineData("(a)(?1)", typeof(RegexRecursionSyntax))]
    [InlineData("(*SKIP)a", typeof(RegexBacktrackingVerbSyntax))]
    public void PcreConstructsAreTheirOwnNodeTypes(string pattern, Type expected)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.PcrePerl);

        Assert.Contains(tree.Root.DescendantNodes(), expected.IsInstanceOfType, $"[{pattern}] produced no {expected.Name}");
    }

    [Theory]
    [InlineData(@"\Qa+b\E", "a+b")]
    [InlineData(@"\Qa+b", "a+b")]
    [InlineData(@"\Q\E", "")]
    public void AQuotedLiteralKeepsItsTextAndDelimiters(string pattern, string value)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.PcrePerl);

        var quoted = Assert.Single(tree.Root.DescendantNodes().OfType<RegexQuotedLiteralSyntax>());
        Assert.Equal(value, quoted.Value);
    }

    // ---- JavaScript ----

    /// <summary>In ECMAScript <c>[^]</c> is an empty negated class, which is how "any character" is written.</summary>
    [Fact]
    public void JavaScriptReadsAnEmptyNegatedClass()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("[^]", RegexFlavor.JavaScript);

        Assert.Empty(tree.Diagnostics);
        var characterClass = Assert.Single(tree.Root.DescendantNodes().OfType<RegexCharacterClassSyntax>());
        Assert.True(characterClass.IsNegated);
        Assert.Empty(characterClass.Members);
    }

    [Theory]
    [InlineData("/a/gg", "REGEX0206")]
    [InlineData("/a/gimgim", "REGEX0206")]
    [InlineData("/a/uv", "REGEX0207")]
    [InlineData("/a/q", "REGEX0205")]
    public void AnInvalidFlagListIsReported(string literal, string id)
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(literal);
        RegexSyntaxAssert.TextIsFaithful(literal, tree);

        Assert.Contains(tree.Diagnostics, d => d.Id == id, $"[{literal}] reported {string.Join(",", tree.Diagnostics.Select(d => d.Id))}");
    }

    [Theory]
    [InlineData("/a/gi")]
    [InlineData("/a/u")]
    [InlineData("/a/v")]
    [InlineData("/a/dgimsuy")]
    public void AValidFlagListIsAccepted(string literal)
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(literal);
        RegexSyntaxAssert.TextIsFaithful(literal, tree);

        Assert.Empty(tree.Diagnostics);
    }

    /// <summary>
    /// In Unicode mode a pattern is a sequence of code points, so a quantifier after an emoji repeats the whole
    /// character rather than the low half of it.
    /// </summary>
    [Fact]
    public void UnicodeModeMakesASurrogatePairOneAtom()
    {
        var withoutFlag = RegexSyntaxTree.ParseJavaScriptLiteral("/\U0001F600*/");
        Assert.Equal(2, withoutFlag.Root.Alternation.Branches[0].Terms.Count);

        var withFlag = RegexSyntaxTree.ParseJavaScriptLiteral("/\U0001F600*/u");
        RegexSyntaxAssert.TextIsFaithful("/\U0001F600*/u", withFlag);

        var term = Assert.Single(withFlag.Root.Alternation.Branches[0].Terms);
        var quantified = Assert.IsType<RegexQuantifiedSyntax>(term);
        var literal = Assert.IsType<RegexLiteralSyntax>(quantified.Term);
        Assert.Equal(0x1F600, literal.CodePoint);
    }

    /// <summary>
    /// A literal lives on one line: the grammar excludes a line terminator from an ordinary character and from the
    /// character after a backslash, so neither spelling can hide one.
    /// </summary>
    [Theory]
    [InlineData("/a\nb/")]
    [InlineData("/a\rb/")]
    [InlineData("/a\u2028b/")]
    [InlineData("/a\u2029b/")]
    [InlineData("/a\\\nb/")]
    public void ALiteralCannotContainALineTerminator(string literal)
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral(literal);
        RegexSyntaxAssert.TextIsFaithful(literal, tree);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0208", $"[{literal}] reported {string.Join(",", tree.Diagnostics.Select(d => d.Id))}");
        Assert.False(tree.Root.IsJavaScriptLiteral && tree.Root.CloseSlashToken is not null);
    }

    /// <summary>An edit to a literal has to stay a literal, or the delimiters become ordinary slashes.</summary>
    [Fact]
    public void EditingALiteralKeepsItALiteral()
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral("/ab/gi");

        var updated = tree.WithChanges(new RegexTextChange(new TextSpan(3, 0), "c"));

        Assert.Equal("/abc/gi", updated.Text);
        Assert.True(updated.Root.IsJavaScriptLiteral);
        Assert.Equal("gi", updated.Root.FlagsToken?.Text);
        Assert.Empty(updated.Diagnostics);
    }

    [Fact]
    public void ReplacingANodeInsideALiteralKeepsItALiteral()
    {
        var tree = RegexSyntaxTree.ParseJavaScriptLiteral("/ab/gi");
        var first = tree.Root.DescendantNodes().OfType<RegexLiteralSyntax>().First();

        var updated = tree.Root.ReplaceNode(first, SyntaxFactory.Literal('z', RegexFlavor.JavaScript));

        Assert.Equal("/zb/gi", updated.ToFullString());
        Assert.True(updated.IsJavaScriptLiteral);
        Assert.Equal("gi", updated.FlagsToken?.Text);
    }

    // ---- Unicode property names ----

    /// <summary>
    /// The known-name set is .NET's own. Flavors that name a property as well as a value have a different and larger
    /// one, so checking a name against the .NET table there would reject properties they really do have.
    /// </summary>
    [Theory]
    [InlineData("pcre")]
    [InlineData("javascript")]
    public void APropertyEqualsValueNameIsAcceptedWhereTheFlavorHasIt(string flavorName)
    {
        Assert.True(RegexFlavor.TryParse(flavorName, out var flavor));
        var options = new RegexParseOptions(flavor) { PatternOptions = RegexPatternOptions.Unicode };

        var tree = RegexSyntaxAssert.TextIsFaithful(@"\p{Script=Greek}", options);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal("Script=Greek", Assert.Single(tree.Root.DescendantNodes().OfType<RegexUnicodeCategorySyntax>()).Name);
    }

    /// <summary>
    /// .NET names categories and blocks but not properties, so it stops at the "=" and calls the escape incomplete --
    /// which is exactly what its own engine reports for this pattern.
    /// </summary>
    [Fact]
    public void APropertyEqualsValueNameIsNotAcceptedForNet()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"\p{Script=Greek}", RegexFlavor.Net);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0032");
    }

    [Fact]
    public void AnUnknownCategoryNameIsStillReportedForNet()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"\p{Bogus}", RegexFlavor.Net);

        Assert.Contains(tree.Diagnostics, d => d.Id == "REGEX0034");
    }

    /// <summary>A category escape is not one where the flavor does not have it.</summary>
    [Theory]
    [InlineData("javascript")]
    [InlineData("ere")]
    public void ALowercaseCategoryEscapeIsNotOneWhereTheFlavorLacksIt(string flavorName)
    {
        Assert.True(RegexFlavor.TryParse(flavorName, out var flavor));

        foreach (var pattern in new[] { @"\p{L}", @"[\p{L}]" })
        {
            var tree = RegexSyntaxAssert.TextIsFaithful(pattern, flavor);

            Assert.Empty(tree.Root.DescendantNodes().OfType<RegexUnicodeCategorySyntax>());
        }
    }

    private sealed class ReplaceFirstLiteral(char from, char to) : RegexSyntaxRewriter
    {
        private bool _done;

        public override RegexSyntaxNode? VisitLiteral(RegexLiteralSyntax node)
        {
            if (_done || node.Value != from)
                return base.VisitLiteral(node);

            _done = true;

            return new RegexLiteralSyntax(node.LiteralToken.WithText(to.ToString()));
        }
    }
}
