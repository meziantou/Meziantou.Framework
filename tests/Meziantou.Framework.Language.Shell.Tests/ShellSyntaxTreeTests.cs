namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class ShellSyntaxTreeTests
{
    public static IEnumerable<ShellDialect> PosixDialects => [ShellDialect.Sh, ShellDialect.Bash, ShellDialect.Zsh];

    public static TheoryData<string> PosixSamples =>
    [
        "",
        "\n",
        "   ",
        "echo hello",
        "echo hello world\n",
        "  echo   spaced   args  \n\n",
        "# only a comment",
        "# leading comment\necho hi # trailing comment\n",
        "ls -la | grep foo | wc -l",
        "make build && make test || echo failed",
        "cd /tmp; ls; pwd",
        "sleep 5 &",
        "! grep -q pattern file",
        "echo 'single quoted'",
        """echo "double quoted"\n""",
        """echo "mixed 'inner' quotes"\n""",
        "echo \"value is $HOME\"",
        "echo ${HOME}",
        "echo ${HOME:-/default/path}",
        "echo $1 $? $@ $$",
        "echo $(date)",
        "echo `date`",
        "echo $(echo $(echo nested))",
        "FOO=bar",
        "FOO=bar BAZ=qux command arg",
        "FOO=",
        "echo out > file.txt",
        "echo out >> file.txt 2> err.txt",
        "cat < input.txt",
        "command 2>&1",
        "echo a\\ b",
        "echo \\$notavar",
        "echo *.txt",
        "echo file?.log",
        "echo one \\\n  two",
        "echo 'unterminated",
        "echo \"unterminated",
        "echo $(unterminated",
        "echo ${unterminated",
        ";",
        ";;;",
        "|",
        "echo a |",
        "echo >",
        "\n\n\n",
        "a=1\nb=2\necho $a$b\n",
    ];

    public static TheoryData<string> BashOnlySamples =>
    [
        "echo $((1 + 2))",
        "echo $(( (3 * 4) - 1 ))",
        "cat <<< 'here string'",
        "echo $((unterminated",
    ];

    [Theory]
    [MemberData(nameof(PosixSamples))]
    public void ParseText_RoundTripsExactly_ForEveryPosixDialect(string text)
    {
        foreach (var dialect in PosixDialects)
        {
            var tree = ShellSyntaxTree.ParseText(text, dialect);

            Assert.Equal(text, tree.Root.ToFullString());
            Assert.Equal(text, tree.Text);
        }
    }

    [Theory]
    [MemberData(nameof(BashOnlySamples))]
    public void ParseText_RoundTripsExactly_ForBashSpecificSyntax(string text)
    {
        foreach (var dialect in PosixDialects)
        {
            var tree = ShellSyntaxTree.ParseText(text, dialect);

            Assert.Equal(text, tree.Root.ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(PosixSamples))]
    public void ParseText_NeverThrows(string text)
    {
        foreach (var dialect in PosixDialects)
        {
            Assert.Null(Record.Exception(() => ShellSyntaxTree.ParseText(text, dialect)));
        }
    }

    [Fact]
    public void ParseText_SimpleCommand_BuildsExpectedTree()
    {
        var tree = ShellSyntaxTree.ParseText("echo hello world", ShellDialect.Bash);

        Assert.Empty(tree.Diagnostics);
        var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Equal("echo", command.NameValue);
        Assert.Equal(2, command.Arguments.Count);
        Assert.Equal("hello", command.Arguments[0].Value);
        Assert.Equal("world", command.Arguments[1].Value);
    }

    [Fact]
    public void ParseText_Pipeline_BuildsPipelineNode()
    {
        var tree = ShellSyntaxTree.ParseText("ls | grep foo | wc -l", ShellDialect.Bash);

        var pipeline = Assert.IsType<ShellPipelineSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Equal(3, pipeline.Commands.Count);
        Assert.Equal(2, pipeline.OperatorTokens.Count);
        Assert.All(pipeline.OperatorTokens, token => Assert.Equal(ShellSyntaxKind.PipeToken, token.Kind));
    }

    [Fact]
    public void ParseText_AndOrList_BuildsCommandListNode()
    {
        var tree = ShellSyntaxTree.ParseText("a && b || c", ShellDialect.Bash);

        var list = Assert.IsType<ShellCommandListSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Equal(3, list.Pipelines.Count);
        Assert.Equal(ShellSyntaxKind.AmpersandAmpersandToken, list.OperatorTokens[0].Kind);
        Assert.Equal(ShellSyntaxKind.PipePipeToken, list.OperatorTokens[1].Kind);
    }

    [Fact]
    public void ParseText_SemicolonSeparatedStatements_AreSiblings()
    {
        var tree = ShellSyntaxTree.ParseText("cd /tmp; ls; pwd", ShellDialect.Bash);

        Assert.Equal(3, tree.Root.Statements.Statements.Count);
        Assert.Equal(2, tree.Root.Statements.SeparatorTokens.Count);
    }

    [Fact]
    public void ParseText_NewlineSeparatedStatements_AreSiblings()
    {
        var tree = ShellSyntaxTree.ParseText("cd /tmp\nls\npwd\n", ShellDialect.Bash);

        Assert.Equal(3, tree.Root.Statements.Statements.Count);
        Assert.Empty(tree.Root.Statements.SeparatorTokens);
    }

    [Fact]
    public void ParseText_Assignment_IsAttachedToTheCommand()
    {
        var tree = ShellSyntaxTree.ParseText("FOO=bar BAZ=qux run --now", ShellDialect.Bash);

        var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Equal(2, command.Assignments.Count);
        Assert.Equal("FOO", command.Assignments[0].Name);
        Assert.Equal("bar", command.Assignments[0].Value?.Value);
        Assert.Equal("run", command.NameValue);
    }

    [Fact]
    public void ParseText_Redirections_KeepIoNumberAndTarget()
    {
        var tree = ShellSyntaxTree.ParseText("run 2> errors.log >> out.log", ShellDialect.Bash);

        var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Equal(2, command.Redirections.Count);
        Assert.Equal("2", command.Redirections[0].IoNumberToken?.Text);
        Assert.Equal("errors.log", command.Redirections[0].Target?.Value);
        Assert.Equal(ShellSyntaxKind.GreaterThanGreaterThanToken, command.Redirections[1].OperatorToken.Kind);
        Assert.Null(command.Redirections[1].IoNumberToken);
    }

    [Fact]
    public void ParseText_RedirectionInTheMiddle_KeepsSourceOrder()
    {
        const string Text = "echo >out hi";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Equal("echo", command.NameValue);
        Assert.Equal("hi", Assert.Single(command.Arguments).Value);
        Assert.Equal(Text, command.ToFullString());
    }

    [Fact]
    public void ParseText_CommentsAreTriviaWithSourceLocations()
    {
        const string Text = "# leading\necho hi # trailing\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        var comments = tree.Root.DescendantComments().ToArray();

        Assert.HasCount(2, comments);
        Assert.Equal("# leading", comments[0].Text);
        Assert.Equal(0, comments[0].Span.Start);
        Assert.Equal("# trailing", comments[1].Text);
        Assert.Equal(Text.IndexOf("# trailing", StringComparison.Ordinal), comments[1].Span.Start);
    }

    [Fact]
    public void ParseText_HashInsideAWordIsNotAComment()
    {
        var tree = ShellSyntaxTree.ParseText("echo abc#def", ShellDialect.Bash);

        var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Equal("abc#def", Assert.Single(command.Arguments).Value);
        Assert.Empty(tree.Root.DescendantComments());
    }

    [Fact]
    public void ParseText_VariableReferences_ExposeTheirName()
    {
        var tree = ShellSyntaxTree.ParseText("echo $HOME ${PATH} $1", ShellDialect.Bash);

        var names = tree.Root.DescendantNodes().OfType<ShellVariableReferenceSyntax>().Select(reference => reference.Name).ToArray();

        Assert.Equal(["HOME", "PATH", "1"], names);
    }

    [Fact]
    public void ParseText_CommandSubstitution_ParsesInnerCommand()
    {
        var tree = ShellSyntaxTree.ParseText("echo $(date -u)", ShellDialect.Bash);

        var substitution = Assert.Single(tree.Root.DescendantNodes().OfType<ShellCommandSubstitutionSyntax>());
        var inner = Assert.IsType<ShellCommandSyntax>(Assert.Single(substitution.Statements.Statements));

        Assert.False(substitution.IsBackquoted);
        Assert.Equal("date", inner.NameValue);
    }

    [Fact]
    public void ParseText_ArithmeticExpansion_IsModeledInBashButNotSh()
    {
        var bash = ShellSyntaxTree.ParseText("echo $((1 + 2))", ShellDialect.Bash);
        var expansion = Assert.Single(bash.Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>());
        Assert.Equal("1 + 2", expansion.Expression);

        var sh = ShellSyntaxTree.ParseText("echo $((1 + 2))", ShellDialect.Sh);
        Assert.Empty(sh.Root.DescendantNodes().OfType<PosixArithmeticExpansionSyntax>());
    }

    [Fact]
    public void ParseText_InvalidInput_ProducesDiagnosticsAndSkippedText()
    {
        var tree = ShellSyntaxTree.ParseText("echo 'unterminated", ShellDialect.Bash);

        Assert.NotEmpty(tree.Diagnostics);
        Assert.All(tree.Diagnostics, diagnostic => Assert.Equal(ShellDiagnosticSeverity.Error, diagnostic.Severity));
        Assert.Equal("SHELL0003", tree.Diagnostics[0].Id);
    }

    [Fact]
    public void ParseText_StraySeparator_IsKeptAsSkippedText()
    {
        const string Text = "; echo hi";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        Assert.Equal(Text, tree.Root.ToFullString());
        Assert.True(tree.Root.ContainsSkippedText);
        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "SHELL0002");
    }

    [Fact]
    public void ParseText_DeeplyNestedSubstitution_ReportsDepthAndStillRoundTrips()
    {
        var text = string.Concat(Enumerable.Repeat("$(", 40)) + "x" + string.Concat(Enumerable.Repeat(")", 40));
        var options = new ShellParseOptions(ShellDialect.Bash) { MaxRecursionDepth = 8 };

        var tree = ShellSyntaxTree.ParseText(text, options);

        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "SHELL0100");
    }

    [Fact]
    public void Span_ExcludesTriviaAndFullSpanIncludesIt()
    {
        const string Text = "  echo hi  ";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));

        Assert.Equal(2, command.Span.Start);
        Assert.Equal(9, command.Span.End);
        Assert.Equal(0, command.FullSpan.Start);
    }

    [Fact]
    public void WithChanges_ReparsesInTheSameDialect()
    {
        var tree = ShellSyntaxTree.ParseText("echo old", ShellDialect.Zsh);
        var updated = tree.WithChanges(new ShellTextChange(new TextSpan(5, 3), "new"));

        Assert.Equal("echo new", updated.Text);
        Assert.Equal(ShellDialect.Zsh, updated.Dialect);
    }

    [Fact]
    public void GetChanges_ReturnsEmptyForIdenticalText()
    {
        var tree = ShellSyntaxTree.ParseText("echo hi", ShellDialect.Bash);
        var other = ShellSyntaxTree.ParseText("echo hi", ShellDialect.Bash);

        Assert.Empty(tree.GetChanges(other));
        Assert.True(tree.IsEquivalentTo(other));
    }

    [Fact]
    public void IsEquivalentTo_IsFalseAcrossDialects()
    {
        var bash = ShellSyntaxTree.ParseText("echo hi", ShellDialect.Bash);
        var sh = ShellSyntaxTree.ParseText("echo hi", ShellDialect.Sh);

        Assert.False(bash.IsEquivalentTo(sh));
    }

    [Fact]
    public void SourceText_ReportsLines()
    {
        var tree = ShellSyntaxTree.ParseText("a\nb\nc", ShellDialect.Bash);

        Assert.Equal(3, tree.SourceText.Lines.Count);
        Assert.Equal("b", tree.SourceText.Lines[1].Text);
        Assert.Equal(1, tree.SourceText.GetLine(2).LineNumber);
    }
}
