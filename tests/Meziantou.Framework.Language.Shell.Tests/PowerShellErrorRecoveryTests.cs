namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// Syntax errors in the PowerShell family, and how the parser recovers from them. Every input below was run through
/// <c>[System.Management.Automation.Language.Parser]::ParseInput</c> on pwsh 7, which reported at least one error.
/// </summary>
public sealed class PowerShellErrorRecoveryTests
{
    private static readonly ShellDialect[] Dialects = [ShellDialect.PowerShell, ShellDialect.PowerShellCore];

    private static ShellStatementSyntax[] TopLevelStatements(ShellSyntaxTree tree) =>
        [.. tree.GetRoot().Statements.Statements.Where(statement => statement is not ShellEmptyStatementSyntax)];

    [Theory]
    // Statement keywords are reserved: without what has to follow them, they are malformed statements, not commands.
    [InlineData("if")]
    [InlineData("if { }")]
    [InlineData("if -x")]
    [InlineData("if () { }")]
    [InlineData("while")]
    [InlineData("while = 1")]
    [InlineData("for")]
    [InlineData("foreach")]
    [InlineData("foreach (a in $b) { }")]
    [InlineData("do")]
    [InlineData("do 1")]
    [InlineData("switch")]
    [InlineData("switch { }")]
    [InlineData("switch ($x) { }")]
    [InlineData("switch -foo ($x) { 1 { } }")]
    [InlineData("try")]
    [InlineData("try { }")]
    [InlineData("try { }\nGet-Date")]
    [InlineData("try { } catch [int], { }")]
    [InlineData("try { } catch [] { }")]
    [InlineData("trap")]
    [InlineData("trap 1")]
    [InlineData("function")]
    [InlineData("function { }")]
    [InlineData("function 'f' { }")]
    [InlineData("function $f { }")]
    [InlineData("filter { }")]
    [InlineData("class { }")]
    [InlineData("class 1")]
    [InlineData("class A : { }")]
    [InlineData("enum { }")]
    [InlineData("enum E : { A }")]
    [InlineData("data")]
    [InlineData("data 1")]
    [InlineData("data -SupportedCommand { }")]
    [InlineData("data -foo x { }")]
    [InlineData("using")]
    [InlineData("using x")]
    [InlineData("using foo bar")]
    [InlineData("using namespace")]
    [InlineData("using namespace a b")]
    [InlineData("Get-Date\nusing namespace System")]
    [InlineData("from")]
    // Named block keywords are keywords at the start of a script body, and then nothing else may stand beside them.
    [InlineData("process")]
    [InlineData("begin 1")]
    [InlineData("clean -eq 2")]
    [InlineData("begin { } 2")]
    [InlineData("param()\nbegin { }\nGet-Date")]
    // A pipeline has to end at a line break, a `;`, or a closing bracket.
    [InlineData("$x = 1 2")]
    [InlineData("1 2 3")]
    [InlineData("$a $b")]
    [InlineData("\"a\" \"b\"")]
    [InlineData("$x = 'a' 'b'")]
    [InlineData("(1) (2)")]
    [InlineData("{ 1 } 2")]
    [InlineData("@{ a = 1 } 2")]
    [InlineData("[int]::MaxValue 2")]
    [InlineData("1 > out.txt 2")]
    [InlineData("return 1 2")]
    [InlineData("$x.y ()")]
    [InlineData("$x .y")]
    [InlineData("$x = @\"\na\n\"@ 2")]
    // Parentheses hold a single pipeline, unlike `$( )`.
    [InlineData("(1; 2)")]
    [InlineData("(1\n2)")]
    [InlineData("$x = (1 \n+ 2)")]
    [InlineData("if ($a; $b) { }")]
    [InlineData("$x = ()")]
    // Expression mode has no bare words, so an operator needs a value after it.
    [InlineData("1 + abc")]
    [InlineData("$x = 1 +\n\nGet-Date")]
    [InlineData("$a -is int")]
    [InlineData("-not abc")]
    [InlineData("[int]abc")]
    [InlineData("$x.M(abc)")]
    [InlineData("$x[abc]")]
    [InlineData("$x = 1, abc")]
    [InlineData("$a ? b : c")]
    [InlineData("param($a = abc)")]
    [InlineData("-")]
    [InlineData("- }")]
    [InlineData("+")]
    [InlineData("+foo")]
    [InlineData("--foo")]
    [InlineData(",abc")]
    [InlineData("! abc")]
    // Missing or malformed pieces of expressions.
    [InlineData("$x.")]
    [InlineData("$x.y.")]
    [InlineData("$x\n.y()")]
    [InlineData("Write-Host $a.)")]
    [InlineData("[]")]
    [InlineData("[int]]")]
    [InlineData("[System).IO.Path]::GetTempPath()")]
    [InlineData("[System@IO]::x")]
    [InlineData("@")]
    [InlineData("$x = @")]
    [InlineData("Get-ChildItem @-Recurse")]
    [InlineData("${a{b}")]
    [InlineData("\"$a: b\"")]
    [InlineData("$a:")]
    [InlineData("$x = $global:^")]
    [InlineData("@\"abc\"@")]
    [InlineData("@{ a = 1 b = 2 }")]
    [InlineData("@{ a-b = 1 }")]
    [InlineData("$h = @{ a\n= 1 }")]
    [InlineData("param($a,)")]
    [InlineData("param(1)")]
    [InlineData("param($a = ,1)")]
    [InlineData("param(\n    [Parameter(]\n    [string]$x\n)")]
    [InlineData("function f { param() param() }")]
    [InlineData("Get-Date\n[CmdletBinding()]\nparam($a)")]
    [InlineData("$x = [0]")]
    [InlineData("$x.M(,1)")]
    [InlineData("$x.M(1,,2)")]
    [InlineData("$a??$b")]
    // Assignments need something to assign to.
    [InlineData("1 = 2")]
    [InlineData("-$x = 1")]
    [InlineData("$x++ = 1")]
    // Commands and pipelines.
    [InlineData("&")]
    [InlineData(".")]
    [InlineData("$x = &")]
    [InlineData("a | &")]
    [InlineData("& | a")]
    [InlineData("a;&")]
    [InlineData("&& a")]
    [InlineData("a | $x")]
    [InlineData("a | { }")]
    [InlineData("Get-Item -Path:")]
    [InlineData("Get-Item -Path: ;")]
    [InlineData("Write-Host -Path ,a")]
    [InlineData("a & | b")]
    [InlineData("a & && b")]
    [InlineData("parallel { }")]
    public void SyntaxErrors_AreReported(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.PowerShellCore);

        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Theory]
    [MemberData(nameof(SyntaxErrorSamples))]
    public void SyntaxErrors_KeepEveryCharacterInBothDialects(string text)
    {
        foreach (var dialect in Dialects)
        {
            ShellSyntaxAssert.TextIsFaithful(text, dialect);
        }
    }

    public static TheoryData<string> SyntaxErrorSamples =>
    [
        "if ($a\nGet-Date\n$x = 1",
        "function f(\nGet-Date",
        "$h = @{\na = 1\n\nGet-Date\n$y = 2",
        "switch ($x) {\n1\n}\nGet-Date",
        "@\"abc\"@\nGet-Date",
        "$x = [int\nGet-Date",
        "$x = (1 +\nGet-Date",
        "a & b",
        "a && b || c",
        "a\n| b",
        "$x.($y",
        "${a`}",
        "switch ($x) { 'a' { } ;; ; 'b' { } }",
        "@{ a = 1 ;; ; b = 2 }",
        "data x -SupportedCommand",
        "using namespace",
        "class A {\n  [void] M() { }\n",
        "$x.Where{",
        "Write-Host $a.b(",
        "1..",
        "[int] {",
    ];

    [Fact]
    public void MissingCloseParenthesis_DoesNotSwallowTheStatementsThatFollow()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("if ($a\nGet-Date\n$x = 1", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(3, statements);
        Assert.IsType<PowerShellIfStatementSyntax>(statements[0]);
        Assert.Equal("Get-Date", Assert.IsType<ShellCommandSyntax>(statements[1]).NameValue);
        Assert.IsType<PowerShellExpressionStatementSyntax>(statements[2]);
        Assert.Equal("SHELL0012", Assert.Single(tree.GetDiagnostics()).Id);
    }

    [Fact]
    public void MissingLoopBody_LeavesTheNextLineAlone()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("while ($true\nGet-Date\nif ($x) { }", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(3, statements);
        Assert.IsType<PowerShellWhileStatementSyntax>(statements[0]);
        Assert.IsType<ShellCommandSyntax>(statements[1]);
        Assert.IsType<PowerShellIfStatementSyntax>(statements[2]);
        Assert.Single(tree.GetDiagnostics());
    }

    [Fact]
    public void MissingForEachBody_LeavesTheNextLineAlone()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("foreach ($a in $b)\nGet-Date", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(2, statements);
        Assert.IsType<PowerShellForEachStatementSyntax>(statements[0]);
        Assert.Equal("Get-Date", Assert.IsType<ShellCommandSyntax>(statements[1]).NameValue);
    }

    [Fact]
    public void UnclosedHashLiteral_EndsBeforeTheNextStatement()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("$h = @{\na = 1\n\nGet-Date\n$y = 2", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(3, statements);
        Assert.Single(Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellHashLiteralSyntax>()).Entries);
        Assert.Equal("Get-Date", Assert.IsType<ShellCommandSyntax>(statements[1]).NameValue);
        Assert.Single(tree.GetDiagnostics());
    }

    [Fact]
    public void SwitchClauseWithoutBody_EndsTheSwitch()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("switch ($x) {\n1\n}\nGet-Date", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(2, statements);
        Assert.IsType<PowerShellSwitchStatementSyntax>(statements[0]);
        Assert.IsType<ShellCommandSyntax>(statements[1]);
    }

    [Fact]
    public void UnexpectedToken_StartsANewStatement()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("$x = 1 2\nGet-Date", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(3, statements);
        var diagnostic = Assert.Single(tree.GetDiagnostics());
        Assert.Equal("SHELL0002", diagnostic.Id);
        Assert.Equal(7, diagnostic.Location.SourceSpan.Start);
    }

    [Fact]
    public void StrayCloseParenthesis_IsReportedOnce()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("Get-Date )\nGet-Host", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(3, statements);
        Assert.IsType<ShellSkippedTextSyntax>(statements[1]);
        Assert.Equal("Get-Host", Assert.IsType<ShellCommandSyntax>(statements[2]).NameValue);
        Assert.Single(tree.GetDiagnostics());
    }

    [Fact]
    public void MalformedHereStringHeader_OnlyTakesItsOwnLine()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("@\"abc\"@\nGet-Date", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(2, statements);
        Assert.Equal("Get-Date", Assert.IsType<ShellCommandSyntax>(statements[1]).NameValue);
        Assert.Equal("SHELL0022", Assert.Single(tree.GetDiagnostics()).Id);
    }

    [Fact]
    public void UnclosedTypeLiteral_EndsAtItsLine()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("$x = [int\nGet-Date", ShellDialect.PowerShellCore);

        Assert.Equal("Get-Date", Assert.IsType<ShellCommandSyntax>(TopLevelStatements(tree)[^1]).NameValue);
        Assert.Single(tree.GetDiagnostics());
    }

    [Fact]
    public void MissingOperand_LeavesTheWordToTheNextStatement()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("1 + abc", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.HasCount(2, statements);
        Assert.IsType<PowerShellBinaryExpressionSyntax>(Assert.IsType<PowerShellExpressionStatementSyntax>(statements[0]).Expression);
        Assert.Equal("abc", Assert.IsType<ShellCommandSyntax>(statements[1]).NameValue);
        Assert.Equal(["SHELL0023", "SHELL0002"], tree.GetDiagnostics().Select(diagnostic => diagnostic.Id));
    }

    [Fact]
    public void MissingOperandAtEndOfLine_LeavesTheNextLineAlone()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("$x = (1 +\nGet-Date", ShellDialect.PowerShellCore);

        Assert.Equal("Get-Date", Assert.IsType<ShellCommandSyntax>(TopLevelStatements(tree)[^1]).NameValue);
        Assert.Equal("SHELL0023", Assert.Single(tree.GetDiagnostics()).Id);
    }

    [Theory]
    [InlineData("function f(")]
    [InlineData("do")]
    [InlineData("if")]
    [InlineData("foreach ($a in $b")]
    [InlineData("switch ($x)")]
    public void MissingPiecesAtTheSamePlace_AreReportedOnce(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.PowerShellCore);

        Assert.Single(tree.GetDiagnostics());
    }

    [Fact]
    public void KeywordWithoutItsSyntax_IsStillThatStatement()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("if", ShellDialect.PowerShellCore);

        Assert.IsType<PowerShellIfStatementSyntax>(Assert.Single(TopLevelStatements(tree)));
    }

    [Fact]
    public void NamedBlocks_DoNotMixWithStatements()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("begin { }\nGet-Date", ShellDialect.PowerShellCore);
        var statements = TopLevelStatements(tree);

        Assert.IsType<PowerShellNamedBlockSyntax>(statements[0]);
        Assert.IsType<ShellCommandSyntax>(statements[1]);
        Assert.Equal(10, Assert.Single(tree.GetDiagnostics()).Location.SourceSpan.Start);
    }

    [Fact]
    public void UsingAfterAnotherStatement_IsReported()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("Get-Date\nusing namespace System", ShellDialect.PowerShellCore);

        Assert.IsType<PowerShellUsingStatementSyntax>(TopLevelStatements(tree)[1]);
        Assert.Equal("SHELL0024", Assert.Single(tree.GetDiagnostics()).Id);
    }

    [Fact]
    public void InvalidAssignmentTarget_IsReported()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("1 = 2", ShellDialect.PowerShellCore);

        Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellAssignmentExpressionSyntax>());
        Assert.Equal("SHELL0027", Assert.Single(tree.GetDiagnostics()).Id);
    }

    [Fact]
    public void BackgroundOperator_SeparatesStatementsInPowerShellCore()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("Start-Sleep 1 & Get-Date", ShellDialect.PowerShellCore);

        Assert.Empty(tree.GetDiagnostics());
        Assert.HasCount(2, TopLevelStatements(tree));
        Assert.Equal("&", tree.GetRoot().Statements.SeparatorTokens[0].Text);
    }

    [Theory]
    [InlineData("Start-Sleep 1 &")]
    [InlineData("a && b")]
    [InlineData("a || b")]
    public void PowerShellSevenOperators_AreErrorsInWindowsPowerShell(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.PowerShell);

        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.Empty(ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore).GetDiagnostics());
    }

    [Theory]
    [InlineData("Get-Process\n| Select-Object Name")]
    [InlineData("Get-Process # note\n| Select-Object Name")]
    [InlineData("Get-Process\n  # note\n  | Select-Object Name")]
    public void PipelineMayContinueWithAPipeOnTheNextLine(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.GetDiagnostics());
        Assert.HasCount(2, Assert.IsType<ShellPipelineSyntax>(Assert.Single(TopLevelStatements(tree))).Commands);
        Assert.NotEmpty(ShellSyntaxTree.ParseText(text, ShellDialect.PowerShell).GetDiagnostics());
    }

    [Fact]
    public void PipeAfterABlankLine_DoesNotContinueThePipeline()
    {
        var tree = ShellSyntaxAssert.TextIsFaithful("Get-Process\n\n| Select-Object Name", ShellDialect.PowerShellCore);

        Assert.NotEmpty(tree.GetDiagnostics());
    }
}
