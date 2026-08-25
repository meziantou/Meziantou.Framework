namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// Pins the behaviour of constructs the tree does not model with a dedicated node. None of them is an error: the
/// text is preserved faithfully and no diagnostic is reported, they simply parse as ordinary words or commands.
/// These tests exist so the boundary is explicit and a future change to any of them is a deliberate one.
/// </summary>
public sealed class ShellUnsupportedConstructTests
{
    private static ShellSyntaxTree ParsesCleanly(string text, ShellDialect dialect)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, dialect);

        Assert.Empty(tree.Diagnostics);

        return tree;
    }

    [Theory]
    // Brace expansion, extended globs, and the deprecated `$[ ]` arithmetic are kept as plain word text.
    [InlineData("echo {a,b}.txt")]
    [InlineData("echo {1..5}")]
    [InlineData("echo !(a).txt")]
    [InlineData("echo @(a|b)")]
    [InlineData("echo $[1+2]")]
    [InlineData("echo ${var@Q}")]
    [InlineData("echo ${var//a/b}")]
    public void Bash_ConstructsWithoutADedicatedNode_StillParseCleanly(string text)
    {
        ParsesCleanly(text, ShellDialect.Bash);
    }

    [Theory]
    // DSC and `#requires` are not modeled; the former is a command, the latter is comment trivia.
    [InlineData("configuration C { Node 'x' { } }")]
    [InlineData("#requires -Version 7\nGet-Date")]
    public void PowerShell_ConstructsWithoutADedicatedNode_StillParseCleanly(string text)
    {
        ParsesCleanly(text, ShellDialect.PowerShellCore);
    }

    [Theory]
    // These are ordinary commands as far as the tree is concerned; only their arguments carry meaning.
    [InlineData("setlocal enabledelayedexpansion\r\n")]
    [InlineData("exit /b 1\r\n")]
    [InlineData("shift /1\r\n")]
    [InlineData("pushd d\r\npopd\r\n")]
    public void Cmd_BuiltinsWithoutADedicatedNode_StillParseCleanly(string text)
    {
        ParsesCleanly(text, ShellDialect.Cmd);
    }

    [Theory]
    [InlineData("a+=b", true)]
    [InlineData("a=b", false)]
    public void Bash_AppendAssignmentIsRecognized(string text, bool expectedAppend)
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.Bash));
        var assignment = Assert.Single(command.Assignments);

        Assert.Equal("a", assignment.Name);
        Assert.Equal("b", assignment.Value?.Value);
        Assert.Equal(expectedAppend, assignment.IsAppend);
    }

    [Fact]
    public void Sh_HasNoAppendAssignment()
    {
        // `+=` is a bash and zsh extension; in sh the whole thing is a command name.
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("a+=b", ShellDialect.Sh));

        Assert.Empty(command.Assignments);
        Assert.Equal("a+=b", command.NameValue);
    }

    [Fact]
    public void PowerShell_StopParsingTokenMakesTheRestOfTheLineLiteral()
    {
        const string Text = "cmd --% /c echo $x & more";
        var tree = ParsesCleanly(Text, ShellDialect.PowerShellCore);

        // Nothing after `--%` is interpreted: no variable, no `&` separator, one statement.
        Assert.Single(tree.Root.Statements.Statements);
        Assert.Empty(tree.Root.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());
    }

    [Fact]
    public void PowerShell_StopParsingOnlyAffectsItsOwnLine()
    {
        const string Text = "cmd --% /c $x\nGet-Item $y\n";
        var tree = ParsesCleanly(Text, ShellDialect.PowerShellCore);

        Assert.HasCount(2, tree.Root.Statements.Statements);
        Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());
    }

    [Theory]
    // A stop-parsing token in an odd position must not shift the spans of what follows it.
    [InlineData("continue||*:lbl try --%|")]
    [InlineData("do return >clean --%)")]
    [InlineData("cmd --%")]
    [InlineData("cmd --% ")]
    [InlineData("cmd --%\nnext")]
    public void PowerShell_StopParsingKeepsSpansFaithful(string text)
    {
        ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.PowerShellCore);
    }
}
