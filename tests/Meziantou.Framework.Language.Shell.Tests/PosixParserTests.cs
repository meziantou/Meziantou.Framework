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

            Assert.Equal(text, tree.GetRoot().ToFullString());
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

        var statement = Assert.IsType<PosixIfStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));
        Assert.Empty(tree.GetDiagnostics());
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
        Assert.True(statement.InKeyword.IsPresent());
        Assert.Equal(["a", "b", "c"], statement.Items.Select(item => item.Value));
        Assert.False(statement.IsSelect);
    }

    [Fact]
    public void ForStatement_WithoutIn_HasNoItems()
    {
        var statement = Assert.IsType<PosixForStatementSyntax>(ShellSyntaxTree.ParseCommand("for i; do echo $i; done", ShellDialect.Bash));

        Assert.False(statement.InKeyword.IsPresent());
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
        Assert.Equal(SyntaxKind.SemicolonSemicolonToken, statement.Clauses[0].TerminatorToken.Kind());
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
        Assert.Equal(expectedKeyword ?? "", definition.FunctionKeyword.Text);
        Assert.Equal(SyntaxKind.PosixGroup, definition.Body.Kind());
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

        Assert.Equal(SyntaxKind.PosixConditionalExpression, bash.Kind());
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
        var array = Assert.Single(command.ChildNodes().OfType<PosixArrayAssignmentSyntax>());

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
        var command = Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[0]);
        var hereDocument = Assert.Single(command.Redirections.Select(r => r.HereDocument).OfType<PosixHereDocumentSyntax>());

        Assert.Equal("\nline one\nline two\n", hereDocument.BodyToken.Text);
        Assert.Equal("EOF\n", hereDocument.DelimiterToken.Text);
        Assert.False(hereDocument.StripsLeadingTabs);
        Assert.False(hereDocument.IsQuotedDelimiter);
        Assert.Equal(Text, tree.GetRoot().ToFullString());
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
        var command = Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[0]);
        var hereDocuments = command.Redirections.Select(r => r.HereDocument).OfType<PosixHereDocumentSyntax>().ToArray();

        Assert.HasCount(2, hereDocuments);
        Assert.Equal("\nfirst\n", hereDocuments[0].BodyToken.Text);
        Assert.Equal("second\n", hereDocuments[1].BodyToken.Text);
    }

    [Fact]
    public void HereDocument_WithoutClosingDelimiter_ReportsShell0011()
    {
        var tree = ShellSyntaxTree.ParseText("cat <<EOF\nbody\n", ShellDialect.Bash);

        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "SHELL0011");
        Assert.Equal("cat <<EOF\nbody\n", tree.GetRoot().ToFullString());
    }

    [Fact]
    public void TimeAndCoproc_ArePrefixStatements()
    {
        var timed = Assert.IsType<PosixPrefixedStatementSyntax>(ShellSyntaxTree.ParseCommand("time ls -la", ShellDialect.Bash));
        Assert.Equal(SyntaxKind.PosixTimeStatement, timed.Kind());
        Assert.Equal("ls", Assert.IsType<ShellCommandSyntax>(timed.Statement).NameValue);

        var coproc = Assert.IsType<PosixPrefixedStatementSyntax>(ShellSyntaxTree.ParseCommand("coproc worker { read x; }", ShellDialect.Bash));
        Assert.Equal("worker", coproc.NameToken.Text);
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

        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "SHELL0012");
        Assert.Equal(Text, tree.GetRoot().ToFullString());
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

    // ---- spec compliance: every expectation below was checked against dash, bash, and zsh with `-n` ----

    private static ShellSyntaxTree ParseDialect(string text, string dialectName)
    {
        Assert.True(ShellDialect.TryParse(dialectName, out var dialect));

        return ShellSyntaxAssert.TextIsFaithful(text, dialect);
    }

    private static ShellSyntaxTree ParsesWithoutDiagnostics(string text, string dialectName)
    {
        var tree = ParseDialect(text, dialectName);
        Assert.Empty(tree.GetDiagnostics());

        return tree;
    }

    private static Diagnostic ReportsSingleError(string text, string dialectName, string id)
    {
        var tree = ParseDialect(text, dialectName);
        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal(id, diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);

        return diagnostic;
    }

    [Theory]
    [InlineData("echo a; fi", "fi")]
    [InlineData("echo a; done", "done")]
    [InlineData("echo a; then", "then")]
    [InlineData("esac", "esac")]
    [InlineData("do echo", "do")]
    [InlineData("}", "}")]
    [InlineData("echo a\nelse\necho b", "else")]
    [InlineData("{ fi; }", "fi")]
    [InlineData("f() { done; }", "done")]
    [InlineData("if true; then echo; fi; then", "then")]
    public void ReservedWordOutsideItsConstruct_IsReported(string text, string word)
    {
        foreach (var dialect in new[] { "sh", "bash", "zsh" })
        {
            var diagnostic = ReportsSingleError(text, dialect, "SHELL0002");
            Assert.Equal($"Unexpected '{word}'.", diagnostic.Message);
            Assert.Equal(word, text.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length));
        }
    }

    [Theory]
    [InlineData("in", "sh")]
    [InlineData("echo; in", "bash")]
    public void InOutsideCaseAndFor_IsReportedOutsideZsh(string text, string dialect)
    {
        ReportsSingleError(text, dialect, "SHELL0002");
        ParsesWithoutDiagnostics(text, "zsh");
    }

    [Theory]
    [InlineData("{ echo; } foo", "foo")]
    [InlineData("if true; then echo; fi echo", "echo")]
    [InlineData("(a) b", "b")]
    [InlineData("while a; do b; done c", "c")]
    [InlineData("case x in a) ;; esac esac", "esac")]
    [InlineData("(( 1 )) foo", "foo")]
    [InlineData("[[ a ]] foo", "foo")]
    [InlineData("[[ a ]] ]]", "]]")]
    [InlineData("x=1 (echo)", "(")]
    [InlineData("f() { :; } g", "g")]
    public void CommandFollowingACompoundCommandOnTheSameLine_IsReported(string text, string unexpected)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            var diagnostic = ReportsSingleError(text, dialect, "SHELL0002");
            Assert.Equal($"Unexpected '{unexpected}'.", diagnostic.Message);
        }
    }

    [Theory]
    [InlineData("echo a (b)")]
    [InlineData("echo $(echo) (a)")]
    public void SubshellFollowingAWordWithoutASeparator_IsReportedOutsideZsh(string text)
    {
        ReportsSingleError(text, "sh", "SHELL0002");
        ReportsSingleError(text, "bash", "SHELL0002");
    }

    [Theory]
    [InlineData("if (true) then echo; fi")]
    [InlineData("while (true) do echo; done")]
    [InlineData("if { true; } then echo; fi")]
    [InlineData("if true; then (echo) fi")]
    [InlineData("if true; then { echo; } fi")]
    [InlineData("case x in a) (echo) ;; esac")]
    [InlineData("(a) | b")]
    [InlineData("(a) && b")]
    [InlineData("(a)& b")]
    [InlineData("{ (echo) }")]
    [InlineData("f() { :; } && g")]
    public void ReservedWordOrOperatorRightAfterAClosingDelimiter_IsAccepted(string text)
    {
        foreach (var dialect in new[] { "sh", "bash", "zsh" })
        {
            ParsesWithoutDiagnostics(text, dialect);
        }
    }

    [Theory]
    [InlineData("if (( 1 )) then echo; fi")]
    [InlineData("if [[ a ]] then echo; fi")]
    public void ReservedWordRightAfterADelimitedExpression_IsOnlyAcceptedByZsh(string text)
    {
        var diagnostic = ReportsSingleError(text, "bash", "SHELL0002");
        Assert.Equal("Unexpected 'then'.", diagnostic.Message);
        ParsesWithoutDiagnostics(text, "zsh");
    }

    [Theory]
    [InlineData("if true; then fi")]
    [InlineData("if true; then echo; else fi")]
    [InlineData("while true; do done")]
    [InlineData("until true; do done")]
    [InlineData("for x in a; do done")]
    [InlineData("{ }")]
    [InlineData("( )")]
    [InlineData("f() { }")]
    public void EmptyCompoundList_IsReportedOutsideZsh(string text)
    {
        ReportsSingleError(text, "sh", "SHELL0001");
        ReportsSingleError(text, "bash", "SHELL0001");
        ParsesWithoutDiagnostics(text, "zsh");
    }

    [Theory]
    [InlineData("echo $( )")]
    [InlineData("echo ``")]
    [InlineData("case x in a) ;; esac")]
    [InlineData("case x in a) esac")]
    public void EmptyListsTheGrammarAllows_AreAccepted(string text)
    {
        foreach (var dialect in new[] { "sh", "bash", "zsh" })
        {
            ParsesWithoutDiagnostics(text, dialect);
        }
    }

    [Theory]
    [InlineData(";")]
    [InlineData("echo a; ; echo b")]
    [InlineData("echo a & ;")]
    [InlineData("if ; then echo; fi")]
    [InlineData("if true; then echo; elif; then echo; fi")]
    public void StraySeparator_IsAnEmptyStatementInZsh(string text)
    {
        var diagnostic = ReportsSingleError(text, "bash", "SHELL0002");
        Assert.Equal("Unexpected ';'.", diagnostic.Message);
        var tree = ParsesWithoutDiagnostics(text, "zsh");
        Assert.Contains(tree.GetRoot().DescendantNodes(), node => node is ShellEmptyStatementSyntax);
    }

    [Fact]
    public void StrayAmpersand_IsReportedEvenInZsh()
    {
        var diagnostic = ReportsSingleError("& echo a", "zsh", "SHELL0002");
        Assert.Equal("Unexpected '&'.", diagnostic.Message);
    }

    [Theory]
    [InlineData("! ! true", 2)]
    [InlineData("echo a | ! cat", 9)]
    public void BangInsideAPipeline_IsReported(string text, int position)
    {
        foreach (var dialect in new[] { "sh", "bash", "zsh" })
        {
            var diagnostic = ReportsSingleError(text, dialect, "SHELL0002");
            Assert.Equal("Unexpected '!'.", diagnostic.Message);
            Assert.Equal(position, diagnostic.Location.SourceSpan.Start);
        }
    }

    [Theory]
    [InlineData("echo a && ! cat")]
    [InlineData("time ! true")]
    [InlineData("! { a; }")]
    public void BangStartingAPipeline_IsAccepted(string text)
    {
        ParsesWithoutDiagnostics(text, "bash");
    }

    [Theory]
    [InlineData(")", ")")]
    [InlineData("echo a )", ")")]
    [InlineData("echo a;; echo b", ";;")]
    [InlineData("echo a;& echo b", ";&")]
    public void StrayClosingToken_IsReportedOnce(string text, string token)
    {
        foreach (var dialect in new[] { "sh", "bash", "zsh" })
        {
            var diagnostic = ReportsSingleError(text, dialect, "SHELL0002");
            Assert.Equal($"Unexpected '{token}'.", diagnostic.Message);
        }
    }

    [Theory]
    [InlineData("{ echo a; } > out", SyntaxKind.PosixGroup, 1)]
    [InlineData("(cd /tmp) 2>/dev/null >&2", SyntaxKind.PosixSubshell, 2)]
    [InlineData("while read x; do echo $x; done < file", SyntaxKind.PosixWhileStatement, 1)]
    [InlineData("if true; then echo; fi > out 2>&1", SyntaxKind.PosixIfStatement, 2)]
    [InlineData("for f in a; do :; done >> log", SyntaxKind.PosixForStatement, 1)]
    [InlineData("case x in a) ;; esac <in", SyntaxKind.PosixCaseStatement, 1)]
    [InlineData("[[ -f x ]] 2>/dev/null", SyntaxKind.PosixConditionalExpression, 1)]
    [InlineData("(( x++ )) >&2", SyntaxKind.PosixArithmeticCommand, 1)]
    public void RedirectionAfterACompoundCommand_BelongsToIt(string text, SyntaxKind innerKind, int redirectionCount)
    {
        var tree = ParsesWithoutDiagnostics(text, "bash");
        var redirected = Assert.IsType<PosixRedirectedStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));

        Assert.Equal(innerKind, redirected.Statement.Kind());
        Assert.Equal(redirectionCount, redirected.Redirections.Count);
    }

    [Fact]
    public void RedirectionAfterAFunctionBody_BelongsToTheBody()
    {
        var tree = ParsesWithoutDiagnostics("f() { echo; } > log", "bash");
        var definition = Assert.IsType<PosixFunctionDefinitionSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));
        var body = Assert.IsType<PosixRedirectedStatementSyntax>(definition.Body);

        Assert.Equal(SyntaxKind.PosixGroup, body.Statement.Kind());
        Assert.Equal("log", Assert.Single(body.Redirections).Target!.Value);
    }

    [Fact]
    public void RedirectedCompoundCommand_ComposesWithAPipeline()
    {
        var tree = ParsesWithoutDiagnostics("{ echo a; } 2>&1 | tee log", "bash");
        var pipeline = Assert.IsType<ShellPipelineSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));

        Assert.Equal(2, pipeline.Commands.Count);
        Assert.IsType<PosixRedirectedStatementSyntax>(pipeline.Commands[0]);
    }

    [Fact]
    public void HereDocument_BodyStartsAfterEveryCommandOnTheLine()
    {
        var tree = ParsesWithoutDiagnostics("cat <<EOF; echo a\nbody\nEOF\necho b\n", "bash");
        var statements = tree.GetRoot().Statements.Statements;

        Assert.HasCount(4, statements);
        Assert.Equal("cat", Assert.IsType<ShellCommandSyntax>(statements[0]).NameValue);
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(statements[1]).NameValue);
        var hereDocument = Assert.IsType<PosixHereDocumentSyntax>(statements[2]);
        Assert.Equal("\nbody\n", hereDocument.BodyToken.Text);
        Assert.Same(statements[0].DescendantNodes().OfType<ShellRedirectionSyntax>().Single(), hereDocument.Redirection);
        Assert.Same(hereDocument, hereDocument.Redirection!.HereDocument);
        Assert.Equal("b", Assert.IsType<ShellCommandSyntax>(statements[3]).Arguments.Single().Value);
    }

    [Theory]
    [InlineData("if cat <<EOF; then\nbody\nEOF\necho; fi\n")]
    [InlineData("cat <<EOF &&\nbody\nEOF\necho b\n")]
    [InlineData("cat <<EOF ||\nbody\nEOF\necho b\n")]
    [InlineData("cat <<EOF |\nbody\nEOF\ngrep x\n")]
    [InlineData("cat <<EOF | while read l; do\nbody\nEOF\necho $l\ndone\n")]
    [InlineData("while read l; do echo $l; done <<EOF\nbody\nEOF\n")]
    [InlineData("cat <<A; cat <<B\na\nA\nb\nB\n")]
    [InlineData("f() {\n  cat <<EOF\nbody }\nEOF\n}\n")]
    public void HereDocument_BodyIsReadFromTheNextLineBreak(string text)
    {
        foreach (var dialect in new[] { "sh", "bash", "zsh" })
        {
            var tree = ParsesWithoutDiagnostics(text, dialect);
            var redirections = tree.GetRoot().DescendantNodes().OfType<ShellRedirectionSyntax>()
                .Where(redirection => redirection.OperatorToken.Kind() == SyntaxKind.LessThanLessThanToken)
                .ToArray();

            Assert.NotEmpty(redirections);
            foreach (var redirection in redirections)
            {
                var hereDocument = redirection.HereDocument;
                Assert.NotNull(hereDocument);
                Assert.Same(redirection, hereDocument.Redirection);
                Assert.DoesNotContain("echo", hereDocument.BodyToken.Text);
                Assert.DoesNotContain("grep", hereDocument.BodyToken.Text);
            }

            Assert.DoesNotContain(tree.GetRoot().DescendantNodes().OfType<ShellCommandSyntax>(), command => command.NameValue is "body" or "a" or "b");
        }
    }

    [Fact]
    public void HereDocument_WithoutClosingDelimiter_IsAWarningLikeInTheShells()
    {
        var tree = ParseDialect("cat <<EOF\nbody\n", "bash");

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("SHELL0011", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
    }

    [Theory]
    [InlineData("echo ${x:-$(echo })}")]
    [InlineData("echo ${x:-\\}}")]
    [InlineData("echo \"${x:-\\}}\"")]
    [InlineData("echo ${x:-`echo }`}")]
    [InlineData("echo ${x:-${y:-\\}}}")]
    [InlineData("echo ${x:-\"}\"}")]
    public void ParameterExpansion_ClosingBraceInsideItsWordDoesNotEndIt(string text)
    {
        foreach (var dialect in new[] { "sh", "bash" })
        {
            var tree = ParsesWithoutDiagnostics(text, dialect);
            var reference = tree.GetRoot().DescendantNodes().OfType<ShellVariableReferenceSyntax>().First();
            Assert.EndsWith("}", reference.ToString());
            Assert.Equal(text.Length - (text.EndsWith('"') ? 1 : 0), reference.Span.End);
        }
    }

    [Theory]
    [InlineData("[[ $(echo ]]) == x ]]")]
    [InlineData("[[ `echo ]]` == x ]]")]
    [InlineData("[[ ${x:-]]} == x ]]")]
    [InlineData("(( $(echo \"))\") ))")]
    [InlineData("(( x == $(echo ')') ))")]
    [InlineData("echo $(( $(echo \"))\") ))")]
    public void DelimitedExpression_ClosingDelimiterInsideASubstitutionDoesNotEndIt(string text)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            ParsesWithoutDiagnostics(text, dialect);
        }
    }

    [Theory]
    [InlineData("echo $((echo a); echo b)")]
    [InlineData("echo $((cd /tmp) && ls)")]
    public void DollarDoubleParenthesisThatIsNotArithmetic_IsACommandSubstitution(string text)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            var tree = ParsesWithoutDiagnostics(text, dialect);
            var substitution = Assert.Single(tree.GetRoot().DescendantNodes().OfType<ShellCommandSubstitutionSyntax>());
            Assert.NotEmpty(substitution.DescendantNodes().OfType<PosixCompoundStatementSyntax>());
            Assert.Empty(tree.GetRoot().DescendantNodes().OfType<PosixArithmeticExpansionSyntax>());
        }
    }

    [Theory]
    [InlineData("for ((i=0;i<3;i++)); do echo; done")]
    [InlineData("for ((i=0;i<3;i++))\ndo echo; done")]
    [InlineData("for (( ; ; )) { break; }")]
    [InlineData("for x in a b; { echo $x; }")]
    [InlineData("for x; { echo $x; }")]
    [InlineData("select x in a; { break; }")]
    public void ForLoopHeadersAndBraceBodies_AreAccepted(string text)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            ParsesWithoutDiagnostics(text, dialect);
        }
    }

    [Theory]
    [InlineData("f() echo hi", "bash")]
    [InlineData("f() echo hi", "sh")]
    [InlineData("function f echo", "bash")]
    [InlineData("function f() echo", "bash")]
    public void FunctionBodyThatIsNotACompoundCommand_IsReportedOutsideZsh(string text, string dialect)
    {
        ReportsSingleError(text, dialect, "SHELL0014");
        ParsesWithoutDiagnostics(text, "zsh");
    }

    [Theory]
    [InlineData("f() ( echo )")]
    [InlineData("f() if true; then echo; fi")]
    [InlineData("f() [[ -n $1 ]]")]
    public void FunctionBodyThatIsACompoundCommand_IsAccepted(string text)
    {
        ParsesWithoutDiagnostics(text, "bash");
    }

    [Fact]
    public void FunctionKeywordWithoutAName_IsReportedInBash()
    {
        ReportsSingleError("function { echo; }", "bash", "SHELL0013");
        ParsesWithoutDiagnostics("function { echo; }", "zsh");
    }

    [Theory]
    [InlineData("a[1]=x", "a[1]", "x")]
    [InlineData("a[i+1]+=x", "a[i+1]", "x")]
    [InlineData("a[$k]=", "a[$k]", "")]
    [InlineData("map[\"a b\"]=1", "map[\"a b\"]", "1")]
    public void ArrayElementAssignment_IsAnAssignment(string text, string name, string value)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(ParsesWithoutDiagnostics(text, dialect).GetRoot().Statements.Statements));
            var assignment = Assert.Single(command.Assignments);
            Assert.Equal(name, assignment.NameToken.Text);
            Assert.Equal(value, assignment.Value?.Value ?? "");
            Assert.Null(command.Name);
        }
    }

    [Theory]
    [InlineData("declare a=(1 2)", "declare")]
    [InlineData("local -a arr=(1 2) b=3", "local")]
    [InlineData("export a=(1 2)", "export")]
    [InlineData("readonly a=(x)", "readonly")]
    [InlineData("typeset -A m=([k]=v)", "typeset")]
    public void DeclarationCommand_AcceptsArrayAssignments(string text, string name)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            var command = Assert.IsType<ShellCommandSyntax>(Assert.Single(ParsesWithoutDiagnostics(text, dialect).GetRoot().Statements.Statements));
            Assert.Equal(name, command.NameValue);
            Assert.Single(command.ChildNodes().OfType<PosixArrayAssignmentSyntax>());
        }
    }

    [Fact]
    public void ArrayAssignmentAsAnOrdinaryArgument_IsReportedInBash()
    {
        ReportsSingleError("echo a=(1 2)", "bash", "SHELL0002");
    }

    [Theory]
    [InlineData("for 1x in a; do :; done")]
    [InlineData("for a-b in a; do :; done")]
    public void LoopVariableThatIsNotAName_IsReported(string text)
    {
        ReportsSingleError(text, "sh", "SHELL0013");
        ReportsSingleError(text, "zsh", "SHELL0013");
    }

    [Theory]
    [InlineData("[[ ]]")]
    [InlineData("[[ ! ]]")]
    [InlineData("[[ a == ]]")]
    [InlineData("[[ a -eq ]]")]
    [InlineData("[[ a b ]]")]
    [InlineData("[[ a == b c ]]")]
    [InlineData("[[ a && ]]")]
    [InlineData("[[ ( a ]]")]
    [InlineData("[[ a || || b ]]")]
    public void InvalidConditionalExpression_IsReported(string text)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            ReportsSingleError(text, dialect, "SHELL0015");
        }
    }

    [Fact]
    public void UnaryTestWithoutOperand_IsOnlyAStringTestInZsh()
    {
        ReportsSingleError("[[ -f ]]", "bash", "SHELL0015");
        ParsesWithoutDiagnostics("[[ -f ]]", "zsh");
    }

    [Theory]
    [InlineData("[[ $x == +([0-9]) ]]")]
    [InlineData("[[ $x != @(a|b)*.txt ]]")]
    [InlineData("[[ $x == !(foo) ]]")]
    [InlineData("[[ a =~ ^(a|b)$ ]]")]
    [InlineData("[[ -n $x && ( $y == z || -f f ) ]]")]
    [InlineData("[[ a &&\nb ]]")]
    [InlineData("[[ a < b && c > d ]]")]
    [InlineData("[[ -v arr[1] ]]")]
    [InlineData("[[ $a -nt $b ]]")]
    public void ValidConditionalExpression_IsModeled(string text)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            var tree = ParsesWithoutDiagnostics(text, dialect);
            var statement = Assert.IsType<PosixDelimitedExpressionStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));
            Assert.IsNotType<ShellRawExpressionSyntax>(statement.Expression);
        }
    }

    [Theory]
    [InlineData("echo @(a|b)")]
    [InlineData("ls !(*.txt)")]
    [InlineData("rm -f +([0-9]).log")]
    [InlineData("case $x in @(a|b)) echo;; esac")]
    public void ExtendedGlob_IsOneWord(string text)
    {
        var tree = ParsesWithoutDiagnostics(text, "bash");
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<PosixCompoundStatementSyntax>());
    }

    [Theory]
    [InlineData("echo a |& cat", "sh")]
    [InlineData("case x in a) ;;& esac", "zsh")]
    [InlineData("case x in a) ;| esac", "bash")]
    public void OperatorsOfAnotherDialect_AreReported(string text, string dialect)
    {
        Assert.NotEmpty(ParseDialect(text, dialect).GetDiagnostics());
    }

    [Theory]
    [InlineData("echo a |& cat", "bash")]
    [InlineData("echo a |& cat", "zsh")]
    [InlineData("case x in a) ;;& esac", "bash")]
    [InlineData("case x in a) ;| esac", "zsh")]
    [InlineData("case x in a) ;& esac", "bash")]
    public void OperatorsOfTheDialect_AreAccepted(string text, string dialect)
    {
        ParsesWithoutDiagnostics(text, dialect);
    }

    [Fact]
    public void AmpersandGreaterThan_IsABackgroundSeparatorInSh()
    {
        var tree = ParsesWithoutDiagnostics("echo a &>f", "sh");
        var statements = tree.GetRoot().Statements;

        Assert.Equal(2, statements.Statements.Count);
        Assert.Equal(SyntaxKind.AmpersandToken, statements.Statements.GetSeparator(0).Kind());
        Assert.Single(Assert.IsType<ShellCommandSyntax>(statements.Statements[1]).Redirections);
    }

    // ---- found by running the scripts installed on a machine through both the parser and the shells ----

    [Theory]
    [InlineData("REGEX=\"^[0-9.]+$\"\necho \"$\" '$'\nif true; then echo; fi\n")]
    [InlineData("echo \"a$'b'\"\n")]
    public void DollarBeforeAQuoteInsideDoubleQuotes_IsLiteral(string text)
    {
        foreach (var dialect in new[] { "bash", "zsh" })
        {
            var tree = ParsesWithoutDiagnostics(text, dialect);
            Assert.DoesNotContain(tree.GetRoot().DescendantTokens(), token => token.Kind() is SyntaxKind.DollarDoubleQuoteToken or SyntaxKind.DollarSingleQuoteToken);
        }
    }

    [Theory]
    [InlineData("read -r a < <(stty size)")]
    [InlineData("while read -r f; do echo \"$f\"; done < <(find . -type f)")]
    [InlineData("x=$(sort < <(ls))")]
    public void ProcessSubstitutionAsARedirectionTarget_IsAccepted(string text)
    {
        var tree = ParsesWithoutDiagnostics(text, "bash");
        var redirection = Assert.Single(tree.GetRoot().DescendantNodes().OfType<ShellRedirectionSyntax>());
        Assert.IsType<PosixProcessSubstitutionSyntax>(Assert.Single(redirection.Target!.Parts));
    }

    [Fact]
    public void RegexGroupMayHoldUnquotedBlanks()
    {
        var tree = ParsesWithoutDiagnostics("[[ \"$*\" =~ (^| )-?-show-sdk-(path|version) && -n \"$x\" ]]", "bash");
        var statement = Assert.IsType<PosixDelimitedExpressionStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));
        var and = Assert.IsType<ShellBinaryExpressionSyntax>(statement.Expression);
        Assert.Equal("(^| )-?-show-sdk-(path|version)", Assert.IsType<ShellBinaryExpressionSyntax>(and.Left).Right.ToString());
    }

    [Theory]
    [InlineData("sh")]
    [InlineData("bash")]
    [InlineData("zsh")]
    public void GroupClosedRightBeforeTheBacktickOfItsSubstitution_IsClosed(string dialect)
    {
        ParsesWithoutDiagnostics("for f in `test -d d && { find d -type f | sort; }`\ndo\n  echo $f\ndone\n", dialect);
    }

    [Theory]
    [InlineData("{ echo '*'{-v,--verbose}'[x]' }", 1)]
    [InlineData("{ echo a}b }", 1)]
    [InlineData("{ echo a}}", 1)]
    [InlineData("{ echo a}\necho b", 2)]
    public void ZshClosingBraceInsideAWord_OnlyClosesTheGroupBeforeADelimiter(string text, int statementCount)
    {
        var tree = ParsesWithoutDiagnostics(text, "zsh");
        Assert.Equal(statementCount, tree.GetRoot().Statements.Statements.Count);
    }

    [Fact]
    public void ZshClosingBraceInsideAWord_ClosesTheGroupBeforeABlank()
    {
        var diagnostic = ReportsSingleError("{ echo a} }", "zsh", "SHELL0002");
        Assert.Equal("Unexpected '}'.", diagnostic.Message);
    }

    // ---- error recovery ----

    [Fact]
    public void Recovery_MissingDoneIsReportedAtTheEnclosingKeyword()
    {
        const string Text = "if a; then\n  while b; do\n    c\nfi\necho after\n";
        var tree = ParseDialect(Text, "bash");

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("SHELL0012", diagnostic.Id);
        Assert.Equal("Expected 'done'.", diagnostic.Message);
        Assert.Equal(Text.IndexOf("fi", StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);

        var statements = tree.GetRoot().Statements.Statements;
        Assert.HasCount(2, statements);
        var ifStatement = Assert.IsType<PosixIfStatementSyntax>(statements[0]);
        Assert.False(ifStatement.FiKeyword.IsMissing);
        Assert.True(Assert.IsType<PosixWhileStatementSyntax>(ifStatement.Body.Statements[0]).DoneKeyword.IsMissing);
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(statements[1]).NameValue);
    }

    [Fact]
    public void Recovery_MissingThenStillParsesTheBody()
    {
        const string Text = "if a; echo b; fi\necho after\n";
        var tree = ParseDialect(Text, "bash");

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("Expected 'then'.", diagnostic.Message);
        Assert.Equal(2, tree.GetRoot().Statements.Statements.Count);
    }

    [Fact]
    public void Recovery_ConditionClosedByFiReportsOnlyTheMissingThen()
    {
        const string Text = "if a; fi\necho after\n";
        var tree = ParseDialect(Text, "bash");

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("Expected 'then'.", diagnostic.Message);
        var ifStatement = Assert.IsType<PosixIfStatementSyntax>(tree.GetRoot().Statements.Statements[0]);
        Assert.False(ifStatement.FiKeyword.IsMissing);
    }

    [Fact]
    public void Recovery_ExtraFiIsSkippedAndParsingContinues()
    {
        const string Text = "if a; then b; fi\nfi\necho after\n";
        var tree = ParseDialect(Text, "bash");

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("Unexpected 'fi'.", diagnostic.Message);
        Assert.Equal(Text.LastIndexOf("fi", StringComparison.Ordinal), diagnostic.Location.SourceSpan.Start);
        var statements = tree.GetRoot().Statements.Statements;
        Assert.IsType<PosixIfStatementSyntax>(statements[0]);
        Assert.IsType<ShellSkippedTextSyntax>(statements[1]);
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(statements[2]).NameValue);
    }

    [Fact]
    public void Recovery_UnclosedSubstitutionInsideAGroupDoesNotSwallowTheClosingBrace()
    {
        const string Text = "{ echo $(date; }\necho after\n";
        var tree = ParseDialect(Text, "bash");

        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "SHELL0006");
        Assert.Equal(Text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void Recovery_MissingEsacIsReportedAtEndOfInput()
    {
        const string Text = "case $x in\n  a) echo a ;;\n  b) echo b ;;\n";
        var tree = ParseDialect(Text, "bash");

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("Expected 'esac'.", diagnostic.Message);
        Assert.Equal(2, Assert.IsType<PosixCaseStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements)).Clauses.Count);
    }

    [Fact]
    public void Recovery_MissingCaseTerminatorBeforeTheNextPattern()
    {
        const string Text = "case x in\n  a) echo a\n  b) echo b ;;\nesac\necho after\n";
        var tree = ParseDialect(Text, "bash");

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("SHELL0002", diagnostic.Id);
        Assert.Equal(Text.IndexOf("b)", StringComparison.Ordinal) + 1, diagnostic.Location.SourceSpan.Start);
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[^1]).NameValue);
    }

    [Fact]
    public void Recovery_MissingClosingBraceOfAFunctionIsReportedOnce()
    {
        const string Text = "f() {\n  echo a\n";
        var tree = ParseDialect(Text, "bash");

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("SHELL0009", diagnostic.Id);
        Assert.Equal(4, diagnostic.Location.SourceSpan.Start);
    }

    [Theory]
    [InlineData("echo a |")]
    [InlineData("echo a &&")]
    [InlineData("| echo a")]
    [InlineData("echo a && || echo b")]
    [InlineData("echo a | | echo b")]
    public void Recovery_MissingPipelineElementIsReportedOnce(string text)
    {
        foreach (var dialect in new[] { "sh", "bash" })
        {
            ReportsSingleError(text, dialect, "SHELL0001");
        }
    }
}
