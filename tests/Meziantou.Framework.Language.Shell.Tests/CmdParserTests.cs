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
        Assert.Equal(text, ShellSyntaxTree.ParseText(text, ShellDialect.Cmd).Root.ToFullString());
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

        Assert.IsType<CmdLabelStatementSyntax>(call.Target);
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
        Assert.NotEmpty(statement.Condition.Text);
    }

    [Fact]
    public void If_ExposesTheElseClause()
    {
        var statement = Assert.IsType<CmdIfStatementSyntax>(ShellSyntaxTree.ParseCommand("if exist a (echo yes) else (echo no)", ShellDialect.Cmd));

        Assert.IsType<CmdParenthesizedBlockSyntax>(statement.Body);
        Assert.NotNull(statement.ElseClause);
        Assert.IsType<CmdParenthesizedBlockSyntax>(statement.ElseClause.Body);
    }

    [Fact]
    public void For_ExposesVariableItemsAndSwitch()
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(ShellSyntaxTree.ParseCommand("for %%i in (a b c) do echo %%i", ShellDialect.Cmd));

        Assert.Equal("i", statement.VariableName);
        Assert.Null(statement.SwitchToken);
        Assert.Equal(3, statement.Items.Count);
        Assert.IsType<ShellCommandSyntax>(statement.Body);
    }

    [Fact]
    public void For_SupportsSwitchesAndOptionStrings()
    {
        var statement = Assert.IsType<CmdForStatementSyntax>(
            ShellSyntaxTree.ParseCommand("for /f \"tokens=1,2\" %%a in (data.txt) do echo %%a", ShellDialect.Cmd));

        Assert.Equal("/f", statement.SwitchToken?.Text);
        Assert.Single(statement.SwitchArguments);
        Assert.Equal("a", statement.VariableName);
    }

    [Fact]
    public void VariableReferences_AreClassified()
    {
        var tree = ShellSyntaxTree.ParseText("echo %PATH% %1 %~dp0", ShellDialect.Cmd);
        var references = tree.Root.DescendantNodes().OfType<CmdVariableReferenceSyntax>().ToArray();

        Assert.HasCount(3, references);
        Assert.Equal("PATH", references[0].Name);
        Assert.False(references[0].IsDelayed);
        Assert.False(references[0].IsLoopVariable);
        Assert.Equal("1", references[1].Name);
        Assert.Null(references[1].CloseToken);
    }

    [Fact]
    public void DelayedExpansion_IsRecognized()
    {
        var tree = ShellSyntaxTree.ParseText("echo !COUNT!", ShellDialect.Cmd);
        var reference = Assert.Single(tree.Root.DescendantNodes().OfType<CmdVariableReferenceSyntax>());

        Assert.True(reference.IsDelayed);
        Assert.Equal("COUNT", reference.Name);
    }

    [Fact]
    public void LoopVariable_IsRecognized()
    {
        var tree = ShellSyntaxTree.ParseText("for %%i in (a) do echo %%i", ShellDialect.Cmd);
        var reference = Assert.Single(tree.Root.DescendantNodes().OfType<CmdVariableReferenceSyntax>());

        Assert.True(reference.IsLoopVariable);
        Assert.Equal("i", reference.Name);
    }

    [Fact]
    public void CommentsAreTrivia()
    {
        var tree = ShellSyntaxTree.ParseText("rem first\r\n:: second\r\necho hi\r\n", ShellDialect.Cmd);
        var comments = tree.Root.DescendantTrivia()
            .Where(trivia => trivia.Kind is ShellSyntaxKind.CmdRemCommentTrivia or ShellSyntaxKind.CmdDoubleColonCommentTrivia)
            .ToArray();

        Assert.HasCount(2, comments);
        Assert.Equal("rem first", comments[0].Text);
        Assert.Equal(":: second", comments[1].Text);
    }

    [Fact]
    public void RemIsOnlyACommentAtStatementStart()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("echo hi rem not a comment", ShellDialect.Cmd));

        Assert.DoesNotContain(command.DescendantTrivia(), trivia => trivia.Kind == ShellSyntaxKind.CmdRemCommentTrivia);
        Assert.Contains(command.Arguments, argument => argument.Value == "rem");
    }

    [Fact]
    public void CaretEscape_IsAWordPart()
    {
        var tree = ShellSyntaxTree.ParseText("echo a^&b", ShellDialect.Cmd);
        var escape = Assert.Single(tree.Root.DescendantNodes().OfType<ShellEscapeSequenceSyntax>());

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

        Assert.Empty(tree.Diagnostics);
        Assert.Equal(text, tree.Root.ToFullString());
    }

    [Theory]
    [InlineData("@rem comment\r\n")]
    [InlineData("@REM comment\r\n")]
    [InlineData("@ rem comment\r\n")]
    public void AtRemIsStillAComment(string text)
    {
        // `@` only suppresses echoing, so it does not stop `rem` from starting a comment.
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Cmd);

        Assert.Equal(text, Assert.Single(tree.Root.DescendantTrivia(), trivia => trivia.Kind == ShellSyntaxKind.CmdRemCommentTrivia).Text + "\r\n");
        Assert.Empty(tree.Root.Statements.Statements);
    }

    [Fact]
    public void AtBeforeAnOrdinaryCommandIsNotAComment()
    {
        var tree = ShellSyntaxTree.ParseText("@echo off\r\n", ShellDialect.Cmd);

        Assert.DoesNotContain(tree.Root.DescendantTrivia(), trivia => trivia.Kind == ShellSyntaxKind.CmdRemCommentTrivia);
        Assert.Single(tree.Root.Statements.Statements);
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

        Assert.Equal("VARDEL X", Assert.IsType<CmdLabelStatementSyntax>(call.Target).Name);
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
}
