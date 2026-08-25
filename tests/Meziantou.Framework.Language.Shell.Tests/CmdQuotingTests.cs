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
    // A percent that closes nothing is kept as text. cmd itself deletes it when running a batch file, so `echo 50%`
    // prints `50`; the tree keeps the character because the text is what round-trips.
    [InlineData("50%", "50%")]
    public void Value_MatchesCmdSemantics(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    // The expectations below were produced by passing the same argument text through cmd.exe on Windows and reading
    // back what the launched process received.

    [Theory]
    [InlineData("a^&b", "a&b")]
    [InlineData("a^|b", "a|b")]
    [InlineData("a^^b", "a^b")]
    [InlineData("a^<b", "a<b")]
    [InlineData("a^>b", "a>b")]
    [InlineData("a^ b", "a b")]
    [InlineData("a^ab", "aab")]
    [InlineData("^plain", "plain")]
    public void CaretEscapesTheNextCharacter(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Theory]
    // Characters that other shells treat as syntax are ordinary text to cmd.
    [InlineData("a=b", "a=b")]
    [InlineData("a;b", "a;b")]
    [InlineData("a,b", "a,b")]
    [InlineData("a[b", "a[b")]
    [InlineData("a]b", "a]b")]
    [InlineData("a{b", "a{b")]
    [InlineData("a}b", "a}b")]
    [InlineData("a#b", "a#b")]
    [InlineData("a'b", "a'b")]
    [InlineData("a`b", "a`b")]
    [InlineData("a~b", "a~b")]
    [InlineData("a+b", "a+b")]
    [InlineData("a@b", "a@b")]
    [InlineData("a$b", "a$b")]
    [InlineData("a!b", "a!b")]
    [InlineData("a(b", "a(b")]
    [InlineData("a)b", "a)b")]
    [InlineData("C:\\path\\to\\file", "C:\\path\\to\\file")]
    [InlineData(".\\rel\\path", ".\\rel\\path")]
    [InlineData("--flag", "--flag")]
    [InlineData("/flag", "/flag")]
    // Quotes only group; they are removed from the value.
    [InlineData("\"C:\\Program Files\\app.exe\"", "C:\\Program Files\\app.exe")]
    [InlineData("a\"b\"c", "abc")]
    [InlineData("\"a b\"c", "a bc")]
    [InlineData("c\"a b\"", "ca b")]
    public void BareArgument_MatchesCmdSemantics(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Theory]
    [InlineData("%%i")]
    [InlineData("%%A")]
    public void DoubledPercentBeforeANameIsAForLoopVariable(string argumentText)
    {
        // A batch file writes `for %%i in (...) do echo %%i`, so `%%name` is modelled as a loop variable and its value
        // is unknown. Outside a `for` body cmd would resolve the same text to a literal `%` followed by the name.
        var word = FirstArgument(argumentText);
        var reference = Assert.Single(word.Parts.OfType<CmdVariableReferenceSyntax>());

        Assert.Null(word.Value);
        Assert.Null(reference.CloseToken);
    }

    [Fact]
    public void DoubledPercentBeforeANonNameIsAnEscapedPercent()
    {
        var word = FirstArgument("100%%");

        Assert.Equal("100%", word.Value);
        Assert.Single(word.Parts.OfType<ShellEscapeSequenceSyntax>());
    }

    [Theory]
    [InlineData("echo a&b", 2)]
    [InlineData("echo a\r\necho b", 2)]
    [InlineData("echo a\r\n\r\necho b", 2)]
    // `&&` and `||` build one command list rather than two statements.
    [InlineData("echo a&&b", 1)]
    [InlineData("echo a||b", 1)]
    public void SeparatorsSplitTheStatementList(string text, int expectedStatements)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Cmd);

        Assert.Equal(expectedStatements, tree.Root.Statements.Statements.Count);
        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Fact]
    public void PipeBuildsAPipeline()
    {
        var pipeline = Assert.IsType<ShellPipelineSyntax>(ShellSyntaxTree.ParseCommand("dir | findstr foo", ShellDialect.Cmd));

        Assert.Equal(2, pipeline.Commands.Count);
    }

    [Fact]
    public void TrailingCaretJoinsTwoLinesAfterWhitespace()
    {
        const string Text = "echo x ^\r\ny\r\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Cmd);

        Assert.Equal(Text, tree.Root.ToFullString());
        Assert.Single(tree.Root.Statements.Statements);
        Assert.Contains(tree.Root.DescendantTrivia(), trivia => trivia.Kind == ShellSyntaxKind.LineContinuationTrivia);
    }

    [Fact]
    public void TrailingCaretJoinsTwoLinesInsideAWord()
    {
        // `echo a^` followed by `b` on the next line echoes `ab`: the caret escapes the line break.
        const string Text = "echo a^\r\nb\r\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Cmd);

        Assert.Equal(Text, tree.Root.ToFullString());
        var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Equal("ab", Assert.Single(command.Arguments).Value);
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
