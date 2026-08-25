namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class PosixParserTests
{
    public static TheoryData<string> ControlFlowSamples =>
    [
        "if true; then echo yes; fi",
        "if true\nthen\n  echo yes\nfi\n",
        "if [ -f x ]; then echo a; elif [ -f y ]; then echo b; else echo c; fi",
        "while read line; do echo \"$line\"; done",
        "until false; do sleep 1; done",
        "for f in a b c; do echo $f; done",
        "for f in *.txt\ndo\n  cat \"$f\"\ndone\n",
        "for i; do echo $i; done",
        "case $x in\n  a) echo A;;\n  b|c) echo BC;;\n  *) echo other;;\nesac\n",
        "case $x in (a) echo A;; esac",
        "greet() { echo hi; }",
        "greet () {\n  echo hi\n}\n",
        "function greet { echo hi; }",
        "function greet() { echo hi; }",
        "( cd /tmp && ls )",
        "{ echo a; echo b; }",
        "[[ -n \"$x\" && $y == z ]]",
        "(( count++ ))",
        "for (( i = 0; i < 10; i++ )); do echo $i; done",
        "files=(one two three)",
        "files=()",
        "diff <(sort a) <(sort b)",
        "time ls -la",
        "coproc mycoproc { read line; }",
        "select opt in a b; do echo $opt; break; done",
        "cat <<EOF\nline one\nline two\nEOF\n",
        "cat <<-'EOF'\n\tindented\n\tEOF\n",
        "cat <<EOF > out.txt\nbody\nEOF\n",
        "cat <<A <<B\nfirst\nA\nsecond\nB\n",
        "if true; then\n  # comment inside\n  echo yes\nfi\n",
        "for f in a; do echo $f; done | wc -l",
        "if true; then echo a; fi && echo b",
        "cat <<EOF\nunterminated body\n",
        "if true; then echo a",
        "case $x in a) echo A",
        "for f in",
        "while",
        "greet() {",
        "[[ unterminated",
        "(( unterminated",
        "[[ x\\",
        "[[ \"unterminated",
        "[[ 'unterminated",
        "(( (",
        "case x in a) ;;",
        "coproc",
        "time",
        "function",
        "{",
        "}",
    ];

    [Theory]
    [MemberData(nameof(ControlFlowSamples))]
    public void ParseText_RoundTripsControlFlowExactly(string text)
    {
        foreach (var dialect in new[] { ShellDialect.Sh, ShellDialect.Bash, ShellDialect.Zsh })
        {
            var tree = ShellSyntaxTree.ParseText(text, dialect);

            Assert.Equal(text, tree.Root.ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(ControlFlowSamples))]
    public void ParseText_ControlFlowNeverThrows(string text)
    {
        foreach (var dialect in new[] { ShellDialect.Sh, ShellDialect.Bash, ShellDialect.Zsh })
        {
            Assert.Null(Record.Exception(() => ShellSyntaxTree.ParseText(text, dialect)));
        }
    }

    [Fact]
    public void IfStatement_ExposesAllClauses()
    {
        var tree = ShellSyntaxTree.ParseText("if a; then b; elif c; then d; else e; fi", ShellDialect.Bash);

        var statement = Assert.IsType<PosixIfStatementSyntax>(Assert.Single(tree.Root.Statements.Statements));
        Assert.Empty(tree.Diagnostics);
        Assert.Equal("if", statement.IfKeyword.Text);
        Assert.Equal("a", Assert.IsType<ShellCommandSyntax>(statement.Condition.Statements[0]).NameValue);
        Assert.Equal("b", Assert.IsType<ShellCommandSyntax>(statement.Body.Statements[0]).NameValue);
        Assert.Single(statement.ElifClauses);
        Assert.Equal("d", Assert.IsType<ShellCommandSyntax>(statement.ElifClauses[0].Body.Statements[0]).NameValue);
        Assert.NotNull(statement.ElseClause);
        Assert.Equal("e", Assert.IsType<ShellCommandSyntax>(statement.ElseClause.Body.Statements[0]).NameValue);
    }

    [Fact]
    public void WhileAndUntil_AreDistinguished()
    {
        var loop = Assert.IsType<PosixWhileStatementSyntax>(ShellSyntaxTree.ParseCommand("while a; do b; done", ShellDialect.Bash));
        var until = Assert.IsType<PosixWhileStatementSyntax>(ShellSyntaxTree.ParseCommand("until a; do b; done", ShellDialect.Bash));

        Assert.False(loop.IsUntil);
        Assert.True(until.IsUntil);
    }

    [Fact]
    public void ForStatement_ExposesVariableAndItems()
    {
        var statement = Assert.IsType<PosixForStatementSyntax>(ShellSyntaxTree.ParseCommand("for f in a b c; do echo $f; done", ShellDialect.Bash));

        Assert.Equal("f", statement.VariableName);
        Assert.NotNull(statement.InKeyword);
        Assert.Equal(["a", "b", "c"], statement.Items.Select(item => item.Value));
        Assert.False(statement.IsSelect);
    }

    [Fact]
    public void ForStatement_WithoutIn_HasNoItems()
    {
        var statement = Assert.IsType<PosixForStatementSyntax>(ShellSyntaxTree.ParseCommand("for i; do echo $i; done", ShellDialect.Bash));

        Assert.Null(statement.InKeyword);
        Assert.Empty(statement.Items);
    }

    [Fact]
    public void SelectStatement_IsBashOnly()
    {
        Assert.IsType<PosixForStatementSyntax>(ShellSyntaxTree.ParseCommand("select o in a; do break; done", ShellDialect.Bash));
        Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("select o in a; do break; done", ShellDialect.Sh));
    }

    [Fact]
    public void CaseStatement_ExposesPatternsAndBodies()
    {
        var statement = Assert.IsType<PosixCaseStatementSyntax>(
            ShellSyntaxTree.ParseCommand("case $x in\n  a) echo A;;\n  b|c) echo BC;;\nesac", ShellDialect.Bash));

        Assert.Equal(2, statement.Clauses.Count);
        Assert.Equal(["a"], statement.Clauses[0].Patterns.Select(pattern => pattern.Value));
        Assert.Equal(["b", "c"], statement.Clauses[1].Patterns.Select(pattern => pattern.Value));
        Assert.Single(statement.Clauses[1].PatternSeparatorTokens);
        Assert.Equal(ShellSyntaxKind.SemicolonSemicolonToken, statement.Clauses[0].TerminatorToken?.Kind);
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(statement.Clauses[0].Body.Statements[0]).NameValue);
    }

    [Theory]
    [InlineData("greet() { echo hi; }", null)]
    [InlineData("function greet { echo hi; }", "function")]
    [InlineData("function greet() { echo hi; }", "function")]
    public void FunctionDefinition_SupportsBothForms(string text, string? expectedKeyword)
    {
        var definition = Assert.IsType<PosixFunctionDefinitionSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.Bash));

        Assert.Equal("greet", definition.Name);
        Assert.Equal(expectedKeyword, definition.FunctionKeyword?.Text);
        Assert.Equal(ShellSyntaxKind.PosixGroup, definition.Body.Kind);
    }

    [Fact]
    public void SubshellAndGroup_AreDistinguished()
    {
        var subshell = Assert.IsType<PosixCompoundStatementSyntax>(ShellSyntaxTree.ParseCommand("( ls )", ShellDialect.Bash));
        var group = Assert.IsType<PosixCompoundStatementSyntax>(ShellSyntaxTree.ParseCommand("{ ls; }", ShellDialect.Bash));

        Assert.True(subshell.IsSubshell);
        Assert.False(group.IsSubshell);
        Assert.Equal("ls", Assert.IsType<ShellCommandSyntax>(group.Statements.Statements[0]).NameValue);
    }

    [Fact]
    public void ExtendedTest_IsBashOnlyAndKeepsItsText()
    {
        var bash = Assert.IsType<PosixDelimitedExpressionStatementSyntax>(ShellSyntaxTree.ParseCommand("[[ -n $x ]]", ShellDialect.Bash));

        Assert.Equal(ShellSyntaxKind.PosixConditionalExpression, bash.Kind);
        Assert.Equal(" -n $x", bash.Expression.ToFullString());
        Assert.False(bash.IsArithmetic);

        Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("[[ -n $x ]]", ShellDialect.Sh));
    }

    [Fact]
    public void ExtendedTest_IgnoresClosingBracketsInsideQuotes()
    {
        var statement = Assert.IsType<PosixDelimitedExpressionStatementSyntax>(ShellSyntaxTree.ParseCommand("[[ $x == \"a]]b\" ]]", ShellDialect.Bash));

        Assert.Equal(" $x == \"a]]b\"", statement.Expression.ToFullString());
    }

    [Fact]
    public void ArithmeticCommand_KeepsItsText()
    {
        var statement = Assert.IsType<PosixDelimitedExpressionStatementSyntax>(ShellSyntaxTree.ParseCommand("(( i = (a + b) * 2 ))", ShellDialect.Bash));

        Assert.True(statement.IsArithmetic);
        Assert.Equal(" i = (a + b) * 2", statement.Expression.ToFullString());
    }

    [Fact]
    public void ArrayAssignment_ExposesElements()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("files=(one two three)", ShellDialect.Bash));
        var array = Assert.Single(command.ChildNodes.OfType<PosixArrayAssignmentSyntax>());

        Assert.Equal("files", array.Name);
        Assert.Equal(["one", "two", "three"], array.Elements.Select(element => element.Value));
    }

    [Fact]
    public void ProcessSubstitution_ParsesItsInnerCommand()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("diff <(sort a) >(tee b)", ShellDialect.Bash));
        var substitutions = command.DescendantNodes().OfType<PosixProcessSubstitutionSyntax>().ToArray();

        Assert.HasCount(2, substitutions);
        Assert.True(substitutions[0].IsInput);
        Assert.False(substitutions[1].IsInput);
        Assert.Equal("sort", Assert.IsType<ShellCommandSyntax>(substitutions[0].Statements.Statements[0]).NameValue);
    }

    [Fact]
    public void ProcessSubstitution_IsARedirectionInSh()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("diff <(sort a)", ShellDialect.Sh));

        Assert.Empty(command.DescendantNodes().OfType<PosixProcessSubstitutionSyntax>());
        Assert.Single(command.Redirections);
    }

    [Fact]
    public void HereDocument_CapturesBodyAndDelimiter()
    {
        const string Text = "cat <<EOF\nline one\nline two\nEOF\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.Root.Statements.Statements[0]);
        var hereDocument = Assert.Single(command.Redirections.Select(r => r.HereDocument).OfType<PosixHereDocumentSyntax>());

        Assert.Equal("\nline one\nline two\n", hereDocument.BodyToken.Text);
        Assert.Equal("EOF\n", hereDocument.DelimiterToken.Text);
        Assert.False(hereDocument.StripsLeadingTabs);
        Assert.False(hereDocument.IsQuotedDelimiter);
        Assert.Equal(Text, tree.Root.ToFullString());
    }

    [Fact]
    public void HereDocument_DashFormStripsTabsAndQuotedDelimiterIsDetected()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("cat <<-'EOF'\n\tbody\n\tEOF\n", ShellDialect.Bash));
        var hereDocument = Assert.Single(command.Redirections.Select(r => r.HereDocument).OfType<PosixHereDocumentSyntax>());

        Assert.True(hereDocument.StripsLeadingTabs);
        Assert.True(hereDocument.IsQuotedDelimiter);
    }

    [Fact]
    public void HereDocument_BodyStartsAfterTrailingRedirectionsOnTheSameLine()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("cat <<EOF > out.txt\nbody\nEOF\n", ShellDialect.Bash));

        Assert.Equal(2, command.Redirections.Count);
        Assert.Equal("\nbody\n", Assert.Single(command.Redirections.Select(r => r.HereDocument).OfType<PosixHereDocumentSyntax>()).BodyToken.Text);
    }

    [Fact]
    public void HereDocument_TwoOnOneLineAreReadInOrder()
    {
        var tree = ShellSyntaxTree.ParseText("cat <<A <<B\nfirst\nA\nsecond\nB\n", ShellDialect.Bash);
        var command = Assert.IsType<ShellCommandSyntax>(tree.Root.Statements.Statements[0]);
        var hereDocuments = command.Redirections.Select(r => r.HereDocument).OfType<PosixHereDocumentSyntax>().ToArray();

        Assert.HasCount(2, hereDocuments);
        Assert.Equal("\nfirst\n", hereDocuments[0].BodyToken.Text);
        Assert.Equal("second\n", hereDocuments[1].BodyToken.Text);
    }

    [Fact]
    public void HereDocument_WithoutClosingDelimiter_ReportsShell0011()
    {
        var tree = ShellSyntaxTree.ParseText("cat <<EOF\nbody\n", ShellDialect.Bash);

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "SHELL0011");
        Assert.Equal("cat <<EOF\nbody\n", tree.Root.ToFullString());
    }

    [Fact]
    public void TimeAndCoproc_ArePrefixStatements()
    {
        var timed = Assert.IsType<PosixPrefixedStatementSyntax>(ShellSyntaxTree.ParseCommand("time ls -la", ShellDialect.Bash));
        Assert.Equal(ShellSyntaxKind.PosixTimeStatement, timed.Kind);
        Assert.Equal("ls", Assert.IsType<ShellCommandSyntax>(timed.Statement).NameValue);

        var coproc = Assert.IsType<PosixPrefixedStatementSyntax>(ShellSyntaxTree.ParseCommand("coproc worker { read x; }", ShellDialect.Bash));
        Assert.Equal("worker", coproc.NameToken?.Text);
    }

    [Fact]
    public void CompoundStatements_ComposeWithPipesAndOperators()
    {
        var pipeline = Assert.IsType<ShellPipelineSyntax>(ShellSyntaxTree.ParseCommand("for f in a; do echo $f; done | wc -l", ShellDialect.Bash));

        Assert.Equal(2, pipeline.Commands.Count);
        Assert.IsType<PosixForStatementSyntax>(pipeline.Commands[0]);
    }

    [Fact]
    public void UnclosedCompound_ReportsADiagnosticAndStillRoundTrips()
    {
        const string Text = "if true; then echo a";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.Bash);

        Assert.Contains(tree.Diagnostics, diagnostic => diagnostic.Id == "SHELL0012");
        Assert.Equal(Text, tree.Root.ToFullString());
    }

    [Fact]
    public void ReservedWordsAreOnlyReservedInCommandPosition()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo if then fi", ShellDialect.Bash));

        Assert.Equal(["if", "then", "fi"], command.Arguments.Select(argument => argument.Value));
    }

    [Fact]
    public void NestedCompounds_AreParsed()
    {
        var statement = Assert.IsType<PosixIfStatementSyntax>(
            ShellSyntaxTree.ParseCommand("if a; then for f in x; do while b; do c; done; done; fi", ShellDialect.Bash));

        var forStatement = Assert.IsType<PosixForStatementSyntax>(statement.Body.Statements[0]);
        Assert.IsType<PosixWhileStatementSyntax>(forStatement.Body.Statements[0]);
    }
}
