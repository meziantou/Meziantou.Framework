namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class ShellEditingTests
{
    [Fact]
    public void ReplaceNode_SwapsAnArgument()
    {
        var tree = ShellSyntaxTree.ParseText("echo old --flag", ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.Root.Statements.Statements[0]);

        var updated = tree.Root.ReplaceNode(command.Arguments[0], SyntaxFactory.Word("new", ShellDialect.Bash));

        Assert.Equal("echo new --flag", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_ReplacesTheExactInstance_WhenTheTextIsDuplicated()
    {
        var tree = ShellSyntaxTree.ParseText("echo dup dup", ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.Root.Statements.Statements[0]);

        var updated = tree.Root.ReplaceNode(command.Arguments[1], SyntaxFactory.Word("second", ShellDialect.Bash));

        Assert.Equal("echo dup second", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_PreservesSurroundingTriviaAndComments()
    {
        const string Text = "# header\necho   old    # trailing\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.Root.Statements.Statements[0]);

        var updated = tree.Root.ReplaceNode(command.Arguments[0], SyntaxFactory.Word("new", ShellDialect.Bash));

        Assert.Equal("# header\necho   new    # trailing\n", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_KeepsTheDialectOfTheOriginalTree()
    {
        var tree = ShellSyntaxTree.ParseText("echo $((1+1))", ShellDialect.Zsh);
        var command = Assert.IsType<ShellCommandSyntax>(tree.Root.Statements.Statements[0]);

        var updated = tree.Root.ReplaceNode(command.Arguments[0], SyntaxFactory.Word("done", ShellDialect.Zsh));

        Assert.Equal(ShellDialect.Zsh, updated.SyntaxTree?.Dialect);
    }

    [Fact]
    public void ReplaceToken_SwapsARedirectionOperator()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi > out.txt", ShellDialect.Bash);
        var redirection = tree.Root.DescendantNodes().OfType<ShellRedirectionSyntax>().Single();

        var updated = tree.Root.ReplaceToken(
            redirection.OperatorToken,
            SyntaxFactory.Token(ShellSyntaxKind.GreaterThanGreaterThanToken, ">>"));

        Assert.Equal("echo hi >> out.txt", updated.ToFullString());
    }

    [Fact]
    public void ReplaceTrivia_RewritesAComment()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi # old note\n", ShellDialect.Bash);
        var comment = tree.Root.DescendantComments().Single();

        var updated = tree.Root.ReplaceTrivia(comment, SyntaxFactory.Comment("new note", ShellDialect.Bash));

        Assert.Equal("echo hi # new note\n", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_WithAnUnrelatedNode_ReturnsTheSameScript()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi", ShellDialect.Bash);
        var other = ShellSyntaxTree.ParseText("unrelated text here", ShellDialect.Bash);
        var foreignNode = other.Root.DescendantNodes().OfType<ShellCommandSyntax>().Single();

        Assert.Same(tree.Root, tree.Root.ReplaceNode(foreignNode, SyntaxFactory.Word("x", ShellDialect.Bash)));
    }

    [Fact]
    public void WithChanges_AppliesMultipleEditsRightToLeft()
    {
        var tree = ShellSyntaxTree.ParseText("aaa bbb ccc", ShellDialect.Bash);

        var updated = tree.WithChanges(
            new ShellTextChange(new TextSpan(0, 3), "xxx"),
            new ShellTextChange(new TextSpan(8, 3), "zzz"));

        Assert.Equal("xxx bbb zzz", updated.Text);
    }

    [Fact]
    public void EditedTree_StillRoundTrips()
    {
        var tree = ShellSyntaxTree.ParseText("# c\nls -la | grep x\n", ShellDialect.Bash);
        var updated = tree.WithChanges(new ShellTextChange(new TextSpan(4, 2), "cd"));

        Assert.Equal(updated.Text, updated.Root.ToFullString());
    }
}
