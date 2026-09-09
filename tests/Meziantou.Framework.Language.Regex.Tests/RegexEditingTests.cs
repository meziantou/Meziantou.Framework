namespace Meziantou.Framework.Language.Regex.Tests;

public sealed class RegexEditingTests
{
    [Fact]
    public void ReplaceNode_SwapsANodeAndKeepsEverythingElse()
    {
        var tree = RegexSyntaxTree.ParseText("a|b|c", RegexDialect.Net);
        var middle = tree.GetRoot().Alternation.Branches[1];

        var updated = tree.GetRoot().ReplaceNode(middle, SyntaxFactory.LiteralText("xy", RegexDialect.Net));

        Assert.Equal("a|xy|c", updated.ToFullString());
    }

    [Fact]
    public void ReplaceToken_SwapsATokenAndKeepsEverythingElse()
    {
        var tree = RegexSyntaxTree.ParseText("ab", RegexDialect.Net);
        var first = tree.GetRoot().DescendantTokens().First(token => token.Text == "a");

        var updated = tree.GetRoot().ReplaceToken(first, SyntaxFactory.Token(SyntaxKind.LiteralToken, "z").WithTriviaFrom(first));

        Assert.Equal("zb", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_CanCarryOverTheTriviaOfTheNodeItReplaces()
    {
        var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
        var tree = RegexSyntaxTree.ParseText("a   b # note\n", options);
        var second = tree.GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().Last();

        var updated = tree.GetRoot().ReplaceNode(second, SyntaxFactory.Literal('z', RegexDialect.Net).WithTriviaFrom(second));

        Assert.Equal("a   z # note\n", updated.ToFullString());
    }

    [Fact]
    public void ReplaceTrivia_SwapsACommentAndKeepsEverythingElse()
    {
        var tree = RegexSyntaxTree.ParseText("a(?#note)b", RegexDialect.Net);
        var comment = Assert.Single(tree.GetRoot().DescendantComments());

        var updated = tree.GetRoot().ReplaceTrivia(comment, SyntaxFactory.Trivia(comment.Kind(), "(?#other)"));

        Assert.Equal("a(?#other)b", updated.ToFullString());
    }

    [Fact]
    public void WithChanges_ReparsesInTheSameDialect()
    {
        var tree = RegexSyntaxTree.ParseText("a*", RegexDialect.PcrePerl);

        var updated = tree.WithChanges(new TextChange(new TextSpan(2, 0), "+"));

        Assert.Equal("a*+", updated.GetText().Text);
        Assert.Equal(RegexDialect.PcrePerl, updated.Dialect);
        Assert.Empty(updated.GetDiagnostics());
    }

    [Fact]
    public void WithChanges_AppliesSeveralEditsFromTheEndBackwards()
    {
        var tree = RegexSyntaxTree.ParseText("abc", RegexDialect.Net);

        var updated = tree.WithChanges(
            new TextChange(new TextSpan(0, 1), "x"),
            new TextChange(new TextSpan(2, 1), "z"));

        Assert.Equal("xbz", updated.GetText().Text);
    }

    [Fact]
    public void GetChanges_ReportsNothingForAnIdenticalTree()
    {
        var tree = RegexSyntaxTree.ParseText("a+", RegexDialect.Net);
        var same = RegexSyntaxTree.ParseText("a+", RegexDialect.Net);

        Assert.Empty(tree.GetChanges(same));
    }

    [Fact]
    public void GetChanges_DoesNotSplitASurrogatePair()
    {
        var before = RegexSyntaxTree.ParseText("a\U0001F600b", RegexDialect.Net);
        var after = RegexSyntaxTree.ParseText("a\U0001F601b", RegexDialect.Net);

        var change = Assert.Single(after.GetChanges(before));
        Assert.Equal(1, change.Span.Start);
        Assert.Equal(2, change.Span.Length);
    }

    [Fact]
    public void Rewriter_ReplacesEveryMatchingNodeAndKeepsTheRest()
    {
        var tree = RegexSyntaxTree.ParseText("a(?#note)b|a", RegexDialect.Net);

        var rewritten = new LiteralRenamer('a', 'z').Visit(tree.GetRoot());

        Assert.Equal("z(?#note)b|z", rewritten?.ToFullString());
    }

    [Fact]
    public void Rewriter_ReturnsTheSameInstanceWhenNothingChanges()
    {
        var tree = RegexSyntaxTree.ParseText("xyz", RegexDialect.Net);

        var rewritten = new LiteralRenamer('a', 'z').Visit(tree.GetRoot());

        Assert.Same(tree.GetRoot(), rewritten);
    }

    private sealed class LiteralRenamer(char from, char to) : RegexSyntaxRewriter
    {
        public override SyntaxNode? VisitLiteral(RegexLiteralSyntax node)
        {
            if (node.Value != from)
                return base.VisitLiteral(node);

            return node.WithLiteralToken(SyntaxFactory.Token(SyntaxKind.LiteralToken, to.ToString()).WithTriviaFrom(node.LiteralToken));
        }
    }

    /// <summary>
    /// A sequence holds its terms in a plain list, with nothing between them, so removing one takes only that one.
    /// </summary>
    [Theory]
    [InlineData(0, "bc")]
    [InlineData(1, "ac")]
    [InlineData(2, "ab")]
    public void RemoveNode_FromASequence_TakesOnlyThatTerm(int index, string expected)
    {
        var tree = RegexSyntaxTree.ParseText("abc", RegexDialect.Net);
        var sequence = tree.GetRoot().DescendantNodes().OfType<RegexSequenceSyntax>().Single();
        var term = sequence.ChildNodes().ElementAt(index);

        var updated = tree.GetRoot().RemoveNode(term, SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal(expected, updated.ToFullString());
    }

    [Fact]
    public void InsertNodesAfter_InASequence_AddsNoSeparator()
    {
        var tree = RegexSyntaxTree.ParseText("ab", RegexDialect.Net);
        var sequence = tree.GetRoot().DescendantNodes().OfType<RegexSequenceSyntax>().Single();
        var term = RegexSyntaxTree.ParseText("x", RegexDialect.Net).GetRoot().DescendantNodes().OfType<RegexLiteralSyntax>().Single();

        var updated = tree.GetRoot().InsertNodesAfter(sequence.ChildNodes().First(), [term]);

        Assert.Equal("axb", updated.ToFullString());
    }

    /// <summary>The branches of an alternation are separated by <c>|</c>, so an edit has to keep them alternating.</summary>
    [Fact]
    public void RemoveNode_FromAnAlternation_TakesTheBarWithIt()
    {
        var tree = RegexSyntaxTree.ParseText("a|b|c", RegexDialect.Net);
        var branches = tree.GetRoot().Alternation.Branches;

        var updated = tree.GetRoot().RemoveNode(branches[1], SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("a|c", updated.ToFullString());
    }

    [Fact]
    public void InsertNodesAfter_InAnAlternationOfOneBranch_StillAddsTheBar()
    {
        var tree = RegexSyntaxTree.ParseText("a", RegexDialect.Net);
        var alternation = tree.GetRoot().Alternation;

        var updated = tree.GetRoot().InsertNodesAfter(alternation.Branches[0], [SyntaxFactory.LiteralText("b", RegexDialect.Net)]);

        Assert.Equal("a|b", updated.ToFullString());
    }
}
