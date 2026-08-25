namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class ShellSyntaxFactoryTests
{
    [Fact]
    public void Command_BuildsRunnableText()
    {
        var command = SyntaxFactory.Command(ShellDialect.Bash, "echo", "hello", "world");

        Assert.Equal("echo hello world", command.ToFullString());
    }

    [Fact]
    public void Command_QuotesArgumentsThatNeedIt()
    {
        var command = SyntaxFactory.Command(ShellDialect.Bash, "echo", "two words", "plain");

        Assert.Equal("echo 'two words' plain", command.ToFullString());
    }

    [Fact]
    public void QuotedString_UsesDoubleQuotesForPosixWhenTheValueHoldsASingleQuote()
    {
        // A single-quoted POSIX string has no escapes, so the quote cannot be written inside one.
        var word = SyntaxFactory.Word("it's", ShellDialect.Bash);

        Assert.Equal("\"it's\"", word.ToFullString());
        Assert.Equal("it's", word.Value);
    }

    [Fact]
    public void QuotedString_EscapesWhatPosixWouldStillActOnInsideDoubleQuotes()
    {
        var word = SyntaxFactory.Word("it's $x `cmd` \\ \"q\"", ShellDialect.Bash);

        Assert.Equal("\"it's \\$x \\`cmd\\` \\\\ \\\"q\\\"\"", word.ToFullString());
        Assert.Equal("it's $x `cmd` \\ \"q\"", word.Value);
    }

    [Theory]
    [InlineData("it's")]
    [InlineData("it's $x")]
    [InlineData("a\"b")]
    [InlineData("a$b")]
    [InlineData("a`b")]
    [InlineData("a\\b")]
    [InlineData("a b")]
    [InlineData("*.txt")]
    [InlineData("")]
    public void Word_ValueMatchesTheInputAndSurvivesAReparse(string value)
    {
        foreach (var dialect in new[] { ShellDialect.Sh, ShellDialect.Bash, ShellDialect.Zsh, ShellDialect.PowerShell, ShellDialect.PowerShellCore })
        {
            var word = SyntaxFactory.Word(value, dialect);

            Assert.Equal(value, word.Value);
            var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo " + word.ToFullString(), dialect));
            Assert.Equal(value, command.Arguments[0].Value);
        }
    }

    [Fact]
    public void QuotedString_DoublesSingleQuotesForPowerShell()
    {
        var word = SyntaxFactory.Word("it's", ShellDialect.PowerShellCore);

        Assert.Equal("'it''s'", word.ToFullString());
    }

    [Fact]
    public void VariableReference_UsesTheDialectSyntax()
    {
        Assert.Equal("$PATH", SyntaxFactory.VariableReference("PATH", ShellDialect.Bash).ToFullString());
        Assert.Equal("${PATH}", SyntaxFactory.VariableReference("PATH", ShellDialect.Bash, braced: true).ToFullString());
        Assert.Equal("%PATH%", SyntaxFactory.VariableReference("PATH", ShellDialect.Cmd).ToFullString());
    }

    [Fact]
    public void Pipeline_JoinsCommandsWithPipes()
    {
        var pipeline = SyntaxFactory.Pipeline(
            SyntaxFactory.Command(ShellDialect.Bash, "ls"),
            SyntaxFactory.Command(ShellDialect.Bash, "wc", "-l"));

        Assert.Equal("ls | wc -l", pipeline.ToFullString());
    }

    [Fact]
    public void Script_RoundTripsThroughTheParser()
    {
        var script = SyntaxFactory.Script(SyntaxFactory.StatementList(
            SyntaxFactory.Command(ShellDialect.Bash, "cd", "/tmp"),
            SyntaxFactory.Command(ShellDialect.Bash, "ls")));

        var text = script.ToFullString();
        Assert.Equal("cd /tmp; ls", text);
        Assert.Equal(text, ShellSyntaxTree.ParseText(text, ShellDialect.Bash).Root.ToFullString());
    }

    [Fact]
    public void Comment_AddsTheDialectMarker()
    {
        Assert.Equal("# note", SyntaxFactory.Comment("note", ShellDialect.Bash).Text);
        Assert.Equal("# already", SyntaxFactory.Comment("# already", ShellDialect.Bash).Text);
        Assert.Equal(":: note", SyntaxFactory.Comment("note", ShellDialect.Cmd).Text);
    }

    [Theory]
    [InlineData("plain", false)]
    [InlineData("with space", true)]
    [InlineData("$var", true)]
    [InlineData("", true)]
    [InlineData("a*b", true)]
    public void RequiresQuoting_DetectsSpecialCharacters(string text, bool expected)
    {
        Assert.Equal(expected, SyntaxFactory.RequiresQuoting(text, ShellDialect.Bash));
    }
}
