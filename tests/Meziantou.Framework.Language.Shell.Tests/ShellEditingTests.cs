namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class ShellEditingTests
{
    [Fact]
    public void ReplaceNode_SwapsAnArgument()
    {
        var tree = ShellSyntaxTree.ParseText("echo old --flag", ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[0]);

        var argument = command.Arguments[0];
        var updated = tree.GetRoot().ReplaceNode(argument, SyntaxFactory.Word("new", ShellDialect.Bash).WithTriviaFrom(argument));

        Assert.Equal("echo new --flag", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_ReplacesTheExactInstance_WhenTheTextIsDuplicated()
    {
        var tree = ShellSyntaxTree.ParseText("echo dup dup", ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[0]);

        var argument = command.Arguments[1];
        var updated = tree.GetRoot().ReplaceNode(argument, SyntaxFactory.Word("second", ShellDialect.Bash).WithTriviaFrom(argument));

        Assert.Equal("echo dup second", updated.ToFullString());
    }

    [Fact]
    public void ReplaceNode_PreservesSurroundingTriviaAndComments()
    {
        const string Text = "# header\necho   old    # trailing\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[0]);

        var argument = command.Arguments[0];
        var updated = tree.GetRoot().ReplaceNode(argument, SyntaxFactory.Word("new", ShellDialect.Bash).WithTriviaFrom(argument));

        Assert.Equal("# header\necho   new    # trailing\n", updated.ToFullString());
    }

    /// <summary>
    /// An edit rebuilds the tree rather than re-reading the text, so the result belongs to no tree of its own until
    /// one is built from it -- and that is what carries the dialect.
    /// </summary>
    [Fact]
    public void ReplaceNode_ReturnsADetachedRootThatCanBeGivenATreeAgain()
    {
        var tree = ShellSyntaxTree.ParseText("echo $((1+1))", ShellDialect.Zsh);
        var command = Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[0]);

        var updated = tree.GetRoot().ReplaceNode(command.Arguments[0], SyntaxFactory.Word("done", ShellDialect.Zsh));

        Assert.Null(updated.SyntaxTree);
        Assert.Equal(ShellDialect.Zsh, ShellSyntaxTree.ParseText(updated.ToFullString(), ShellDialect.Zsh).Dialect);
    }

    [Fact]
    public void ReplaceToken_SwapsARedirectionOperator()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi > out.txt", ShellDialect.Bash);
        var redirection = tree.GetRoot().DescendantNodes().OfType<ShellRedirectionSyntax>().Single();

        var updated = tree.GetRoot().ReplaceToken(
            redirection.OperatorToken,
            SyntaxFactory.Token(SyntaxKind.GreaterThanGreaterThanToken, ">>").WithTriviaFrom(redirection.OperatorToken));

        Assert.Equal("echo hi >> out.txt", updated.ToFullString());
    }

    [Fact]
    public void ReplaceTrivia_RewritesAComment()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi # old note\n", ShellDialect.Bash);
        var comment = tree.GetRoot().DescendantComments().Single();

        var updated = tree.GetRoot().ReplaceTrivia(comment, SyntaxFactory.Comment("new note", ShellDialect.Bash));

        Assert.Equal("echo hi # new note\n", updated.ToFullString());
    }

    /// <summary>
    /// Replacing a node from another tree is rejected rather than quietly doing nothing, which is what the old
    /// text-search fallback did when it could not find the node.
    /// </summary>
    [Fact]
    public void ReplaceNode_WithAnUnrelatedNode_IsRejected()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi", ShellDialect.Bash);
        var other = ShellSyntaxTree.ParseText("unrelated text here", ShellDialect.Bash);
        var foreignNode = other.GetRoot().DescendantNodes().OfType<ShellCommandSyntax>().Single();

        Assert.Throws<ArgumentException>(() => tree.GetRoot().ReplaceNode(foreignNode, SyntaxFactory.Word("x", ShellDialect.Bash)));
    }

    [Fact]
    public void WithChanges_AppliesMultipleEditsRightToLeft()
    {
        var tree = ShellSyntaxTree.ParseText("aaa bbb ccc", ShellDialect.Bash);

        var updated = tree.WithChanges(
            new TextChange(new TextSpan(0, 3), "xxx"),
            new TextChange(new TextSpan(8, 3), "zzz"));

        Assert.Equal("xxx bbb zzz", updated.GetText().Text);
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

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            Assert.Equal(text, tree.GetRoot().ReplaceNode(node, node).ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(IncompleteScripts))]
    public void ReplaceToken_WithTheSameToken_ChangesNothing(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxTree.ParseText(text, dialect);

        foreach (var token in tree.GetRoot().DescendantTokens())
        {
            Assert.Equal(text, tree.GetRoot().ReplaceToken(token, token).ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(IncompleteScripts))]
    public void Rewriter_ThatReplacesNothing_ChangesNothing(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxTree.ParseText(text, dialect);

        Assert.Same(tree.GetRoot(), new UnchangedRewriter().Visit(tree.GetRoot()));
    }

    [Fact]
    public void ReplaceNode_KeepsTriviaHeldByATrailingMissingToken()
    {
        // The space is the leading trivia of the missing token that ends the statement, so it falls outside the span.
        var tree = ShellSyntaxTree.ParseText("for ", ShellDialect.Bash);
        var statement = Assert.Single(tree.GetRoot().Statements.Statements);

        Assert.True(statement.Span.End <= statement.FullSpan.End);
        Assert.Equal("for ", tree.GetRoot().ReplaceNode(statement, statement).ToFullString());
    }

    [Fact]
    public void ReplaceNode_SeesLeadingTriviaHeldPastAMissingToken()
    {
        // The function definition starts with a missing name of no width, so the space before `(` is on the next token.
        var tree = ShellSyntaxTree.ParseText("l l ()", ShellDialect.Zsh);
        var definition = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PosixFunctionDefinitionSyntax>());

        Assert.True(definition.Span.Start >= definition.FullSpan.Start);
        Assert.Equal("l l ()", tree.GetRoot().ReplaceNode(definition, definition).ToFullString());
    }

    private sealed class UnchangedRewriter : ShellSyntaxRewriter;

    [Fact]
    public void EditedTree_StillRoundTrips()
    {
        var tree = ShellSyntaxTree.ParseText("# c\nls -la | grep x\n", ShellDialect.Bash);
        var updated = tree.WithChanges(new TextChange(new TextSpan(4, 2), "cd"));

        Assert.Equal(updated.GetText().Text, updated.GetRoot().ToFullString());
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

    /// <summary>
    /// A command holds its elements in a plain list, with nothing between them, so removing one takes only that one.
    /// </summary>
    [Fact]
    public void RemoveNode_FromACommand_TakesOnlyThatElement()
    {
        var tree = ShellSyntaxTree.ParseText("echo a b c", ShellDialect.Bash);
        var command = tree.GetRoot().DescendantNodes().OfType<ShellCommandSyntax>().First();
        var target = command.Elements[2];

        var updated = tree.GetRoot().RemoveNode(target, SyntaxRemoveOptions.KeepNoTrivia);

        Assert.Equal("echo a c", updated.ToFullString());
    }

    /// <summary>
    /// The bodies of a pipeline's here-documents follow it in one sequence, so each redirection has to find its own.
    /// </summary>
    [Fact]
    public void HereDocument_OfEachRedirectionInAPipeline_IsItsOwnBody()
    {
        var tree = ShellSyntaxTree.ParseText("cat <<A | cat <<B\nbodyA\nA\nbodyB\nB\n", ShellDialect.Bash);
        var redirections = tree.GetRoot().DescendantNodes().OfType<ShellRedirectionSyntax>().ToArray();

        Assert.HasCount(2, redirections);
        Assert.Contains("bodyA", redirections[0].HereDocument!.ToFullString());
        Assert.Contains("bodyB", redirections[1].HereDocument!.ToFullString());

        // The two directions of the link agree.
        foreach (var redirection in redirections)
        {
            Assert.Same(redirection, redirection.HereDocument!.Redirection);
        }
    }

    /// <summary>
    /// Two removals in different slots of the same node both take effect. Registering them one after the other and
    /// keeping only the last would drop one silently.
    /// </summary>
    [Fact]
    public void RemoveNodes_TakesChildrenFromTwoSlotsOfTheSameNode()
    {
        var tree = ShellSyntaxTree.ParseText("if true; then echo a; elif false; then echo b; else echo c; fi", ShellDialect.Bash);
        var conditional = tree.GetRoot().DescendantNodes().OfType<PosixIfStatementSyntax>().Single();

        var updated = tree.GetRoot().RemoveNodes([conditional.ElifClauses[0], conditional.ElseClause!], SyntaxRemoveOptions.KeepNoTrivia);
        var text = updated.ToFullString();

        Assert.DoesNotContain("elif", text);
        Assert.DoesNotContain("else", text);
    }

    /// <summary>The diagnostics of a tree that keeps them in a flat list still reach a language-independent caller.</summary>
    [Fact]
    public void GetDiagnostics_IsReachableThroughTheSharedTreeType()
    {
        SyntaxTree tree = ShellSyntaxTree.ParseText("if true", ShellDialect.Bash);

        Assert.Equal(((ShellSyntaxTree)tree).GetDiagnostics().Count, tree.GetDiagnostics().Count);
        Assert.NotEmpty(tree.GetDiagnostics());
    }

    /// <summary>
    /// A command added to a pipeline is joined with a pipe, not with the separator a statement list would use.
    /// The separator carries the spacing of the one already there; the spacing of the command itself is the
    /// caller's, the same as it is for a replacement.
    /// </summary>
    [Fact]
    public void AddingToAPipelineKeepsItAPipeline()
    {
        var tree = ShellSyntaxTree.ParseText("a | b", ShellDialect.Bash);
        var pipeline = tree.GetRoot().DescendantNodes().OfType<ShellPipelineSyntax>().Single();

        var added = SyntaxFactory.Command(ShellDialect.Bash, "c");
        Assert.Equal("a | b |c", pipeline.WithCommands(pipeline.Commands.Add(added)).ToFullString());
        Assert.Equal("a | b | c", pipeline.WithCommands(pipeline.Commands.Add(added.WithLeadingTrivia(SyntaxFactory.Space))).ToFullString());
    }

    /// <summary>
    /// A pipeline of one command has no pipe to copy, so the node that owns the slot is what says a pipe goes there.
    /// </summary>
    [Fact]
    public void AddingToAPipelineThatHasNoSeparatorYetStillUsesAPipe()
    {
        var pipeline = SyntaxFactory.Pipeline(SyntaxFactory.Command(ShellDialect.Bash, "a"));

        var updated = pipeline.WithCommands(pipeline.Commands.Add(SyntaxFactory.Command(ShellDialect.Bash, "b")));

        Assert.Equal("a|b", updated.ToFullString());
        Assert.Equal(2, updated.Commands.Count);
        Assert.Equal(SyntaxKind.PipeToken, updated.Commands.GetSeparator(0).Kind());
    }

    /// <summary>
    /// Inserting into a pipeline of one command has no pipe to copy either, so the slot that holds the list is what
    /// says a pipe goes there -- the language-wide separator of a shell is the semicolon, which would end it.
    /// </summary>
    [Fact]
    public void InsertNodesAfter_InAPipelineOfOneCommand_StillUsesAPipe()
    {
        var pipeline = SyntaxFactory.Pipeline(SyntaxFactory.Command(ShellDialect.Bash, "a"));

        var updated = pipeline.InsertNodesAfter(pipeline.Commands[0], [SyntaxFactory.Command(ShellDialect.Bash, "b")]);

        Assert.Equal("a|b", updated.ToFullString());
        Assert.Equal(2, updated.Commands.Count);
        Assert.Equal(SyntaxKind.PipeToken, updated.Commands.GetSeparator(0).Kind());
    }

    /// <summary>A statement list still gets the separator a statement list takes.</summary>
    [Fact]
    public void AddingToAStatementListUsesASemicolon()
    {
        var tree = ShellSyntaxTree.ParseText("a", ShellDialect.Bash);
        var statements = tree.GetRoot().Statements;

        var updated = statements.WithStatements(statements.Statements.Add(SyntaxFactory.Command(ShellDialect.Bash, "b")));

        Assert.Equal(SyntaxKind.SemicolonToken, updated.Statements.GetSeparator(0).Kind());
    }

    /// <summary>
    /// A here-document inside a substitution belongs to the statement list inside it, so it must not be counted
    /// when pairing the bodies of the list outside.
    /// </summary>
    [Fact]
    public void HereDocumentsInsideASubstitutionDoNotShiftTheOnesOutside()
    {
        const string Text = "cat <(cat <<INNER\ninner\nINNER\n) <<OUTER\nouter\nOUTER\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        Assert.Equal(Text, tree.GetRoot().ToFullString());

        var redirections = tree.GetRoot().DescendantNodes().OfType<ShellRedirectionSyntax>()
            .Where(redirection => redirection.OperatorToken.Kind() == SyntaxKind.LessThanLessThanToken)
            .ToArray();
        var inner = redirections.Single(redirection => redirection.Target!.Value == "INNER");
        var outer = redirections.Single(redirection => redirection.Target!.Value == "OUTER");

        Assert.Contains("inner", inner.HereDocument!.ToFullString());
        Assert.Contains("outer", outer.HereDocument!.ToFullString());
        Assert.Same(outer, outer.HereDocument!.Redirection);
        Assert.Same(inner, inner.HereDocument!.Redirection);
    }
}
