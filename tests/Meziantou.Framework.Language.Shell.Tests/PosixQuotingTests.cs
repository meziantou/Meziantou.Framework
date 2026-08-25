namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// Quoting and escaping semantics for the POSIX family, checked against what bash actually resolves a word to.
/// Round-tripping alone would not catch a wrong <see cref="ShellWordSyntax.Value"/>.
/// </summary>
public sealed class PosixQuotingTests
{
    private static ShellWordSyntax FirstArgument(string argumentText, ShellDialect? dialect = null)
    {
        var statement = ShellSyntaxTree.ParseCommand("echo " + argumentText, dialect ?? ShellDialect.Bash);

        return Assert.IsType<ShellCommandSyntax>(statement).Arguments[0];
    }

    [Theory]
    // Unquoted: a backslash escapes any single character.
    [InlineData(@"a\ b", "a b")]
    [InlineData(@"a\nb", "anb")]
    [InlineData(@"a\\b", @"a\b")]
    [InlineData(@"\$HOME", "$HOME")]
    [InlineData(@"\'", "'")]
    // Single quotes: everything is literal, including backslashes and dollars.
    [InlineData("'a\\b'", @"a\b")]
    [InlineData("'$HOME'", "$HOME")]
    [InlineData("'a\"b'", "a\"b")]
    [InlineData("''", "")]
    // Double quotes: a backslash is special only before $ ` " \ and a line break.
    [InlineData("\"a\\\"b\"", "a\"b")]
    [InlineData("\"a\\\\b\"", @"a\b")]
    [InlineData("\"a\\$b\"", "a$b")]
    [InlineData("\"a\\`b\"", "a`b")]
    [InlineData("\"a\\qb\"", @"a\qb")]
    [InlineData("\"a\\1b\"", @"a\1b")]
    [InlineData("\"\"", "")]
    [InlineData("\"it's\"", "it's")]
    // Adjacent quoted and unquoted runs concatenate into one word.
    [InlineData("a''b", "ab")]
    [InlineData("\"a\"'b'c", "abc")]
    [InlineData(@"'it'\''s'", "it's")]
    [InlineData("''''", "")]
    public void Value_MatchesShellSemantics(string argumentText, string expected)
    {
        Assert.Equal(expected, FirstArgument(argumentText).Value);
    }

    [Fact]
    public void DoubleQuotedString_MaySpanLines()
    {
        Assert.Equal("a\nb", FirstArgument("\"a\nb\"").Value);
    }

    [Fact]
    public void SingleQuotedString_MaySpanLines()
    {
        Assert.Equal("a\nb", FirstArgument("'a\nb'").Value);
    }

    [Theory]
    // ANSI-C quoting resolves escapes; it is a bash and zsh extension.
    [InlineData(@"$'a\tb'", "a\tb")]
    [InlineData(@"$'a\nb'", "a\nb")]
    [InlineData(@"$'a\\b'", @"a\b")]
    [InlineData(@"$'it\'s'", "it's")]
    [InlineData(@"$'\x41'", "A")]
    [InlineData(@"$'\101'", "A")]
    [InlineData(@"$'A'", "A")]
    [InlineData(@"$'\q'", @"\q")]
    [InlineData(@"$''", "")]
    public void AnsiCQuoting_ResolvesEscapes(string argumentText, string expected)
    {
        var word = FirstArgument(argumentText);

        Assert.Equal(expected, word.Value);
        Assert.True(Assert.IsType<ShellQuotedStringSyntax>(Assert.Single(word.Parts)).IsAnsiC);
    }

    [Fact]
    public void LocaleQuoting_BehavesLikeADoubleQuotedString()
    {
        Assert.Equal("msg", FirstArgument("$\"msg\"").Value);
        Assert.Null(FirstArgument("$\"$HOME\"").Value);
    }

    [Fact]
    public void DollarQuoting_IsNotAvailableInSh()
    {
        // In sh the `$` is literal text followed by an ordinary single-quoted string.
        var word = FirstArgument(@"$'a\tb'", ShellDialect.Sh);

        Assert.Equal(@"$a\tb", word.Value);
        Assert.False(word.Parts.OfType<ShellQuotedStringSyntax>().Single().IsAnsiC);
    }

