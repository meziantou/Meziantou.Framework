namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class ShellParseCommandTests
{
    [Fact]
    public void ParseCommand_ReturnsTheSingleCommand()
    {
        var statement = ShellSyntaxTree.ParseCommand("git commit -m 'wip'", ShellDialect.Bash);

        var command = Assert.IsType<ShellCommandSyntax>(statement);
        Assert.Equal("git", command.NameValue);
        Assert.Equal(3, command.Arguments.Count);
        Assert.Equal("-m", command.Arguments[1].Value);
        Assert.Equal("wip", command.Arguments[2].Value);
    }

    [Fact]
    public void ParseCommand_ReturnsAPipelineWhenTheTextContainsAPipe()
    {
        var statement = ShellSyntaxTree.ParseCommand("ls | wc -l", ShellDialect.Bash);

        Assert.IsType<ShellPipelineSyntax>(statement);
    }

    [Fact]
    public void ParseCommand_ReturnsACommandListForAndOrOperators()
    {
        var statement = ShellSyntaxTree.ParseCommand("a && b", ShellDialect.Bash);

        Assert.IsType<ShellCommandListSyntax>(statement);
    }

    [Fact]
    public void ParseCommand_AttachesTheNodeToABackingTree()
    {
        const string Text = "echo hi";
        var statement = ShellSyntaxTree.ParseCommand(Text, ShellDialect.Bash);

        Assert.NotNull(statement.SyntaxTree);
        Assert.Equal(ShellDialect.Bash, statement.Dialect);
        Assert.Equal(Text, statement.ToFullString());
        Assert.Equal(0, statement.Span.Start);
        Assert.Equal(Text.Length, statement.Span.End);
        Assert.Equal(Text, statement.SyntaxTree.Root.ToFullString());
    }

    [Fact]
    public void ParseCommand_TrailingContent_ReportsShell0101AndStillRoundTrips()
    {
        const string Text = "echo one\necho two";
        var statement = ShellSyntaxTree.ParseCommand(Text, ShellDialect.Bash);

        Assert.Equal("echo one", statement.ToFullString());
        Assert.Contains(statement.SyntaxTree!.Diagnostics, diagnostic => diagnostic.Id == "SHELL0101");
        Assert.Equal(Text, statement.SyntaxTree.Root.ToFullString());
    }

    [Fact]
    public void ParseCommand_EmptyText_ReturnsSkippedTextInsteadOfThrowing()
    {
        var statement = ShellSyntaxTree.ParseCommand("   ", ShellDialect.Bash);

        Assert.IsType<ShellSkippedTextSyntax>(statement);
        Assert.NotNull(statement.SyntaxTree);
    }

    [Theory]
    [InlineData("sh")]
    [InlineData("bash")]
    [InlineData("zsh")]
    public void ParseCommand_WorksForEveryPosixDialect(string dialectName)
    {
        Assert.True(ShellDialect.TryParse(dialectName, out var dialect));

        var statement = ShellSyntaxTree.ParseCommand("printf '%s\\n' value", dialect);

        Assert.Equal("printf", Assert.IsType<ShellCommandSyntax>(statement).NameValue);
    }

    [Fact]
    public void ParseExpression_KeepsTheTextAndAttachesATree()
    {
        const string Text = "1 + 2 * 3";
        var expression = ShellSyntaxTree.ParseExpression(Text, ShellDialect.Bash);

        var raw = Assert.IsType<ShellRawExpressionSyntax>(expression);
        Assert.Equal(Text, raw.Text);
        Assert.Equal(Text, raw.ToFullString());
        Assert.NotNull(raw.SyntaxTree);
    }

    [Fact]
    public void ParseCommand_NeverThrowsOnMalformedInput()
    {
        foreach (var text in new[] { "", "|", ";;", "echo 'unterminated", "$(" })
        {
            Assert.Null(Record.Exception(() => ShellSyntaxTree.ParseCommand(text, ShellDialect.Bash)));
        }
    }
}
