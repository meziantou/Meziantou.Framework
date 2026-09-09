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
}