    [Fact]
    public void UnterminatedQuotes_ReportShell0003()
    {
        foreach (var text in new[] { "echo 'x", "echo \"x", "echo $'x" })
        {
            var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Bash);

            Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "SHELL0003");
            Assert.Equal(text, tree.Root.ToFullString());
        }
    }

    [Fact]
    public void ExpansionsMakeTheValueUnknown()
    {
        Assert.Null(FirstArgument("\"$HOME\"").Value);
        Assert.Null(FirstArgument("$HOME").Value);
        Assert.Null(FirstArgument("\"a`date`b\"").Value);
        Assert.Null(FirstArgument("\"a$(date)b\"").Value);
        Assert.Null(FirstArgument("${HOME}").Value);
    }

    [Fact]
    public void QuotedStrings_ReportWhetherTheyExpand()
    {
        Assert.True(Assert.IsType<ShellQuotedStringSyntax>(Assert.Single(FirstArgument("'x'").Parts)).IsVerbatim);
        Assert.False(Assert.IsType<ShellQuotedStringSyntax>(Assert.Single(FirstArgument("\"x\"").Parts)).IsVerbatim);
        Assert.False(Assert.IsType<ShellQuotedStringSyntax>(Assert.Single(FirstArgument(@"$'x'").Parts)).IsVerbatim);
    }

    [Fact]
    public void QuotedWhitespace_DoesNotSplitTheWord()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo \"a b\" 'c d' e\\ f", ShellDialect.Bash));

        Assert.Equal(["a b", "c d", "e f"], command.Arguments.Select(argument => argument.Value));
    }

    [Fact]
    public void QuotedOperators_AreNotOperators()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("""echo '|' "&&" \; '>'""", ShellDialect.Bash));

        Assert.Equal(["|", "&&", ";", ">"], command.Arguments.Select(argument => argument.Value));
    }

    [Fact]
    public void CommentMarkerInsideQuotes_IsNotAComment()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo '# not a comment' \"# also not\"", ShellDialect.Bash));

        Assert.Equal(["# not a comment", "# also not"], command.Arguments.Select(argument => argument.Value));
        Assert.Empty(command.DescendantComments());
    }

    [Fact]
    public void LineContinuation_JoinsAWordAcrossLines()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo one \\\n  two", ShellDialect.Bash));

        Assert.Equal(["one", "two"], command.Arguments.Select(argument => argument.Value));
        Assert.Contains(command.DescendantTrivia(), trivia => trivia.Kind == ShellSyntaxKind.LineContinuationTrivia);
    }

    [Fact]
    public void ParameterExpansion_MayContainAQuotedBrace()
    {
        const string Text = "echo ${var:-\"}\"}";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(Text, tree.Root.ToFullString());
        Assert.Equal("var:-\"}\"", Assert.Single(tree.Root.DescendantNodes().OfType<ShellVariableReferenceSyntax>()).Name);
    }

    [Fact]
    public void CommandSubstitution_MayContainAQuotedCloseParen()
    {
        const string Text = "echo $(echo \")\")";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(Text, tree.Root.ToFullString());
    }

    // The expectations below were produced by running the same inputs through bash and comparing byte for byte.

    [Fact]
    public void LineContinuationInsideAWord_JoinsIt()
    {
        var word = FirstArgument("x\\\ny");

        Assert.Equal("xy", word.Value);
    }

    [Fact]
    public void BacktickSubstitution_EndsAtItsClosingBacktick()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo `date` tail", ShellDialect.Bash));
        var substitution = Assert.Single(command.DescendantNodes().OfType<ShellCommandSubstitutionSyntax>());

        Assert.True(substitution.IsBackquoted);
        Assert.Equal("date", Assert.IsType<ShellCommandSyntax>(Assert.Single(substitution.Statements.Statements)).NameValue);
        Assert.Equal(["tail"], command.Arguments.Skip(1).Select(argument => argument.Value));
    }

    [Fact]
    public void NestedBacktickSubstitutionsAreNotInvented()
    {
        var tree = ShellSyntaxTree.ParseText("a=`one`; b=`two`", ShellDialect.Bash);

        Assert.Empty(tree.Diagnostics);
        Assert.HasCount(2, tree.Root.DescendantNodes().OfType<ShellCommandSubstitutionSyntax>());
    }

    [Theory]
    // A reserved word is only reserved when it forms a whole word.
    [InlineData("for$(cmd)")]
    [InlineData("if${VAR}")]
    [InlineData("while'x'")]
    [InlineData("function${VAR}")]
    public void KeywordGluedToAnExpansion_IsACommandName(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Bash);

        Assert.Empty(tree.Diagnostics);
        Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
    }

    [Theory]
    [InlineData("[[$x")]
    [InlineData("[[*")]
    public void ExtendedTestGluedToAWord_IsACommandName(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Bash);

        Assert.Empty(tree.Diagnostics);
        Assert.IsType<ShellCommandSyntax>(Assert.Single(tree.Root.Statements.Statements));
    }

    [Theory]
    [InlineData("time")]
    [InlineData("coproc")]
    public void TimeAndCoprocAreCompleteStatementsOnTheirOwn(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Bash);

        Assert.Empty(tree.Diagnostics);
        var statement = Assert.IsType<PosixPrefixedStatementSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.IsType<ShellEmptyStatementSyntax>(statement.Statement);
    }
}
