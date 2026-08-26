namespace Meziantou.Framework.Language.Regex.Tests;

/// <summary>
/// The shape of the tree the .NET parser builds. Shape has to be asserted separately from round-tripping, because a
/// pattern kept entirely as skipped text round-trips perfectly while being structurally wrong.
/// </summary>
public sealed class NetParserTests
{
    private static T ParseSingleAtom<T>(string pattern)
        where T : RegexSyntaxNode
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);

        var term = Assert.Single(tree.Root.Alternation.Branches[0].Terms);

        return Assert.IsType<T>(term);
    }

    [Fact]
    public void ALiteralIsOneAtomPerCodeUnit()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("abc", RegexFlavor.Net);

        var terms = tree.Root.Alternation.Branches[0].Terms;
        Assert.Equal(3, terms.Count);
        Assert.Equal(['a', 'b', 'c'], terms.Cast<RegexLiteralSyntax>().Select(literal => literal.Value));
    }

    /// <summary>
    /// A quantifier binds one UTF-16 code unit, so in an emoji followed by <c>*</c> it applies to the low surrogate
    /// alone. That is what the engine does, and a tree that grouped the pair would describe a different pattern.
    /// </summary>
    [Fact]
    public void AQuantifierBindsOneCodeUnitOfASurrogatePair()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("\U0001F600*", RegexFlavor.Net);

        var terms = tree.Root.Alternation.Branches[0].Terms;
        Assert.Equal(2, terms.Count);
        Assert.IsType<RegexLiteralSyntax>(terms[0]);
        Assert.IsType<RegexQuantifiedSyntax>(terms[1]);
    }

    [Fact]
    public void ADotIsItsOwnAtom() => ParseSingleAtom<RegexAnyCharacterSyntax>(".");

    [Theory]
    [InlineData("^", RegexAnchorKind.Caret)]
    [InlineData("$", RegexAnchorKind.Dollar)]
    [InlineData(@"\A", RegexAnchorKind.StartOfInput)]
    [InlineData(@"\Z", RegexAnchorKind.EndOfInputBeforeFinalLineBreak)]
    [InlineData(@"\z", RegexAnchorKind.EndOfInput)]
    [InlineData(@"\G", RegexAnchorKind.ContiguousMatch)]
    [InlineData(@"\b", RegexAnchorKind.WordBoundary)]
    [InlineData(@"\B", RegexAnchorKind.NonWordBoundary)]
    public void AnAnchorReportsItsKind(string pattern, RegexAnchorKind kind)
    {
        Assert.Equal(kind, ParseSingleAtom<RegexAnchorSyntax>(pattern).AnchorKind);
    }

    [Theory]
    [InlineData(@"\d", false)]
    [InlineData(@"\D", true)]
    [InlineData(@"\w", false)]
    [InlineData(@"\S", true)]
    public void AShorthandClassReportsWhetherItIsNegated(string pattern, bool negated)
    {
        Assert.Equal(negated, ParseSingleAtom<RegexCharacterClassEscapeSyntax>(pattern).IsNegated);
    }

    [Theory]
    [InlineData(@"\n", "\n")]
    [InlineData(@"\t", "\t")]
    [InlineData(@"\x41", "A")]
    [InlineData(@"\u0041", "A")]
    [InlineData(@"\101", "A")]
    [InlineData(@"\cA", "\u0001")]
    [InlineData(@"\ca", "\u0001")]
    [InlineData(@"\e", "\u001b")]
    [InlineData(@"\.", ".")]
    public void AnEscapeReportsTheCharacterItStandsFor(string pattern, string value)
    {
        Assert.Equal(value, ParseSingleAtom<RegexCharacterEscapeSyntax>(pattern).Value);
    }

    [Theory]
    [InlineData(@"\p{L}", "L", false)]
    [InlineData(@"\P{Lu}", "Lu", true)]
    [InlineData(@"\p{IsGreek}", "IsGreek", false)]
    public void AUnicodeCategoryReportsItsNameAndNegation(string pattern, string name, bool negated)
    {
        var category = ParseSingleAtom<RegexUnicodeCategorySyntax>(pattern);

        Assert.Equal(name, category.Name);
        Assert.Equal(negated, category.IsNegated);
    }

    [Theory]
    [InlineData("(a)", typeof(RegexCapturingGroupSyntax))]
    [InlineData("(?:a)", typeof(RegexNonCapturingGroupSyntax))]
    [InlineData("(?>a)", typeof(RegexAtomicGroupSyntax))]
    [InlineData("(?<n>a)", typeof(RegexNamedGroupSyntax))]
    [InlineData("(?'n'a)", typeof(RegexNamedGroupSyntax))]
    [InlineData("(?=a)", typeof(RegexLookaroundSyntax))]
    [InlineData("(?!a)", typeof(RegexLookaroundSyntax))]
    [InlineData("(?<=a)", typeof(RegexLookaroundSyntax))]
    [InlineData("(?<!a)", typeof(RegexLookaroundSyntax))]
    [InlineData("(?i:a)", typeof(RegexOptionsGroupSyntax))]
    public void AGroupHeaderSelectsTheNodeType(string pattern, Type expected)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);

        var group = Assert.Single(tree.Root.DescendantNodes().OfType<RegexGroupSyntax>());
        Assert.IsType(expected, group);
    }

    [Theory]
    [InlineData("(?=a)", RegexLookaroundKind.PositiveLookahead)]
    [InlineData("(?!a)", RegexLookaroundKind.NegativeLookahead)]
    [InlineData("(?<=a)", RegexLookaroundKind.PositiveLookbehind)]
    [InlineData("(?<!a)", RegexLookaroundKind.NegativeLookbehind)]
    public void ALookaroundReportsItsDirectionAndPolarity(string pattern, RegexLookaroundKind kind)
    {
        var lookaround = ParseSingleAtom<RegexLookaroundSyntax>(pattern);

        Assert.Equal(kind, lookaround.LookaroundKind);
        Assert.Equal(kind is RegexLookaroundKind.PositiveLookbehind or RegexLookaroundKind.NegativeLookbehind, lookaround.IsLookbehind);
        Assert.Equal(kind is RegexLookaroundKind.NegativeLookahead or RegexLookaroundKind.NegativeLookbehind, lookaround.IsNegative);
    }

    [Fact]
    public void ABalancingGroupReportsBothNames()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(?<a>x)(?<b-a>y)", RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);

        var balancing = Assert.Single(tree.Root.DescendantNodes().OfType<RegexBalancingGroupSyntax>());
        Assert.Equal("b", balancing.Name);
        Assert.Equal("a", balancing.PreviousName);
    }

    [Fact]
    public void ABalancingGroupMayOnlyPop()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(a)(?<-1>b)", RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);

        var balancing = Assert.Single(tree.Root.DescendantNodes().OfType<RegexBalancingGroupSyntax>());
        Assert.Equal("", balancing.Name);
        Assert.Equal("1", balancing.PreviousName);
    }

    [Fact]
    public void ANumberedBackreferenceReportsItsGroup()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(@"(a)\1", RegexFlavor.Net);

        var backreference = Assert.Single(tree.Root.DescendantNodes().OfType<RegexBackreferenceSyntax>());
        Assert.Equal(1, backreference.Number);
    }

    /// <summary>
    /// With fewer than ten groups the engine reads <c>\10</c> as the octal escape for a backspace rather than as a
    /// reference to a group that does not exist. Both branches of that asymmetry are load-bearing.
    /// </summary>
    [Fact]
    public void TenIsAnOctalEscapeUntilThereAreTenGroups()
    {
        var withoutGroups = RegexSyntaxAssert.TextIsFaithful(@"\10", RegexFlavor.Net);
        Assert.Empty(withoutGroups.Diagnostics);
        Assert.Single(withoutGroups.Root.DescendantNodes().OfType<RegexCharacterEscapeSyntax>());

        var withGroups = RegexSyntaxAssert.TextIsFaithful(@"(a)(b)(c)(d)(e)(f)(g)(h)(i)(j)\10", RegexFlavor.Net);
        Assert.Empty(withGroups.Diagnostics);
        Assert.Equal(10, Assert.Single(withGroups.Root.DescendantNodes().OfType<RegexBackreferenceSyntax>()).Number);
    }

    [Theory]
    [InlineData(@"(?<n>a)\k<n>")]
    [InlineData(@"(?<n>a)\k'n'")]
    [InlineData(@"(?<n>a)\<n>")]
    public void ANamedBackreferenceReportsItsName(string pattern)
    {
        var tree = RegexSyntaxAssert.TextIsFaithful(pattern, RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);

        Assert.Equal("n", Assert.Single(tree.Root.DescendantNodes().OfType<RegexNamedBackreferenceSyntax>()).Name);
    }

    [Fact]
    public void AConditionalOnAGroupReferenceKeepsTheReferenceAndBothBranches()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(x)(?(1)a|b)", RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);

        var conditional = Assert.Single(tree.Root.DescendantNodes().OfType<RegexConditionalSyntax>());
        var reference = Assert.IsType<RegexConditionalReferenceSyntax>(conditional.Condition);
        Assert.Equal("1", reference.Name);
        Assert.Equal(2, conditional.Alternation.Branches.Count);
    }

    /// <summary>A name that is not a group makes the condition an expression to match rather than a reference.</summary>
    [Fact]
    public void AConditionOnAnUndefinedNameIsAnExpression()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(?(foo)a|b)", RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);

        var conditional = Assert.Single(tree.Root.DescendantNodes().OfType<RegexConditionalSyntax>());
        Assert.IsNotType<RegexConditionalReferenceSyntax>(conditional.Condition);
    }

    [Fact]
    public void AConditionalConditionDoesNotTakeACaptureNumber()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("(?(foo)a|b)(x)", RegexFlavor.Net);

        var capture = Assert.Single(tree.Captures);
        Assert.Equal(1, capture.Number);
    }

    [Fact]
    public void ACharacterClassKeepsItsMembersAndItsNegation()
    {
        var characterClass = ParseSingleAtom<RegexCharacterClassSyntax>(@"[^a-z\d.]");

        Assert.True(characterClass.IsNegated);
        Assert.Equal(3, characterClass.Members.Count);
        Assert.IsType<RegexCharacterRangeSyntax>(characterClass.Members[0]);
        Assert.IsType<RegexCharacterClassEscapeSyntax>(characterClass.Members[1]);

        // A dot is an ordinary character inside a class.
        Assert.IsType<RegexLiteralSyntax>(characterClass.Members[2]);
    }

    [Fact]
    public void AClosingBracketIsLiteralWhenItIsTheFirstMember()
    {
        var characterClass = ParseSingleAtom<RegexCharacterClassSyntax>("[]]");

        var member = Assert.Single(characterClass.Members);
        Assert.Equal(']', Assert.IsType<RegexLiteralSyntax>(member).Value);
    }

    [Fact]
    public void AShorthandClassNeverStartsARange()
    {
        var characterClass = ParseSingleAtom<RegexCharacterClassSyntax>(@"[\d-z]");

        Assert.Equal(3, characterClass.Members.Count);
        Assert.Empty(characterClass.Members.OfType<RegexCharacterRangeSyntax>());
    }

    [Fact]
    public void ASubtractionMayFollowARangeDirectly()
    {
        var characterClass = ParseSingleAtom<RegexCharacterClassSyntax>("[a-[b]]");

        // The dash was claimed by the look-ahead that started the range, so "a" is a plain member and the dash belongs
        // to the subtraction.
        Assert.Equal(2, characterClass.Members.Count);
        Assert.IsType<RegexLiteralSyntax>(characterClass.Members[0]);
        Assert.IsType<RegexClassSubtractionSyntax>(characterClass.Members[1]);
    }

    [Fact]
    public void ASubtractionMayHaveADashOfItsOwn()
    {
        var characterClass = ParseSingleAtom<RegexCharacterClassSyntax>("[a-z-[aeiou]]");

        Assert.Equal(2, characterClass.Members.Count);
        Assert.IsType<RegexCharacterRangeSyntax>(characterClass.Members[0]);
        var subtraction = Assert.IsType<RegexClassSubtractionSyntax>(characterClass.Members[1]);
        Assert.Equal(5, subtraction.Subtracted.Members.Count);
    }

    [Fact]
    public void SubtractionsNest()
    {
        var characterClass = ParseSingleAtom<RegexCharacterClassSyntax>("[a-z-[b-[c]]]");

        var subtraction = Assert.IsType<RegexClassSubtractionSyntax>(characterClass.Members[1]);
        Assert.Single(subtraction.Subtracted.Members.OfType<RegexClassSubtractionSyntax>());
    }

    [Fact]
    public void AnEscapedDashIsAMemberRatherThanARangeStart()
    {
        var characterClass = ParseSingleAtom<RegexCharacterClassSyntax>(@"[a\-z]");

        Assert.Equal(3, characterClass.Members.Count);
        Assert.Empty(characterClass.Members.OfType<RegexCharacterRangeSyntax>());
    }

    [Fact]
    public void ABraceThatDoesNotOpenABoundIsAnOrdinaryCharacter()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("a{a}", RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);

        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexQuantifiedSyntax>());
        Assert.Equal(4, tree.Root.Alternation.Branches[0].Terms.Count);
    }

    [Fact]
    public void AnEmptyBranchIsStillABranch()
    {
        var tree = RegexSyntaxAssert.TextIsFaithful("a||b", RegexFlavor.Net);

        Assert.Equal(3, tree.Root.Alternation.Branches.Count);
        Assert.Empty(tree.Root.Alternation.Branches[1].Terms);
    }
}
