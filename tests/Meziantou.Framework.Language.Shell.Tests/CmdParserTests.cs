namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class CmdParserTests
{
    public static TheoryData<string> Samples =>
    [
        "",
        "\r\n",
        "echo Hello",
        "@echo off\r\n",
        "dir /b",
        "dir | findstr foo",
        "build && test",
        "build || goto :error",
        "cd tmp & dir",
        "echo %PATH%",
        "echo %USERPROFILE%\\bin",
        "echo %1 %2 %*",
        "echo %~dp0",
        "echo %~nx1",
        "set NAME=value",
        "set \"NAME=value with spaces\"",
        "set /a total=1+2",
        "set /p answer=Continue? ",
        "set NAME=",
        "setlocal enabledelayedexpansion\r\necho !NAME!\r\n",
        "if exist file.txt echo found",
        "if not exist file.txt echo missing",
        "if errorlevel 1 goto :error",
        "if defined NAME echo set",
        "if \"%A%\"==\"b\" echo equal",
        "if /i \"%A%\"==\"B\" echo equal",
        "if %n% GEQ 5 echo big",
        "if exist a (echo yes) else (echo no)",
        "if exist a (\r\n  echo yes\r\n) else (\r\n  echo no\r\n)\r\n",
        "for %%i in (*.txt) do echo %%i",
        "for /d %%d in (*) do echo %%d",
        "for /r %%f in (*.cs) do echo %%f",
        "for /l %%n in (1,1,10) do echo %%n",
        "for /f \"tokens=1,2\" %%a in (data.txt) do echo %%a %%b",
        ":start\r\necho looping\r\ngoto start\r\n",
        ":eof",
        "goto :eof",
        "call :subroutine arg",
        "call other.bat",
        "rem this is a comment\r\n",
        "REM upper case comment\r\n",
        ":: double colon comment\r\n",
        "echo hi rem not a comment",
        "echo out > result.txt",
        "echo out >> result.txt",
        "command 2>&1",
        "type < input.txt",
        "echo caret ^& literal",
        "echo 100%% done",
        "(echo a\r\necho b)",
        "if exist a echo yes",
        "for",
        "if",
        "set",
        "goto",
        "call",
        "(",
        ")",
        "&",
        "%",
        "!",
        "echo \"unterminated",
        "for %%i in (unterminated",
        // Shapes taken from batch files shipped with Windows and with the tools installed alongside it.
        "@REM comment with <angles> and | pipes\r\n",
        "@ rem spaced comment\r\n",
        "if 1==1 (set N=5)\r\n",
        "set N=5)\r\n",
        "if exist \"C:\\Program Files\\app.exe\" echo found\r\n",
        "if not exist \"C:\\Program Files\\app.exe\" echo missing\r\n",
        "for /f \"tokens=*\" %%i in ('reg query x ^| findstr y') do ( set /a n+=1 )\r\n",
        "if exist x (\r\n\t(set RET=1)\r\n) else (set RET=2)\r\n",
        "(call :VARDEL X)\r\n",
        "echo a(b c)d\r\n",
        "echo (\r\n",
        "echo )\r\n",
    ];

    [Theory]
    [MemberData(nameof(Samples))]
    public void ParseText_RoundTripsExactly(string text)
    {
        Assert.Equal(text, ShellSyntaxTree.ParseText(text, ShellDialect.Cmd).GetRoot().ToFullString());
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ParseText_NeverThrows(string text)
    {
        Assert.Null(Record.Exception(() => ShellSyntaxTree.ParseText(text, ShellDialect.Cmd)));
    }

    [Fact]
    public void Command_ExposesNameAndArguments()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("dir /b /s", ShellDialect.Cmd));

        Assert.Equal("dir", command.NameValue);
        Assert.Equal(["/b", "/s"], command.Arguments.Select(argument => argument.Value));
    }

    [Fact]
    public void Label_ExposesItsName()
    {
        var label = Assert.IsType<CmdLabelStatementSyntax>(ShellSyntaxTree.ParseCommand(":start", ShellDialect.Cmd));

        Assert.Equal("start", label.Name);
    }

    [Fact]
    public void Goto_ExposesItsTarget()
    {
        Assert.Equal("error", Assert.IsType<CmdGotoStatementSyntax>(ShellSyntaxTree.ParseCommand("goto error", ShellDialect.Cmd)).Label);
        Assert.Equal("eof", Assert.IsType<CmdGotoStatementSyntax>(ShellSyntaxTree.ParseCommand("goto :eof", ShellDialect.Cmd)).Label);
    }

    [Fact]
    public void Call_WrapsTheInvokedCommand()
    {
        var call = Assert.IsType<CmdCallStatementSyntax>(ShellSyntaxTree.ParseCommand("call :sub arg", ShellDialect.Cmd));

        // Calling a label is an ordinary command named after the label, not a label statement: its arguments,
        // redirections, and the operators after it are parsed like any other command's.
        var target = Assert.IsType<ShellCommandSyntax>(call.Target);
        Assert.Equal(":sub", target.NameValue);
        Assert.Equal(["arg"], target.Arguments.Select(argument => argument.Value));
    }

    [Fact]
    public void Set_ExposesNameValueAndSwitches()
    {
        var plain = Assert.IsType<CmdSetStatementSyntax>(ShellSyntaxTree.ParseCommand("set NAME=value", ShellDialect.Cmd));
        Assert.Equal("NAME", plain.Name);
        Assert.Equal("value", plain.Value?.Value);
        Assert.False(plain.IsArithmetic);

        var arithmetic = Assert.IsType<CmdSetStatementSyntax>(ShellSyntaxTree.ParseCommand("set /a total=1+2", ShellDialect.Cmd));
        Assert.True(arithmetic.IsArithmetic);

        var prompt = Assert.IsType<CmdSetStatementSyntax>(ShellSyntaxTree.ParseCommand("set /p answer=Go?", ShellDialect.Cmd));
        Assert.True(prompt.IsPrompt);

        var cleared = Assert.IsType<CmdSetStatementSyntax>(ShellSyntaxTree.ParseCommand("set NAME=", ShellDialect.Cmd));
        Assert.Null(cleared.Value);
    }

    [Theory]
    [InlineData("if exist a echo yes", false, false)]
    [InlineData("if not exist a echo yes", false, true)]
    [InlineData("if /i \"%a%\"==\"b\" echo yes", true, false)]
    [InlineData("if /i not \"%a%\"==\"b\" echo yes", true, true)]
    public void If_ExposesSwitchesAndCondition(string text, bool caseInsensitive, bool negated)
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.Cmd));

        Assert.Equal(caseInsensitive, statement.IsCaseInsensitive);
        Assert.Equal(negated, statement.IsNegated);
        Assert.NotEmpty(statement.Condition.ToFullString());
    }

    [Fact]
    public void If_ExposesTheElseClause()
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(ShellSyntaxTree.ParseCommand("if exist a (echo yes) else (echo no)", ShellDialect.Cmd));

        Assert.IsType<CmdParenthesizedBlockSyntax>(statement.Body);
        Assert.NotNull(statement.ElseClause);
        Assert.IsType<CmdParenthesizedBlockSyntax>(statement.ElseClause.Body);
    }

    [Theory]
    [InlineData("if exist file.txt echo yes", "exist", "file.txt")]
    [InlineData("if not exist \"C:\\Program Files\\app.exe\" echo no", "exist", "\"C:\\Program Files\\app.exe\"")]
    [InlineData("if defined FOO echo set", "defined", "FOO")]
    [InlineData("if errorlevel 1 echo failed", "errorlevel", "1")]
    [InlineData("if cmdextversion 2 echo ok", "cmdextversion", "2")]
    public void IfCondition_UnaryForm_ExposesTheOperatorAndOperand(string text, string expectedOperator, string expectedOperand)
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.Cmd));
        var condition = Assert.IsType<ShellUnaryExpressionSyntax>(statement.Condition);

        Assert.Equal(expectedOperator, condition.OperatorText);
        Assert.Equal(expectedOperand, Assert.IsType<ShellOperandExpressionSyntax>(condition.Operand).Word.ToFullString().Trim());
    }

    [Theory]
    [InlineData("if a==b echo eq", "a", "==", "b")]
    [InlineData("if \"%a%\"==\"b\" echo eq", "\"%a%\"", "==", "\"b\"")]
    [InlineData("if /i \"%OS%\"==\"Windows_NT\" echo nt", "\"%OS%\"", "==", "\"Windows_NT\"")]
    [InlineData("if %n% GEQ 5 echo big", "%n%", "GEQ", "5")]
    [InlineData("if %n% equ 5 echo five", "%n%", "equ", "5")]
    [InlineData("if !x! neq !y! echo differ", "!x!", "neq", "!y!")]
    public void IfCondition_ComparisonForm_ExposesBothOperands(string text, string expectedLeft, string expectedOperator, string expectedRight)
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.Cmd));
        var condition = Assert.IsType<ShellBinaryExpressionSyntax>(statement.Condition);

        Assert.Equal(expectedLeft, Assert.IsType<ShellOperandExpressionSyntax>(condition.Left).Word.ToFullString().Trim());
        Assert.Equal(expectedOperator, condition.OperatorText);
        Assert.Equal(expectedRight, Assert.IsType<ShellOperandExpressionSyntax>(condition.Right).Word.ToFullString().Trim());
    }

    [Fact]
    public void IfCondition_OperandExposesVariableReferences()
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(ShellSyntaxTree.ParseCommand("if %n% equ 5 echo five", ShellDialect.Cmd));
        var condition = Assert.IsType<ShellBinaryExpressionSyntax>(statement.Condition);
        var word = Assert.IsType<ShellOperandExpressionSyntax>(condition.Left).Word;

        var reference = Assert.IsType<CmdVariableReferenceSyntax>(Assert.Single(word.Parts));
        Assert.Equal("n", reference.Name);
    }

    [Theory]
    // Text matching none of the four forms stays a lone operand rather than becoming an error.
    [InlineData("if %x% echo hi")]
    [InlineData("if exist")]
    public void IfCondition_UnrecognizedForm_IsALoneOperand(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);
        var statement = Assert.IsType<CmdIfStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));

        Assert.IsNotType<ShellRawExpressionSyntax>(statement.Condition);
    }

    [Fact]
    public void For_ExposesVariableItemsAndSwitch()
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(ShellSyntaxTree.ParseCommand("for %%i in (a b c) do echo %%i", ShellDialect.Cmd));

        Assert.Equal("i", statement.VariableName);
        Assert.False(statement.SwitchToken.IsPresent());
        Assert.Equal(3, statement.Items.Count);
        Assert.IsType<ShellCommandSyntax>(statement.Body);
    }

    [Fact]
    public void For_SupportsSwitchesAndOptionStrings()
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(
            ShellSyntaxTree.ParseCommand("for /f \"tokens=1,2\" %%a in (data.txt) do echo %%a", ShellDialect.Cmd));

        Assert.Equal("/f", statement.SwitchToken.Text);
        Assert.Single(statement.SwitchArguments);
        Assert.Equal("a", statement.VariableName);
    }

    [Fact]
    public void VariableReferences_AreClassified()
    {
        var tree = ShellSyntaxTree.ParseText("echo %PATH% %1 %~dp0", ShellDialect.Cmd);
        var references = tree.GetRoot().DescendantNodes().OfType<CmdVariableReferenceSyntax>().ToArray();

        Assert.HasCount(3, references);
        Assert.Equal("PATH", references[0].Name);
        Assert.False(references[0].IsDelayed);
        Assert.False(references[0].IsLoopVariable);
        Assert.Equal("1", references[1].Name);
        Assert.False(references[1].CloseToken.IsPresent());
    }

    [Fact]
    public void DelayedExpansion_IsRecognized()
    {
        var tree = ShellSyntaxTree.ParseText("echo !COUNT!", ShellDialect.Cmd);
        var reference = Assert.Single(tree.GetRoot().DescendantNodes().OfType<CmdVariableReferenceSyntax>());

        Assert.True(reference.IsDelayed);
        Assert.Equal("COUNT", reference.Name);
    }

    [Fact]
    public void LoopVariable_IsRecognized()
    {
        var tree = ShellSyntaxTree.ParseText("for %%i in (a) do echo %%i", ShellDialect.Cmd);
        var reference = Assert.Single(tree.GetRoot().DescendantNodes().OfType<CmdVariableReferenceSyntax>());

        Assert.True(reference.IsLoopVariable);
        Assert.Equal("i", reference.Name);
    }

    [Fact]
    public void CommentsAreTrivia()
    {
        var tree = ShellSyntaxTree.ParseText("rem first\r\n:: second\r\necho hi\r\n", ShellDialect.Cmd);
        var comments = tree.GetRoot().DescendantTrivia()
            .Where(trivia => trivia.Kind() is SyntaxKind.CmdRemCommentTrivia or SyntaxKind.CmdDoubleColonCommentTrivia)
            .ToArray();

        Assert.HasCount(2, comments);
        Assert.Equal("rem first", comments[0].ToString());
        Assert.Equal(":: second", comments[1].ToString());
    }

    [Fact]
    public void RemIsOnlyACommentAtStatementStart()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo hi rem not a comment", ShellDialect.Cmd));

        Assert.DoesNotContain(command.DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.CmdRemCommentTrivia);
        Assert.Contains(command.Arguments, argument => argument.Value == "rem");
    }

    [Fact]
    public void CaretEscape_IsAWordPart()
    {
        var tree = ShellSyntaxTree.ParseText("echo a^&b", ShellDialect.Cmd);
        var escape = Assert.Single(tree.GetRoot().DescendantNodes().OfType<ShellEscapeSequenceSyntax>());

        Assert.Equal("^&", escape.EscapeToken.Text);
        Assert.Equal("&", escape.Value);
    }

    [Fact]
    public void ParenthesizedBlock_HoldsItsStatements()
    {
        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(ShellSyntaxTree.ParseCommand("(echo a\r\necho b)", ShellDialect.Cmd));

        Assert.Equal(2, block.Statements.Statements.Count);
    }

    [Fact]
    public void Redirections_AreAttachedToTheCommand()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo hi > out.txt", ShellDialect.Cmd));

        var redirection = Assert.Single(command.Redirections);
        Assert.Equal("out.txt", redirection.Target?.Value);
    }

    // Every input below was run through cmd.exe on Windows and behaved as described, so a diagnostic here would be a
    // false positive on a batch file that runs.

    [Theory]
    [InlineData("@REM comment with <angles> and | pipes\r\necho after\r\n")]
    [InlineData("@ rem spaced comment <angles>\r\necho after\r\n")]
    [InlineData("if exist \"C:\\Program Files\\app.exe\" echo found\r\n")]
    [InlineData("if not exist \"C:\\Program Files\\app.exe\" echo missing\r\n")]
    [InlineData("if defined \"a b\" echo yes\r\n")]
    [InlineData("for /f \"tokens=*\" %%i in ('reg query x ^| findstr y') do ( set /a n+=1 )\r\n")]
    [InlineData("for %%i in (1 2) do ( set /a n+=1 )\r\n")]
    [InlineData("if exist x (\r\n\t(set RET=1)\r\n) else (set RET=2)\r\n")]
    [InlineData("(call :VARDEL X)\r\n")]
    [InlineData("(goto :eof)\r\n")]
    [InlineData("echo a(b c)d\r\n")]
    [InlineData("echo (\r\n")]
    [InlineData("echo )\r\n")]
    [InlineData("set N=5)\r\n")]
    public void ConstructsAcceptedByCmd_ParseWithoutDiagnostics(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Cmd);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Theory]
    [InlineData("@rem comment\r\n")]
    [InlineData("@REM comment\r\n")]
    [InlineData("@ rem comment\r\n")]
    public void AtRemIsStillAComment(string text)
    {
        // `@` only suppresses echoing, so it does not stop `rem` from starting a comment.
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Cmd);

        Assert.Equal(text, Assert.Single(tree.GetRoot().DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.CmdRemCommentTrivia).ToString() + "\r\n");
        Assert.Empty(tree.GetRoot().Statements.Statements);
    }

    [Fact]
    public void AtBeforeAnOrdinaryCommandIsNotAComment()
    {
        var tree = ShellSyntaxTree.ParseText("@echo off\r\n", ShellDialect.Cmd);

        Assert.DoesNotContain(tree.GetRoot().DescendantTrivia(), trivia => trivia.Kind() == SyntaxKind.CmdRemCommentTrivia);
        Assert.Single(tree.GetRoot().Statements.Statements);
    }

    [Fact]
    public void RemainderOfAWordIsNotACommentEvenAfterAt()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("@remove file.txt", ShellDialect.Cmd));

        Assert.Equal("@remove", command.NameValue);
    }

    [Fact]
    public void OpenParenIsOnlyABlockAtTheStartOfACommand()
    {
        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(ShellSyntaxTree.ParseCommand("(echo a)", ShellDialect.Cmd));
        Assert.Single(block.Statements.Statements);

        // Elsewhere it is an ordinary character, which is why `echo a(b c)d` prints `a(b c)d`.
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo a(b c)d", ShellDialect.Cmd));
        Assert.Equal(["a(b", "c)d"], command.Arguments.Select(argument => argument.Value));
    }

    [Fact]
    public void CloseParenEndsAWordOnlyInsideABlock()
    {
        var outside = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo a)b", ShellDialect.Cmd));
        Assert.Equal("a)b", Assert.Single(outside.Arguments).Value);

        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(ShellSyntaxTree.ParseCommand("(echo a)b", ShellDialect.Cmd));
        var inside = Assert.IsType<ShellCommandSyntax>(Assert.Single(block.Statements.Statements));
        Assert.Equal("a", Assert.Single(inside.Arguments).Value);
    }

    [Fact]
    public void SetInsideABlockStopsAtTheClosingParenthesis()
    {
        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(ShellSyntaxTree.ParseCommand("(set RET=5)", ShellDialect.Cmd));
        var set = Assert.IsType<CmdSetStatementSyntax>(Assert.Single(block.Statements.Statements));

        Assert.Equal("5", set.Value?.Value);
        Assert.Equal(")", block.CloseParenToken.Text);
    }

    [Fact]
    public void SetOutsideABlockKeepsTheParenthesisInItsValue()
    {
        // At the top level cmd assigns `5)`, parenthesis included.
        var set = Assert.IsType<CmdSetStatementSyntax>(ShellSyntaxTree.ParseCommand("set RET=5)", ShellDialect.Cmd));

        Assert.Equal("5)", set.Value?.Value);
    }

    [Fact]
    public void SetInsideAForBodyStopsAtTheClosingParenthesis()
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(
            ShellSyntaxTree.ParseCommand("for %%i in (1 2) do ( set /a n+=1 )", ShellDialect.Cmd));

        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(statement.Body);
        Assert.IsType<CmdSetStatementSyntax>(Assert.Single(block.Statements.Statements));
        Assert.Equal(")", block.CloseParenToken.Text);
    }

    [Fact]
    public void CallToALabelInsideABlockStopsAtTheClosingParenthesis()
    {
        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(ShellSyntaxTree.ParseCommand("(call :VARDEL X)", ShellDialect.Cmd));
        var call = Assert.IsType<CmdCallStatementSyntax>(Assert.Single(block.Statements.Statements));

        var target = Assert.IsType<ShellCommandSyntax>(call.Target);
        Assert.Equal(":VARDEL", target.NameValue);
        Assert.Equal(["X"], target.Arguments.Select(argument => argument.Value));
        Assert.Equal(")", block.CloseParenToken.Text);
    }

    [Fact]
    public void ForItemsStopAtTheClosingParenthesisEvenAtTheTopLevel()
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(
            ShellSyntaxTree.ParseCommand("for %%i in (a b) do echo %%i", ShellDialect.Cmd));

        Assert.Equal(["a", "b"], statement.Items.Select(item => item.Value));
        Assert.Equal(")", statement.CloseParenToken.Text);
    }

    [Theory]
    [InlineData("if exist \"a b\" echo yes")]
    [InlineData("if not exist \"a b\" echo yes")]
    [InlineData("if defined \"a b\" echo yes")]
    public void IfOperandMayBeQuotedAndContainSpaces(string text)
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.Cmd));

        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(statement.Body).NameValue);
    }

    // The checks below rebuild the text from the children rather than reading it off the root, which is the only way
    // to notice a node that dropped a character or points at the wrong span. See ShellSyntaxAssert.

    [Theory]
    // The shape of an npm shim: many line-separated statements and a single `&` near the end.
    [InlineData("@ECHO off\r\nGOTO start\r\n:find_dp0\r\nSET dp0=%~dp0\r\nendLocal & goto #_undefined_#\r\n")]
    [InlineData("echo a\r\necho b & echo c\r\n")]
    [InlineData("echo a\r\necho b\r\necho c & echo d\r\n")]
    [InlineData("echo a & echo b\r\necho c\r\n")]
    public void AmpersandIsRebuiltAgainstTheStatementItFollows(string text)
    {
        ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);
    }

    [Theory]
    // A `for` whose item list runs into an operator is malformed, but the operator still belongs to the tree.
    [InlineData("for>>")]
    [InlineData("for %%i in (a|b) do echo x")]
    [InlineData("for %%i in (a>b) do echo x")]
    [InlineData("for %%i in (a&b) do echo x")]
    [InlineData("for %%i in (unterminated")]
    // Tokens whose text is measured by a scan have to report the position they start at, not the one they end at.
    [InlineData("goto.~\r\n")]
    [InlineData("goto")]
    [InlineData("set$else/p [:label ")]
    [InlineData("set")]
    [InlineData("for %%")]
    public void MalformedInputKeepsEveryCharacterInTheTree(string text)
    {
        ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);
    }

    [Fact]
    public void GotoTargetSpanStartsAtTheTarget()
    {
        var tree = ShellSyntaxTree.ParseText("goto :eof", ShellDialect.Cmd);
        var statement = Assert.IsType<CmdGotoStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));

        Assert.Equal(":eof", tree.GetText().Text[statement.LabelToken.Span.Start..statement.LabelToken.Span.End]);
    }

    [Fact]
    public void SetNameSpanStartsAtTheName()
    {
        var tree = ShellSyntaxTree.ParseText("set NAME=value", ShellDialect.Cmd);
        var statement = Assert.IsType<CmdSetStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));
        var name = statement.NameToken!;

        Assert.Equal("NAME", tree.GetText().Text[name.Span.Start..name.Span.End]);
    }

    [Fact]
    public void ForVariableSpanStartsAtTheVariable()
    {
        var tree = ShellSyntaxTree.ParseText("for %%i in (a) do echo %%i", ShellDialect.Cmd);
        var statement = Assert.IsType<CmdForStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));

        Assert.Equal("%%i", tree.GetText().Text[statement.VariableToken.Span.Start..statement.VariableToken.Span.End]);
    }

    // The tests below pin down how cmd.exe itself splits a line. The grammar they follow is the one cmd's parser
    // implements: a line is `s0 -> s1 [& s0]`, `s1 -> s2 [|| s1]`, `s2 -> s3 [&& s2]`, `s3 -> s4 [| s3]`, and the
    // command of an `if`, `else`, or `for ... do` is a whole `s0`, so it runs to the end of the logical line.

    private static ShellStatementSyntax SingleStatement(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        return Assert.Single(tree.GetRoot().Statements.Statements);
    }

    private static string[] DiagnosticIds(string text) => [.. ShellSyntaxTree.ParseText(text, ShellDialect.Cmd).GetDiagnostics().Select(diagnostic => diagnostic.Id)];

    [Theory]
    // `if exist f del f & echo deleted` only echoes when the file existed: the `&` is part of the `if` command.
    [InlineData("if 1==0 echo a & echo b", 2)]
    [InlineData("if 1==0 echo a && echo b & echo c", 2)]
    [InlineData("if exist a (echo y) & echo z", 2)]
    public void IfBodyRunsToTheEndOfTheLine(string text, int expectedCommands)
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(SingleStatement(text));
        var body = Assert.IsType<ShellCommandListSyntax>(statement.Body);

        Assert.Equal(expectedCommands, body.Pipelines.Count);
        Assert.Equal("&", Assert.Single(body.OperatorTokens).Text);
        Assert.Empty(DiagnosticIds(text));
    }

    [Fact]
    public void ForBodyRunsToTheEndOfTheLine()
    {
        // `for %%i in (1 2) do echo %%i & echo x` echoes `x` once per iteration.
        var statement = Assert.IsType<CmdForStatementSyntax>(SingleStatement("for %%i in (1 2) do echo %%i & echo x"));

        Assert.HasCount(2, Assert.IsType<ShellCommandListSyntax>(statement.Body).Pipelines);
    }

    [Fact]
    public void ElseBodyRunsToTheEndOfTheLine()
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(SingleStatement("if exist a (echo y) else echo n & echo m"));

        Assert.NotNull(statement.ElseClause);
        Assert.HasCount(2, Assert.IsType<ShellCommandListSyntax>(statement.ElseClause.Body).Pipelines);
    }

    [Fact]
    public void BodyInsideABlockStopsAtTheClosingParenthesis()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("(if 1==1 echo a & echo b) & echo c", ShellDialect.Cmd);
        Assert.HasCount(2, tree.GetRoot().Statements.Statements);
        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(tree.GetRoot().Statements.Statements[0]);
        var statement = Assert.IsType<CmdIfStatementSyntax>(Assert.Single(block.Statements.Statements));

        Assert.HasCount(2, Assert.IsType<ShellCommandListSyntax>(statement.Body).Pipelines);
    }

    [Fact]
    public void TrailingAmpersandAfterABodySeparatesTheStatement()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("if 1==1 echo a &\r\necho b\r\n", ShellDialect.Cmd);

        Assert.HasCount(2, tree.GetRoot().Statements.Statements);
        Assert.IsType<ShellCommandSyntax>(Assert.IsType<CmdIfStatementSyntax>(tree.GetRoot().Statements.Statements[0]).Body);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Theory]
    // cmd reads a script a line at a time, so an `else` on the next line is a command of its own.
    [InlineData("if exist a (echo y)\r\nelse (echo n)\r\n")]
    [InlineData("if exist a (\r\necho y\r\n)\r\nelse (\r\necho n\r\n)\r\n")]
    public void ElseOnTheFollowingLineIsNotAnElseClause(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);
        var statement = Assert.IsType<CmdIfStatementSyntax>(tree.GetRoot().Statements.Statements[0]);

        Assert.Null(statement.ElseClause);
        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "SHELL0002");
    }

    [Theory]
    // A command body takes every word to the end of the line, `else` included, so only a `)` can precede an `else`.
    [InlineData("if exist a goto x else goto y")]
    [InlineData("if exist a echo x else echo y")]
    public void ElseAfterACommandBodyIsNotAnElseClause(string text)
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(ShellSyntaxTree.ParseText(text, ShellDialect.Cmd).GetRoot().Statements.Statements[0]);

        Assert.Null(statement.ElseClause);
    }

    [Theory]
    [InlineData("else echo x")]
    [InlineData("echo a\r\nelse echo x")]
    [InlineData("(echo a\r\nelse (echo b))")]
    public void ElseWithoutAnIf_IsReported(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "SHELL0002");
    }

    [Theory]
    // Outside a block a line break ends the line, so the command cannot start on the next one.
    [InlineData("if exist a\r\necho hi\r\n")]
    [InlineData("if 1==1\r\necho hi\r\n")]
    [InlineData("for %%i in (a) do\r\necho hi\r\n")]
    [InlineData("if exist a (echo y) else\r\necho hi\r\n")]
    [InlineData("echo a |\r\necho hi\r\n")]
    [InlineData("echo a &&\r\necho hi\r\n")]
    [InlineData("echo a ||\r\necho hi\r\n")]
    [InlineData("call\r\necho hi\r\n")]
    public void MissingCommandAtTheEndOfTheLine_IsReportedAndTheNextLineStandsAlone(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Contains(tree.GetDiagnostics(), diagnostic => diagnostic.Id == "SHELL0001");
        Assert.HasCount(2, tree.GetRoot().Statements.Statements);
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[1]).NameValue);
    }

    [Theory]
    [InlineData("call :sub arg & echo done", 2)]
    [InlineData("call :sub & echo done", 2)]
    [InlineData("(call :sub arg) & echo done", 2)]
    public void CallToALabelEndsAtAnOperator(string text, int expectedStatements)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Equal(expectedStatements, tree.GetRoot().Statements.Statements.Count);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void CallToALabelTakesArgumentsAndRedirections()
    {
        var list = Assert.IsType<ShellCommandListSyntax>(SingleStatement("call :sub \"a b\" %1 >> log.txt 2>&1 || goto :error"));
        var call = Assert.IsType<CmdCallStatementSyntax>(list.Pipelines[0]);
        var target = Assert.IsType<ShellCommandSyntax>(call.Target);

        Assert.Equal(":sub", target.NameValue);
        Assert.Equal("a b", target.Arguments[0].Value);
        Assert.HasCount(2, target.Redirections);
        Assert.IsType<CmdGotoStatementSyntax>(list.Pipelines[1]);
    }

    [Theory]
    [InlineData("set /p VERSION=<version.txt", "VERSION", null, "<", "version.txt")]
    [InlineData("set x=hello>out.txt", "x", "hello", ">", "out.txt")]
    [InlineData("set /a x=1 2>nul", "x", "1 ", ">", "nul")]
    [InlineData("set \"x=a b\" >nul", "x", "a b", ">", "nul")]
    public void SetRedirectionIsNotPartOfTheValue(string text, string expectedName, string? expectedValue, string expectedOperator, string expectedTarget)
    {
        var set = Assert.IsType<CmdSetStatementSyntax>(SingleStatement(text));
        var redirection = Assert.Single(set.Redirections);

        Assert.Equal(expectedName, set.Name);
        Assert.Equal(expectedValue, set.Value?.Value);
        Assert.Equal(expectedOperator, redirection.OperatorToken.Text);
        Assert.Equal(expectedTarget, redirection.Target?.Value);
        Assert.Empty(DiagnosticIds(text));
    }

    [Theory]
    // Quotes, escapes, and a redirection followed by more text keep the characters in the value.
    [InlineData("set x=\"a>b\"", "\"a>b\"")]
    [InlineData("set x=a^>b", "a>b")]
    [InlineData("set \"x=a>b\"", "a>b")]
    public void SetValueKeepsProtectedRedirectionCharacters(string text, string expectedValue)
    {
        var set = Assert.IsType<CmdSetStatementSyntax>(SingleStatement(text));

        Assert.Equal(expectedValue, set.Value?.Value);
        Assert.Empty(set.Redirections);
    }

    [Theory]
    [InlineData("(echo a & echo b) > out.txt", 1)]
    [InlineData("(\r\necho a\r\necho b\r\n) >> out.txt 2>&1", 2)]
    public void BlockRedirectionBelongsToTheBlock(string text, int expectedRedirections)
    {
        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(SingleStatement(text));

        Assert.Equal(expectedRedirections, block.Redirections.Count);
        Assert.Empty(DiagnosticIds(text));
    }

    [Theory]
    [InlineData("set \"NAME=value\"", "NAME", "value")]
    [InlineData("set \"NAME=value with spaces\"", "NAME", "value with spaces")]
    [InlineData("set \"NAME=a & b\"", "NAME", "a & b")]
    // Everything after the last quote is ignored.
    [InlineData("set \"NAME=value\" junk", "NAME", "value")]
    [InlineData("set /p \"NAME=Prompt: \"", "NAME", "Prompt: ")]
    [InlineData("set \"NAME=\"", "NAME", "")]
    public void QuotedSetAssignment_ExposesTheNameAndValueWithoutTheQuotes(string text, string expectedName, string expectedValue)
    {
        var set = Assert.IsType<CmdSetStatementSyntax>(SingleStatement(text));

        Assert.Equal(expectedName, set.Name);
        Assert.Equal(expectedValue, set.Value?.Value ?? "");
    }

    [Fact]
    public void UnquotedSetAssignment_KeepsTheQuotesInTheValue()
    {
        var set = Assert.IsType<CmdSetStatementSyntax>(SingleStatement("set x=\"a b\""));

        Assert.Equal("x", set.Name);
        Assert.Equal("\"a b\"", set.Value?.Value);
    }

    [Theory]
    // `,`, `;`, and `=` separate the items of a for set like spaces do; `for /l` relies on it.
    [InlineData("for %%i in (a,b;c) do echo %%i", new[] { "a", "b", "c" })]
    [InlineData("for %%i in (a=b) do echo %%i", new[] { "a", "b" })]
    [InlineData("for /l %%n in (1,1,10) do echo %%n", new[] { "1", "1", "10" })]
    [InlineData("for /l %%n in (1, 1, 10) do echo %%n", new[] { "1", "1", "10" })]
    [InlineData("for %%i in (\r\n  a\r\n  b\r\n) do echo %%i", new[] { "a", "b" })]
    // Inside the set, `rem` and `::` are items, not comments that would hide the rest of the line.
    [InlineData("for %%i in (rem x) do echo %%i", new[] { "rem", "x" })]
    [InlineData("for %%i in (::x y) do echo %%i", new[] { "::x", "y" })]
    public void ForSetItemsAreSplitOnTokenDelimiters(string text, string[] expectedItems)
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(SingleStatement(text));

        Assert.Equal(expectedItems, statement.Items.Select(item => item.Value));
        Assert.Empty(DiagnosticIds(text));
    }

    [Fact]
    public void ForFSetKeepsTheDelimitersOfItsCommand()
    {
        // `for /f` hands the text between the parentheses to its own parser, which is why `delims=,` works.
        var statement = Assert.IsType<CmdForStatementSyntax>(SingleStatement("for /f \"delims=,\" %%a in ('echo a,b') do echo %%a"));

        Assert.Equal(["'echo", "a,b'"], statement.Items.Select(item => item.Value));
    }

    [Theory]
    [InlineData("for /r %ROOT% %%f in (*) do echo %%f", "f", 1)]
    [InlineData("for /r \"%ROOT%\" %%f in (*) do echo %%f", "f", 1)]
    [InlineData("for /r %1 %%f in (*) do echo %%f", "f", 1)]
    [InlineData("for /d /r . %%d in (bin obj) do rd /s /q \"%%d\"", "d", 2)]
    [InlineData("for %%# in (a) do echo %%#", "#", 0)]
    [InlineData("for %i in (a) do echo %i", "i", 0)]
    public void ForVariableIsTheTokenBeforeIn(string text, string expectedVariable, int expectedSwitchArguments)
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(SingleStatement(text));

        Assert.Equal(expectedVariable, statement.VariableName);
        Assert.Equal(expectedSwitchArguments, statement.SwitchArguments.Count);
        Assert.Empty(DiagnosticIds(text));
    }

    [Theory]
    // cmd only recognizes a keyword that forms a whole token, so these run programs.
    [InlineData("for_each.bat arg")]
    [InlineData("set-env.cmd arg")]
    [InlineData("goto2 arg")]
    [InlineData("if-x.cmd arg")]
    [InlineData("call_me.bat arg")]
    [InlineData("ifconfig arg")]
    public void KeywordPrefixOfALongerWord_IsACommand(string text)
    {
        var command = Assert.IsType<ShellCommandSyntax>(SingleStatement(text));

        Assert.Equal(text[..text.IndexOf(' ', StringComparison.Ordinal)], command.NameValue);
        Assert.Empty(DiagnosticIds(text));
    }

    [Theory]
    [InlineData("goto:eof", typeof(CmdGotoStatementSyntax))]
    [InlineData("call:sub", typeof(CmdCallStatementSyntax))]
    [InlineData("set/a x=1", typeof(CmdSetStatementSyntax))]
    [InlineData("if/i a==b echo x", typeof(CmdIfStatementSyntax))]
    [InlineData("for/l %%n in (1,1,2) do echo %%n", typeof(CmdForStatementSyntax))]
    public void KeywordFollowedByASwitchOrColon_IsStillAKeyword(string text, Type expectedType)
    {
        Assert.IsType(expectedType, SingleStatement(text));
        Assert.Empty(DiagnosticIds(text));
    }

    [Theory]
    [InlineData("if defined_x==1 echo y")]
    [InlineData("if not_x==1 echo y")]
    [InlineData("if exist.txt==x echo y")]
    [InlineData("if errorlevel1==1 echo y")]
    public void IfOperatorPrefixOfALongerWord_IsAComparisonOperand(string text)
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(SingleStatement(text));

        Assert.False(statement.IsNegated);
        Assert.IsType<ShellBinaryExpressionSyntax>(statement.Condition);
    }

    [Theory]
    [InlineData("@if exist a (echo y) else (echo n)", typeof(CmdIfStatementSyntax))]
    [InlineData("@for %%i in (a) do echo %%i", typeof(CmdForStatementSyntax))]
    [InlineData("@set x=1", typeof(CmdSetStatementSyntax))]
    [InlineData("@goto :eof", typeof(CmdGotoStatementSyntax))]
    [InlineData("@call :sub", typeof(CmdCallStatementSyntax))]
    [InlineData("@(echo a)", typeof(CmdParenthesizedBlockSyntax))]
    public void AtPrefixDoesNotHideAKeyword(string text, Type expectedType)
    {
        Assert.IsType(expectedType, SingleStatement(text));
        Assert.Empty(DiagnosticIds(text));
    }

    [Fact]
    public void AtPrefixedIfBlockSpanningLinesParsesAsOneStatement()
    {
        const string Text = "@if not exist out (\r\n  mkdir out\r\n)\r\necho done\r\n";
        var tree = ShellSyntaxAssert.TextIsFaithful(Text, ShellDialect.Cmd);

        Assert.HasCount(2, tree.GetRoot().Statements.Statements);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Theory]
    // A caret at the end of a line escapes the first character of the next one, so this `&` is literal text.
    [InlineData("echo a^\r\n&b", "a&b")]
    [InlineData("echo a^\n|b", "a|b")]
    // When the next line is empty the escaped character is the line break itself, and the command goes on.
    [InlineData("echo a^\r\n\r\nb", "a\nb")]
    public void CaretAtTheEndOfALineEscapesTheNextCharacter(string text, string expectedValue)
    {
        var command = Assert.IsType<ShellCommandSyntax>(SingleStatement(text));

        Assert.Equal(expectedValue, Assert.Single(command.Arguments).Value);
    }

    [Fact]
    public void CaretContinuationBeforeAnOperatorAfterWhitespace_EscapesTheOperator()
    {
        var command = Assert.IsType<ShellCommandSyntax>(SingleStatement("echo a ^\r\n& b"));

        Assert.Equal(["a", "&", "b"], command.Arguments.Select(argument => argument.Value));
    }

    [Fact]
    public void CaretDoesNotEscapeAPercentExpansion()
    {
        // Percent expansion happens before carets are processed, so `^%PATH%` escapes the first expanded character.
        var command = Assert.IsType<ShellCommandSyntax>(SingleStatement("echo ^%PATH%"));

        Assert.Equal("PATH", Assert.Single(command.DescendantNodes().OfType<CmdVariableReferenceSyntax>()).Name);
    }

    [Theory]
    // Delayed expansion runs after the line is split, so a `!` cannot pair with one past an operator.
    [InlineData("echo Done! & echo ok!", 2)]
    [InlineData("echo a! | findstr b!", 1)]
    public void DelayedExpansionDoesNotSpanAnOperator(string text, int expectedStatements)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Equal(expectedStatements, tree.GetRoot().Statements.Statements.Count);
        Assert.Empty(tree.GetRoot().DescendantNodes().OfType<CmdVariableReferenceSyntax>());
    }

    [Theory]
    [InlineData("goto")]
    [InlineData("goto\r\necho hi")]
    [InlineData("if 1==1 goto")]
    public void GotoWithoutALabel_IsReported(string text)
    {
        ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Contains("SHELL0013", DiagnosticIds(text));
    }

    [Theory]
    [InlineData(")")]
    [InlineData(") comment & echo x")]
    [InlineData("echo a & ) echo b")]
    public void CloseParenWithNoOpenBlock_IgnoresTheRestOfTheLineAndIsReported(string text)
    {
        // With no block open, cmd discards a `)` in command position together with the rest of its line.
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Contains("SHELL0002", DiagnosticIds(text));
        Assert.DoesNotContain(tree.GetRoot().DescendantNodes().OfType<ShellCommandSyntax>(), command => command.NameValue is ")" or "x" or "b");
    }

    [Fact]
    public void CloseParenWithNoOpenBlock_DoesNotSwallowTheNextLine()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(")\r\necho after\r\n", ShellDialect.Cmd);

        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[^1]).NameValue);
    }

    [Theory]
    [InlineData("if", "SHELL0040")]
    [InlineData("if 1==", "SHELL0040")]
    [InlineData("if 1== \r\necho hi", "SHELL0040")]
    [InlineData("if exist", "SHELL0040")]
    [InlineData("if not defined", "SHELL0040")]
    [InlineData("if 1 equ", "SHELL0040")]
    [InlineData("if a b echo x", "SHELL0041")]
    public void IncompleteIfCondition_IsReported(string text, string expectedId)
    {
        ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Contains(expectedId, DiagnosticIds(text));
    }

    [Fact]
    public void IfConditionWhoseOperatorComesFromAVariable_IsNotReported()
    {
        // `set C=1==1` makes `if %C% echo hi` a valid comparison, so the parser cannot tell.
        Assert.Empty(DiagnosticIds("if %C% echo hi"));
    }

    [Theory]
    [InlineData("for %%i\r\necho hi\r\n")]
    [InlineData("for %%i in\r\necho hi\r\n")]
    [InlineData("for %%i in (a)\r\necho hi\r\n")]
    [InlineData("for %%i in a do echo %%i\r\necho hi\r\n")]
    public void MalformedForHeader_IsReportedWithoutSwallowingTheNextLine(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Contains("SHELL0012", DiagnosticIds(text));
        Assert.IsType<CmdForStatementSyntax>(tree.GetRoot().Statements.Statements[0]);
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[^1]).NameValue);
    }

    [Fact]
    public void MissingForParenthesis_StillFindsTheBody()
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(ShellSyntaxTree.ParseText("for %%i in a do echo %%i", ShellDialect.Cmd).GetRoot().Statements.Statements[0]);

        Assert.True(statement.DoKeyword.IsPresent());
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(statement.Body).NameValue);
    }

    [Fact]
    public void UnclosedBlock_KeepsTheStatementsAfterItAsNodes()
    {
        // cmd reads to the end of the file looking for the `)`, so the rest of the script belongs to the block.
        const string Text = "if exist a (\r\n  echo a\r\nif exist b (echo b) else (echo c)\r\nfor %%i in (x) do echo %%i\r\n:end\r\necho done\r\n";
        var tree = ShellSyntaxAssert.TextIsFaithful(Text, ShellDialect.Cmd);

        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("SHELL0009", diagnostic.Id);
        var block = Assert.IsType<CmdParenthesizedBlockSyntax>(Assert.IsType<CmdIfStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements)).Body);
        Assert.Equal(
            [typeof(ShellCommandSyntax), typeof(CmdIfStatementSyntax), typeof(CmdForStatementSyntax), typeof(CmdLabelStatementSyntax), typeof(ShellCommandSyntax)],
            block.Statements.Statements.Select(statement => statement.GetType()));
    }

    [Theory]
    [InlineData("()")]
    [InlineData("if 1==1 (\r\n) else (echo b)")]
    public void EmptyBlock_IsReported(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Contains("SHELL0001", DiagnosticIds(text));
        Assert.NotEmpty(tree.GetRoot().DescendantNodes().OfType<CmdParenthesizedBlockSyntax>());
    }

    [Fact]
    public void BlockHoldingOnlyAComment_IsNotEmpty()
    {
        Assert.Empty(DiagnosticIds("if 1==1 (\r\n  rem nothing to do\r\n) else (echo b)\r\n"));
    }

    [Fact]
    public void TextAfterTheClosingParenthesisOfABlock_IsReported()
    {
        const string Text = "(echo a) b\r\necho c\r\n";
        var tree = ShellSyntaxAssert.TextIsFaithful(Text, ShellDialect.Cmd);

        Assert.Contains("SHELL0002", DiagnosticIds(Text));
        Assert.Equal("echo", Assert.IsType<ShellCommandSyntax>(tree.GetRoot().Statements.Statements[^1]).NameValue);
    }

    [Theory]
    [InlineData("echo a | | echo b")]
    [InlineData("echo a && && echo b")]
    [InlineData("echo > & echo b")]
    [InlineData("echo \"a & echo b")]
    [InlineData("if exist a (echo a) else")]
    [InlineData("for /f \"tokens=1 %%a in (x) do echo %%a")]
    [InlineData("set \"x=1")]
    [InlineData("@(")]
    [InlineData("@if")]
    [InlineData("for %%i in (a) do (")]
    [InlineData("if a==b (echo) else (")]
    [InlineData(") ^\r\nx")]
    public void MalformedInputReportsAndRoundTrips(string text)
    {
        ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.NotEmpty(DiagnosticIds(text));
    }

    [Theory]
    [InlineData("@echo off\r\nsetlocal EnableDelayedExpansion\r\nset /p VERSION=<version.txt\r\n(\r\n  echo !VERSION!\r\n) > out.txt\r\n")]
    [InlineData("call :build || goto :error\r\ngoto :eof\r\n:build\r\nmsbuild ^\r\n  /p:Configuration=Release\r\nexit /b %errorlevel%\r\n:error\r\nexit /b 1\r\n")]
    [InlineData("if errorlevel 1 echo failed & exit /b 1\r\n")]
    [InlineData("for /d /r . %%d in (bin obj) do @if exist \"%%d\" rd /s /q \"%%d\"\r\n")]
    [InlineData("for /f \"usebackq tokens=1,2 delims==\" %%a in (\"config.ini\") do set \"%%a=%%b\"\r\n")]
    [InlineData("set \"PATH=%PATH%;C:\\Program Files (x86)\\tool\"\r\n")]
    [InlineData("if \"%~1\"==\"\" (echo usage) else if /i \"%~1\"==\"--help\" (echo help) else (call :run %*)\r\n")]
    [InlineData("echo Done!\r\n")]
    [InlineData("(for %%i in (a b) do (\r\n  echo %%i\r\n)) 2>nul\r\n")]
    public void RealisticScripts_ParseWithoutDiagnostics(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Cmd);

        Assert.Empty(tree.GetDiagnostics());
    }
}
