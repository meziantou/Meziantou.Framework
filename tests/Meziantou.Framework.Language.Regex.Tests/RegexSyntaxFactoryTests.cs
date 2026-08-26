namespace Meziantou.Framework.Language.Regex.Tests;

public sealed class RegexSyntaxFactoryTests
{
    [Theory]
    [InlineData('a', "a")]
    [InlineData('0', "0")]
    [InlineData('.', @"\.")]
    [InlineData('*', @"\*")]
    [InlineData('(', @"\(")]
    [InlineData('[', @"\[")]
    [InlineData(']', @"\]")]
    [InlineData('\\', @"\\")]
    [InlineData('|', @"\|")]
    [InlineData('^', @"\^")]
    [InlineData('$', @"\$")]
    [InlineData('-', @"\-")]
    [InlineData('#', @"\#")]
    [InlineData(' ', @"\ ")]
    public void Literal_EscapesWhatHasToBeEscaped(char value, string expected)
    {
        Assert.Equal(expected, SyntaxFactory.Literal(value, RegexFlavor.Net).ToFullString());
    }

    /// <summary>What the factory builds has to parse back to what it says it is.</summary>
    [Fact]
    public void LiteralText_ProducesAPatternThatMatchesTheTextItself()
    {
        const string Text = @"a.b*c(d)[e]{f}|g^h$i\j#k l-m";

        var built = SyntaxFactory.LiteralText(Text, RegexFlavor.Net).ToFullString();
        var tree = RegexSyntaxAssert.TextIsFaithful(built, RegexFlavor.Net);

        Assert.Empty(tree.Diagnostics);
        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexQuantifiedSyntax>());
        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexGroupSyntax>());
        Assert.Empty(tree.Root.DescendantNodes().OfType<RegexCharacterClassSyntax>());
    }

    [Fact]
    public void Group_WrapsItsBody()
    {
        var group = SyntaxFactory.Group(SyntaxFactory.Alternation(SyntaxFactory.LiteralText("ab", RegexFlavor.Net)));

        Assert.Equal("(ab)", group.ToFullString());
    }

    [Fact]
    public void Alternation_InsertsOneBarBetweenBranches()
    {
        var alternation = SyntaxFactory.Alternation(
            SyntaxFactory.LiteralText("a", RegexFlavor.Net),
            SyntaxFactory.LiteralText("b", RegexFlavor.Net),
            SyntaxFactory.LiteralText("c", RegexFlavor.Net));

        Assert.Equal("a|b|c", alternation.ToFullString());
    }

    [Theory]
    [InlineData('*', RegexQuantifierMode.Greedy, "a*")]
    [InlineData('+', RegexQuantifierMode.Lazy, "a+?")]
    [InlineData('?', RegexQuantifierMode.Possessive, "a?+")]
    public void Quantified_WritesTheOperatorAndItsModifier(char quantifier, RegexQuantifierMode mode, string expected)
    {
        var atom = (RegexAtomSyntax)SyntaxFactory.Literal('a', RegexFlavor.Net);

        Assert.Equal(expected, SyntaxFactory.Quantified(atom, quantifier, mode).ToFullString());
    }

    [Theory]
    [InlineData(2, 2, "a{2}")]
    [InlineData(2, null, "a{2,}")]
    [InlineData(2, 5, "a{2,5}")]
    public void Quantified_WritesBounds(int min, int? max, string expected)
    {
        var atom = (RegexAtomSyntax)SyntaxFactory.Literal('a', RegexFlavor.Net);

        var built = SyntaxFactory.Quantified(atom, min, max).ToFullString();
        Assert.Equal(expected, built);

        var tree = RegexSyntaxAssert.TextIsFaithful(built, RegexFlavor.Net);
        var quantified = Assert.IsType<RegexQuantifiedSyntax>(tree.Root.Alternation.Branches[0].Terms[0]);
        Assert.Equal(min, quantified.Quantifier.MinCount);
        Assert.Equal(max, quantified.Quantifier.MaxCount);
    }

    [Fact]
    public void CharacterClass_WritesItsMembers()
    {
        var characterClass = SyntaxFactory.CharacterClass(
            negated: true,
            SyntaxFactory.CharacterRange('a', 'z', RegexFlavor.Net),
            SyntaxFactory.ClassEscape('d'));

        var built = characterClass.ToFullString();
        Assert.Equal(@"[^a-z\d]", built);

        var tree = RegexSyntaxAssert.TextIsFaithful(built, RegexFlavor.Net);
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void Anchor_WritesEveryKind()
    {
        foreach (var kind in Enum.GetValues<RegexAnchorKind>())
        {
            var built = SyntaxFactory.Anchor(kind).ToFullString();
            var tree = RegexSyntaxAssert.TextIsFaithful(built, RegexFlavor.PcrePerl);

            var anchor = Assert.Single(tree.Root.DescendantNodes().OfType<RegexAnchorSyntax>());
            Assert.Equal(kind, anchor.AnchorKind);
        }
    }

    /// <summary>
    /// In a basic expression the escaped form is the construct, so escaping a parenthesis there would open a group
    /// rather than match one. The literal is the bare character.
    /// </summary>
    [Theory]
    [InlineData('(', "(")]
    [InlineData(')', ")")]
    [InlineData('{', "{")]
    [InlineData('}', "}")]
    [InlineData('+', "+")]
    [InlineData('?', "?")]
    [InlineData('|', "|")]
    [InlineData('.', @"\.")]
    [InlineData('*', @"\*")]
    [InlineData('[', @"\[")]
    public void PosixBasicLeavesTheEscapedDelimitersBare(char value, string expected)
    {
        Assert.Equal(expected, SyntaxFactory.Literal(value, RegexFlavor.PosixBasic).ToFullString());
    }

    [Theory]
    [InlineData('(', @"\(")]
    [InlineData('{', @"\{")]
    [InlineData('+', @"\+")]
    [InlineData('|', @"\|")]
    public void PosixExtendedEscapesTheCharactersThatAreConstructsThere(char value, string expected)
    {
        Assert.Equal(expected, SyntaxFactory.Literal(value, RegexFlavor.PosixExtended).ToFullString());
    }

    /// <summary>Whatever the factory escapes has to parse back as a literal, in the flavor it was built for.</summary>
    [Theory]
    [InlineData("net")]
    [InlineData("javascript")]
    [InlineData("pcre")]
    [InlineData("ere")]
    [InlineData("bre")]
    public void AnEscapedLiteralParsesBackAsOneAtomInItsOwnFlavor(string flavorName)
    {
        Assert.True(RegexFlavor.TryParse(flavorName, out var flavor));

        foreach (var value in @"a1 .*+?[]{}()|^$\-#/<>=!:'")
        {
            var built = SyntaxFactory.Literal(value, flavor).ToFullString();
            var tree = RegexSyntaxAssert.TextIsFaithful(built, flavor);

            Assert.Empty(tree.Diagnostics, $"[{built}] built for {value} in {flavorName}");
            Assert.Empty(tree.Root.DescendantNodes().OfType<RegexQuantifiedSyntax>());
            Assert.Empty(tree.Root.DescendantNodes().OfType<RegexGroupSyntax>());
            Assert.Empty(tree.Root.DescendantNodes().OfType<RegexCharacterClassSyntax>());
            Assert.Single(tree.Root.Alternation.Branches[0].Terms);
        }
    }

    [Fact]
    public void Quantified_RejectsAnOperatorThatIsNotOne()
    {
        var literal = (RegexAtomSyntax)SyntaxFactory.Literal('a', RegexFlavor.Net);

        Assert.Throws<ArgumentOutOfRangeException>(() => SyntaxFactory.Quantified(literal, 'x'));
    }
}
