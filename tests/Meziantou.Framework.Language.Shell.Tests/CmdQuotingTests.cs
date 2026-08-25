namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>Quoting, escaping, and expansion semantics for cmd.exe.</summary>
public sealed class CmdQuotingTests
{
    private static ShellWordSyntax FirstArgument(string argumentText)
    {
        var statement = ShellSyntaxTree.ParseCommand("echo " + argumentText, ShellDialect.Cmd);

        return Assert.IsType<ShellCommandSyntax>(statement).Arguments[0];
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("\"a b\"", "a b")]
    [InlineData("\"\"", "")]
    // The caret escapes the next character.
    [InlineData("a^&b", "a&b")]
    [InlineData("a^^b", "a^b")]
    [InlineData("a^|b", "a|b")]
    [InlineData("a^ b", "a b")]
    // A doubled percent is a literal percent in a batch file.
    [InlineData("100%%", "100%")]
    // A percent that closes nothing stays literal.
    [InlineData("50%", "50%")]
    public void Value_MatchesCmdSemantics(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Fact]
    public void PercentExpansion_MakesTheValueUnknown()
    {
        Assert.Null(FirstArgument("%PATH%").Value);
        Assert.Null(FirstArgument("\"a %B% c\"").Value);
        Assert.Null(FirstArgument("!DELAYED!").Value);
    }

    [Fact]
    public void QuotedOperators_AreNotOperators()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo \"a & b\" \"c | d\"", ShellDialect.Cmd));

        Assert.Equal(["a & b", "c | d"], command.Arguments.Select(argument => argument.Value));
        Assert.Empty(command.Redirections);
    }

    [Fact]
    public void CaretEscapedOperator_DoesNotSplitTheCommand()
    {
        var statement = ShellSyntaxTree.ParseCommand("echo a^&b", ShellDialect.Cmd);

        var command = Assert.IsType<ShellCommandSyntax>(statement);
        Assert.Equal("a&b", Assert.Single(command.Arguments).Value);
    }

    [Theory]
    [InlineData("%PATH%", "PATH", false)]
    [InlineData("%1", "1", false)]
    [InlineData("%*", "*", false)]
    [InlineData("%~dp0", "~dp0", false)]
    [InlineData("%~nx1", "~nx1", false)]
    [InlineData("!COUNT!", "COUNT", true)]
    public void VariableReferences_AreClassified(string text, string expectedName, bool expectedDelayed)
    {
        var word = FirstArgument(text);
        var reference = Assert.Single(word.Parts.OfType<CmdVariableReferenceSyntax>());

        Assert.Equal(expectedName, reference.Name);
        Assert.Equal(expectedDelayed, reference.IsDelayed);
        Assert.Equal(text, word.ToFullString().TrimStart());
    }

    [Fact]
    public void PercentInsideQuotes_IsStillAnExpansion()
    {
        var word = FirstArgument("\"path=%ROOT%\\bin\"");
        var quoted = Assert.IsType<ShellQuotedStringSyntax>(Assert.Single(word.Parts));

        Assert.Single(quoted.Parts.OfType<CmdVariableReferenceSyntax>());
    }

    [Fact]
    public void UnterminatedQuote_ReportsShell0003()
    {
        const string Text = "echo \"unterminated";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Cmd);

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "SHELL0003");
        Assert.Equal(Text, tree.Root.ToFullString());
    }

    [Fact]
    public void QuotesDoNotSpanLines()
    {
        const string Text = "echo \"unterminated\r\necho next\r\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Cmd);

        Assert.Equal(2, tree.Root.Statements.Statements.Count);
        Assert.Equal(Text, tree.Root.ToFullString());
    }

    [Fact]
    public void RemAndDoubleColonComments_AreCaseInsensitiveAndLineScoped()
    {
        var tree = ShellSyntaxTree.ParseText("REM upper\r\nrem lower\r\n:: colon\r\necho hi\r\n", ShellDialect.Cmd);
        var comments = tree.Root.DescendantTrivia()
            .Where(trivia => trivia.Kind is ShellSyntaxKind.CmdRemCommentTrivia or ShellSyntaxKind.CmdDoubleColonCommentTrivia)
            .ToArray();

        Assert.HasCount(3, comments);
        Assert.Single(tree.Root.Statements.Statements);
    }

    [Fact]
    public void RemPrefixOfALongerWord_IsNotAComment()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("remove file.txt", ShellDialect.Cmd));

        Assert.Equal("remove", command.NameValue);
        Assert.DoesNotContain(command.DescendantTrivia(), trivia => trivia.Kind == ShellSyntaxKind.CmdRemCommentTrivia);
    }

    [Fact]
    public void SetWithQuotedAssignment_KeepsTheWholeAssignmentInTheName()
    {
        // `set "NAME=value"` is the safe form; the quote is part of the text that reaches the variable name.
        var tree = ShellSyntaxTree.ParseText("set \"NAME=value with spaces\"", ShellDialect.Cmd);

        Assert.Equal("set \"NAME=value with spaces\"", tree.Root.ToFullString());
        Assert.IsType<CmdSetStatementSyntax>(tree.Root.Statements.Statements[0]);
    }

    [Fact]
    public void GlobCharacters_AreWordParts()
    {
        var word = FirstArgument("*.txt");

        Assert.Single(word.Parts.OfType<ShellGlobSyntax>());
        Assert.Equal("*.txt", word.Value);
    }
}
