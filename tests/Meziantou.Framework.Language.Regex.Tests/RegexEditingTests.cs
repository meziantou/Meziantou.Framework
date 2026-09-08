namespace Meziantou.Framework.Language.Regex.Tests;

public sealed class RegexEditingTests
{
    [Fact]
    public void ReplaceNode_SwapsANodeAndKeepsEverythingElse()
    {
        var tree = RegexSyntaxTree.ParseText("a|b|c", RegexDialect.Net);
        var middle = tree.Root.Alternation.Branches[1];

        var updated = tree.Root.ReplaceNode(middle, SyntaxFactory.LiteralText("xy", RegexDialect.Net));

        Assert.Equal("a|xy|c", updated.ToFullString());
    }

    [Fact]
    public void ReplaceToken_SwapsATokenAndKeepsEverythingElse()
    {
        var tree = RegexSyntaxTree.ParseText("ab", RegexDialect.Net);
        var first = tree.Root.DescendantTokens().First(token => token.Text == "a");

        var updated = tree.Root.ReplaceToken(first, first.WithText("z"));

        Assert.Equal("zb", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_KeepsTheTriviaInFrontOfTheNodeItReplaces()
    {
        var options = new RegexParseOptions(RegexDialect.Net) { PatternOptions = RegexPatternOptions.IgnorePatternWhitespace };
        var tree = RegexSyntaxTree.ParseText("a   b # note\n", options);
        var second = tree.Root.DescendantNodes().OfType<RegexLiteralSyntax>().Last();

        var updated = tree.Root.ReplaceNode(second, SyntaxFactory.Literal('z', RegexDialect.Net));

        Assert.Equal("a   z # note\n", updated.ToFullString());
    }

    [Fact]
    public void ReplaceTrivia_SwapsACommentAndKeepsEverythingElse()
    {
        var tree = RegexSyntaxTree.ParseText("a(?#note)b", RegexDialect.Net);
        var comment = Assert.Single(tree.Root.DescendantComments());

        var updated = tree.Root.ReplaceTrivia(comment, comment.WithText("(?#other)"));

        Assert.Equal("a(?#other)b", updated.ToFullString());
    }

    [Fact]
    public void WithChanges_ReparsesInTheSameDialect()
    {
        var tree = RegexSyntaxTree.ParseText("a*", RegexDialect.PcrePerl);

        var updated = tree.WithChanges(new RegexTextChange(new TextSpan(2, 0), "+"));

        Assert.Equal("a*+", updated.Text);
        Assert.Equal(RegexDialect.PcrePerl, updated.Dialect);
        Assert.Empty(updated.Diagnostics);
    }

    [Fact]
    public void WithChanges_AppliesSeveralEditsFromTheEndBackwards()
    {
        var tree = RegexSyntaxTree.ParseText("abc", RegexDialect.Net);

        var updated = tree.WithChanges(
            new RegexTextChange(new TextSpan(0, 1), "x"),
            new RegexTextChange(new TextSpan(2, 1), "z"));

        Assert.Equal("xbz", updated.Text);
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

        var rewritten = new LiteralRenamer('a', 'z').Visit(tree.Root);

        Assert.Equal("z(?#note)b|z", rewritten?.ToFullString());
    }

    [Fact]
    public void Rewriter_ReturnsTheSameInstanceWhenNothingChanges()
    {
        var tree = RegexSyntaxTree.ParseText("xyz", RegexDialect.Net);

        var rewritten = new LiteralRenamer('a', 'z').Visit(tree.Root);

        Assert.Same(tree.Root, rewritten);
    }

    private sealed class LiteralRenamer(char from, char to) : RegexSyntaxRewriter
    {
        public override RegexSyntaxNode? VisitLiteral(RegexLiteralSyntax node)
        {
            if (node.Value != from)
                return base.VisitLiteral(node);

            return new RegexLiteralSyntax(node.LiteralToken.WithText(to.ToString()));
        }
    }
}
