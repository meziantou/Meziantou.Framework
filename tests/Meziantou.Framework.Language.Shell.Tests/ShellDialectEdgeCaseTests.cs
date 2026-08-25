namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// Constructs that are easy to mis-parse in each dialect. These assert the resulting tree shape, not just that the
/// text survives, because skipped text would round-trip perfectly while being structurally wrong.
/// </summary>
public sealed class ShellDialectEdgeCaseTests
{
    private static ShellStatementSyntax Single(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxTree.ParseText(text, dialect);

        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);

        return Assert.Single(tree.Root.Statements.Statements);
    }

    // ---- POSIX ----

    [Fact]
    public void Posix_CasePatternsMayBeGlobs()
    {
        var statement = Assert.IsType<PosixCaseStatementSyntax>(
            Single("case $f in\n*.txt) echo t;;\n[ab]*) echo ab;;\n*) echo o;;\nesac\n", ShellDialect.Bash));

        Assert.HasCount(3, statement.Clauses);
        Assert.Single(statement.Clauses[0].Patterns[0].Parts.OfType<ShellGlobSyntax>());
        Assert.Single(statement.Clauses[1].Patterns[0].Parts.OfType<ShellGlobSyntax>(), glob => glob.IsBracketExpression);
    }

    [Theory]
    [InlineData("cat <<EOF\r\nbody\r\nEOF\r\n", "\r\nbody\r\n")]
    [InlineData("cat <<EOF\nEOF\n", "\n")]
    [InlineData("cat <<EOF\nEOFX\nEOF\n", "\nEOFX\n")]
    public void Posix_HereDocumentBodyStopsAtTheExactDelimiterLine(string text, string expectedBody)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Bash);
        Assert.Equal(text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);

        var hereDocument = Assert.Single(tree.Root.DescendantNodes().OfType<PosixHereDocumentSyntax>());
        Assert.Equal(expectedBody, hereDocument.BodyToken.Text);
        Assert.NotNull(hereDocument.Redirection?.HereDocument);
    }

    [Fact]
    public void Posix_HereDocumentComposesWithAPipeline()
    {
        const string Text = "cat <<EOF | wc -l\nx\nEOF\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        Assert.Equal(Text, tree.Root.ToFullString());
        Assert.Empty(tree.Diagnostics);

        // The pipeline is the whole command line; the body follows it.
        var pipeline = Assert.IsType<ShellPipelineSyntax>(tree.Root.Statements.Statements[0]);
        Assert.HasCount(2, pipeline.Commands);
        var hereDocument = Assert.Single(tree.Root.DescendantNodes().OfType<PosixHereDocumentSyntax>());
        Assert.Equal("\nx\n", hereDocument.BodyToken.Text);
    }

    [Fact]
    public void Posix_SubshellComposesWithAPipeline()
    {
        var pipeline = Assert.IsType<ShellPipelineSyntax>(Single("( echo a ) | wc\n", ShellDialect.Bash));

        Assert.True(Assert.IsType<PosixCompoundStatementSyntax>(pipeline.Commands[0]).IsSubshell);
    }

    [Fact]
    public void Posix_NestedQuotesInsideCommandSubstitution()
    {
        var command = Assert.IsType<ShellCommandSyntax>(Single("echo \"$(echo \"inner\")\"\n", ShellDialect.Bash));
        var substitution = Assert.Single(command.DescendantNodes().OfType<ShellCommandSubstitutionSyntax>());

        Assert.Equal("inner", Assert.IsType<ShellCommandSyntax>(substitution.Statements.Statements[0]).Arguments[0].Value);
    }

    [Fact]
    public void Posix_TrailingBackslashAtEndOfInputDoesNotThrow()
    {
        var tree = ShellSyntaxTree.ParseText("echo x\\", ShellDialect.Bash);

        Assert.Equal("echo x\\", tree.Root.ToFullString());
    }

    // ---- PowerShell ----

    [Fact]
    public void PowerShell_SplattedArgumentIsRecognizedAmongOtherArguments()
    {
        var command = Assert.IsType<ShellCommandSyntax>(Single("Get-X @params -Y 1\n", ShellDialect.PowerShellCore));

        Assert.True(Assert.Single(command.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>()).IsSplatted);
    }

    [Fact]
    public void PowerShell_NestedTernaryParses()
    {
        Single("$a = $x ? ($y ? 1 : 2) : 3\n", ShellDialect.PowerShellCore);
    }

    [Fact]
    public void PowerShell_MultiLinePipelineContinues()
    {
        var pipeline = Assert.IsType<ShellPipelineSyntax>(Single("a |\nb |\nc\n", ShellDialect.PowerShellCore));

        Assert.HasCount(3, pipeline.Commands);
    }

    [Fact]
    public void PowerShell_TypeConstrainedAssignmentIsACast()
    {
        var statement = Assert.IsType<PowerShellExpressionStatementSyntax>(Single("[int]$a = 5\n", ShellDialect.PowerShellCore));
        var assignment = Assert.IsType<PowerShellAssignmentExpressionSyntax>(statement.Expression);

        Assert.IsType<PowerShellCastExpressionSyntax>(assignment.Target);
    }

    [Fact]
    public void PowerShell_CommentInsideAHashLiteralIsTrivia()
    {
        var statement = Single("$a = @{\n # c\n x = 1\n}\n", ShellDialect.PowerShellCore);
        var hash = Assert.Single(statement.DescendantNodes().OfType<PowerShellHashLiteralSyntax>());

        Assert.Single(hash.Entries);
        Assert.Single(statement.DescendantComments());
    }

    [Fact]
    public void PowerShell_HashLiteralToleratesATrailingSeparator()
    {
        var statement = Single("$a = @{ x = 1; }\n", ShellDialect.PowerShellCore);

        Assert.Single(Assert.Single(statement.DescendantNodes().OfType<PowerShellHashLiteralSyntax>()).Entries);
    }

    // ---- Cmd ----

    [Fact]
    public void Cmd_IfElseMaySpanLines()
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(
            Single("if exist a (\r\n echo y\r\n) else (\r\n echo n\r\n)\r\n", ShellDialect.Cmd));

        Assert.IsType<CmdParenthesizedBlockSyntax>(statement.Body);
        Assert.IsType<CmdParenthesizedBlockSyntax>(statement.ElseClause?.Body);
    }

    [Fact]
    public void Cmd_NestedIfIsTheBodyOfTheOuterIf()
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(Single("if exist a if exist b echo both\r\n", ShellDialect.Cmd));

        Assert.IsType<CmdIfStatementSyntax>(statement.Body);
    }

    [Fact]
    public void Cmd_SetSlashAKeepsAParenthesizedExpression()
    {
        var statement = Assert.IsType<CmdSetStatementSyntax>(Single("set /a \"x=(1+2)*3\"\r\n", ShellDialect.Cmd));

        Assert.True(statement.IsArithmetic);
        Assert.Contains("(1+2)*3", statement.Value?.ToFullString());
    }

    [Fact]
    public void Cmd_CaretLineContinuationJoinsACommand()
    {
        var command = Assert.IsType<ShellCommandSyntax>(Single("echo a ^\r\n b\r\n", ShellDialect.Cmd));

        Assert.Equal(["a", "b"], command.Arguments.Select(argument => argument.Value));
        Assert.Contains(command.DescendantTrivia(), trivia => trivia.Kind == ShellSyntaxKind.LineContinuationTrivia);
    }

    [Fact]
    public void Cmd_ForBodyMayBeAParenthesizedBlock()
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(Single("for %%i in (a) do (\r\n echo %%i\r\n)\r\n", ShellDialect.Cmd));

        Assert.IsType<CmdParenthesizedBlockSyntax>(statement.Body);
    }

    [Fact]
    public void Cmd_QuotedPipeDoesNotSplitThePipeline()
    {
        var pipeline = Assert.IsType<ShellPipelineSyntax>(Single("echo \"a|b\" | findstr x\r\n", ShellDialect.Cmd));

        Assert.HasCount(2, pipeline.Commands);
        Assert.Equal("a|b", Assert.IsType<ShellCommandSyntax>(pipeline.Commands[0]).Arguments[0].Value);
    }
}
