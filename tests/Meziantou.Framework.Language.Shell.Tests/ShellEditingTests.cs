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

    public static TheoryData<string, ShellDialect> IncompleteScripts() => new()
    {
        // Each of these ends in a missing token, so some of the text belongs to a node without belonging to its Span.
        { "for ", ShellDialect.Bash },
        { "| ", ShellDialect.Sh },
        { "&&\n", ShellDialect.Cmd },
        { "while ", ShellDialect.Zsh },
        { "case ", ShellDialect.Bash },
        // These start with a missing token of no width, so the leading trivia sits on the second token.
        { "l l ()", ShellDialect.Zsh },
        { "\n()", ShellDialect.Zsh },
        { "coproc ()", ShellDialect.Zsh },
    };

    [Theory]
    [MemberData(nameof(IncompleteScripts))]
    public void ReplaceNode_WithTheSameNode_ChangesNothing(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxTree.ParseText(text, dialect);

        foreach (var node in tree.Root.DescendantNodes())
        {
            Assert.Equal(text, tree.Root.ReplaceNode(node, node).ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(IncompleteScripts))]
    public void ReplaceToken_WithTheSameToken_ChangesNothing(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxTree.ParseText(text, dialect);

        foreach (var token in tree.Root.DescendantTokens())
        {
            Assert.Equal(text, tree.Root.ReplaceToken(token, token).ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(IncompleteScripts))]
    public void Rewriter_ThatReplacesNothing_ChangesNothing(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxTree.ParseText(text, dialect);

        Assert.Same(tree.Root, new UnchangedRewriter().Visit(tree.Root));
    }

    [Fact]
    public void ReplaceNode_KeepsTriviaHeldByATrailingMissingToken()
    {
        // The space belongs to the for statement but falls outside its Span, which stops at the last token with text.
        var tree = ShellSyntaxTree.ParseText("for ", ShellDialect.Bash);
        var statement = Assert.Single(tree.Root.Statements.Statements);

        Assert.True(statement.Span.End < statement.FullSpan.End);
        Assert.Equal("for ", tree.Root.ReplaceNode(statement, statement).ToFullString());
    }

    [Fact]
    public void ReplaceNode_SeesLeadingTriviaHeldPastAMissingToken()
    {
        // The function definition starts with a missing name of no width, so the space before `(` is on the next token.
        var tree = ShellSyntaxTree.ParseText("l l ()", ShellDialect.Zsh);
        var definition = Assert.Single(tree.Root.DescendantNodes().OfType<PosixFunctionDefinitionSyntax>());

        Assert.True(definition.Span.Start > definition.FullSpan.Start);
        Assert.Equal("l l ()", tree.Root.ReplaceNode(definition, definition).ToFullString());
    }

    private sealed class UnchangedRewriter : ShellSyntaxRewriter;

    [Fact]
    public void EditedTree_StillRoundTrips()
    {
        var tree = ShellSyntaxTree.ParseText("# c\nls -la | grep x\n", ShellDialect.Bash);
        var updated = tree.WithChanges(new ShellTextChange(new TextSpan(4, 2), "cd"));

        Assert.Equal(updated.Text, updated.Root.ToFullString());
    }

    [Fact]
    public void WithArguments_SeparatesFactoryWordsFromTheCommandName()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo old", ShellDialect.Bash));

        var updated = command.WithArguments([SyntaxFactory.Word("new", ShellDialect.Bash), SyntaxFactory.Word("two", ShellDialect.Bash)]);

        Assert.Equal("echo new two", updated.ToFullString());
    }

    [Fact]
    public void WithArguments_KeepsTheSpacingOfWordsThatBringTheirOwn()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo a   b", ShellDialect.Bash));

        // Each parsed word owns the whitespace in front of it, so reordering carries the spacing along and no
        // separator is added on top of it.
        Assert.Equal("echo   b a", command.WithArguments([.. command.Arguments.Reverse()]).ToFullString());
    }
}
