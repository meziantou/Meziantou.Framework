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
}
