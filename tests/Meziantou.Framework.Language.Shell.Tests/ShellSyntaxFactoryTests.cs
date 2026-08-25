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

    public static TheoryData<string> WordPartKinds()
    {
        var data = new TheoryData<string>();
        foreach (var type in typeof(ShellWordPartSyntax).Assembly.GetTypes())
        {
            if (type.IsSubclassOf(typeof(ShellWordPartSyntax)) && !type.IsAbstract)
            {
                data.Add(type.Name);
            }
        }

        return data;
    }

    /// <summary>
    /// Every word-part type has to be separated from what precedes it. Driving the theory off the types in the
    /// assembly means a part type added later shows up here instead of silently gluing itself to the command name.
    /// </summary>
    [Theory]
    [MemberData(nameof(WordPartKinds))]
    public void Command_And_WithArguments_SeparateEveryWordPartKind(string typeName)
    {
        var word = SyntaxFactory.Word(BuildPart(typeName));

        var built = SyntaxFactory.Command(SyntaxFactory.Word("echo", ShellDialect.Bash), word).ToFullString();
        Assert.Equal("echo ", built[.."echo ".Length]);

        var parsed = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo old", ShellDialect.Bash));
        var edited = parsed.WithArguments([word]).ToFullString();
        Assert.Equal("echo ", edited[.."echo ".Length]);
    }

    private static ShellWordPartSyntax BuildPart(string typeName)
    {
        var statements = SyntaxFactory.StatementList(SyntaxFactory.Command(ShellDialect.Bash, "ls"));

        return typeName switch
        {
            nameof(ShellLiteralWordPartSyntax) => SyntaxFactory.Literal("plain"),
            nameof(ShellQuotedStringSyntax) => SyntaxFactory.QuotedString("a b", ShellDialect.Bash),
            nameof(ShellVariableReferenceSyntax) => SyntaxFactory.VariableReference("value", ShellDialect.Bash),
            nameof(CmdVariableReferenceSyntax) => (ShellWordPartSyntax)SyntaxFactory.VariableReference("value", ShellDialect.Cmd),
            nameof(ShellGlobSyntax) => new ShellGlobSyntax(SyntaxFactory.Token(ShellSyntaxKind.BareTextToken, "*")),
            nameof(ShellEscapeSequenceSyntax) => new ShellEscapeSequenceSyntax(SyntaxFactory.Token(ShellSyntaxKind.EscapeToken, @"\$", "$")),
            nameof(ShellCommandSubstitutionSyntax) => new ShellCommandSubstitutionSyntax(SyntaxFactory.Token(ShellSyntaxKind.DollarOpenParenToken, "$("), statements, SyntaxFactory.Token(ShellSyntaxKind.CloseParenToken, ")")),
            nameof(PosixProcessSubstitutionSyntax) => new PosixProcessSubstitutionSyntax(SyntaxFactory.Token(ShellSyntaxKind.OpenParenToken, "<("), statements, SyntaxFactory.Token(ShellSyntaxKind.CloseParenToken, ")")),
            nameof(PosixArithmeticExpansionSyntax) => new PosixArithmeticExpansionSyntax(SyntaxFactory.Token(ShellSyntaxKind.DollarOpenParenToken, "$(("), SyntaxFactory.RawExpression("1+2"), SyntaxFactory.Token(ShellSyntaxKind.CloseParenToken, "))")),
            nameof(ShellEmbeddedExpressionSyntax) => new ShellEmbeddedExpressionSyntax(SyntaxFactory.RawExpression("1+2")),
            _ => throw new ArgumentException($"No sample for {typeName}", nameof(typeName)),
        };
    }
}
