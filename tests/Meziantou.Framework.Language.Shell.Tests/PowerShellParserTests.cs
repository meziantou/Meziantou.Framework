namespace Meziantou.Framework.Language.Shell.Tests;

public sealed class PowerShellParserTests
{
    private static readonly ShellDialect[] Dialects = [ShellDialect.PowerShell, ShellDialect.PowerShellCore];

    public static TheoryData<string> Samples =>
    [
        "",
        "\n",
        "Get-ChildItem",
        "Get-ChildItem -Path C:\\temp -Recurse",
        "Get-Process | Where-Object { $_.CPU -gt 10 } | Select-Object Name",
        "$x = 1",
        "$x += 1",
        "$x = 'literal'",
        "$x = \"expandable $y\"",
        "$x = \"nested $($a.B) tail\"",
        "$x = @(1, 2, 3)",
        "$x = @{ a = 1; b = 'two' }",
        "$x = 1, 2, 3",
        "$x = 1..10",
        "$x = $a -eq $b",
        "$x = $a -and $b -or $c",
        "$x = -not $a",
        "$x = !$a",
        "$x = [int]'42'",
        "$x = [System.IO.Path]::GetFileName($p)",
        "$x = $obj.Property.Nested",
        "$x = $obj.Method(1, 'two')",
        "$x = $list[0]",
        "$x = $list[0..2]",
        "$i++",
        "--$i",
        "$env:PATH",
        "${weird name}",
        "$script:value = 3",
        "if ($a) { 'yes' }",
        "if ($a) { 'yes' } elseif ($b) { 'maybe' } else { 'no' }",
        "if ($a)\n{\n  'yes'\n}\nelse\n{\n  'no'\n}\n",
        "while ($true) { break }",
        "do { $i++ } while ($i -lt 10)",
        "do { $i++ } until ($i -ge 10)",
        "for ($i = 0; $i -lt 10; $i++) { $i }",
        "for (;;) { break }",
        "foreach ($item in $items) { $item }",
        "switch ($x) { 1 { 'one' } default { 'other' } }",
        "switch -Regex ($x) { '^a' { 'a' } }",
        "try { risky } catch { 'failed' }",
        "try { risky } catch [System.IO.IOException] { 'io' } catch { 'other' } finally { 'done' }",
        "trap { 'trapped' }",
        "trap [Exception] { continue }",
        "function Get-Thing { 'thing' }",
        "function Get-Thing($a, $b) { $a + $b }",
        "filter Select-Even { if ($_ % 2 -eq 0) { $_ } }",
        "param($Name, $Age)",
        "param([string]$Name = 'x', [int]$Age)",
        "[CmdletBinding()]\nparam([Parameter(Mandatory)][string]$Name)\n",
        "class Widget { }",
        "class Widget : Base { }",
        "enum Color { Red; Green }",
        "begin { 'b' }",
        "process { 'p' }",
        "end { 'e' }",
        "dynamicparam { 'd' }",
        "data Strings { 'x' }",
        "using namespace System.IO",
        "return",
        "return $x",
        "throw 'boom'",
        "exit 1",
        "break",
        "continue",
        ":outer while ($true) { break outer }",
        "$block = { param($a) $a * 2 }",
        "& $command arg",
        "Write-Host 'hi' > out.txt",
        "Write-Host 'hi' 2>&1",
        "# a comment\nGet-Date # trailing\n",
        "<# block\n   comment #>\nGet-Date\n",
        "Get-Date `\n  -Format o",
        "@\"\nhere string $x\n\"@",
        "@'\nverbatim here\n'@",
        "'unterminated",
        "\"unterminated",
        "if ($a) {",
        "function",
        "@{",
        "@(",
        "$",
        "[",
        "}",
        ")",
        ";;",
        // Shapes taken from scripts shipped with Windows and with the modules installed alongside it.
        "foreach ($d in Get-ChildItem -Path $p -Directory) { $d }",
        "foreach ($d in Get-ChildItem | Sort-Object) { $d }",
        "for ($i = 0; Test-Path $p; $i++) { $i }",
        "return $x | Where-Object { $_ }",
        "return Get-Item -Path x",
        "throw New-Object System.Exception",
        "$count ++",
        "$x = ,1",
        "$x = @{ $parameter.Name = $parameter.Value }",
        "$x = @{ Names = $items | Sort-Object }",
        "$x = @{ ids = $a, $b, $c }",
        "$x = $xml.results.'test-case'",
        "$x = $info.$script:Version",
        "$x = [int]!$global:?",
        "[Type, Assembly]$x = $y",
        "$x = @\"\nouter $(if ($true)\n{\n@\"\ninner\n\"@\n})\n\"@\n",
    ];

    public static TheoryData<string> CoreOnlySamples =>
    [
        "$x = $a ? 'yes' : 'no'",
        "$x = $a ?? 'fallback'",
        "$x ??= 'fallback'",
        "build && test",
        "build || fallback",
        "clean { 'cleanup' }",
    ];

    [Theory]
    [MemberData(nameof(Samples))]
    public void ParseText_RoundTripsExactly(string text)
    {
        foreach (var dialect in Dialects)
        {
            Assert.Equal(text, ShellSyntaxTree.ParseText(text, dialect).GetRoot().ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(CoreOnlySamples))]
    public void ParseText_RoundTripsCoreOnlySyntaxInBothDialects(string text)
    {
        foreach (var dialect in Dialects)
        {
            Assert.Equal(text, ShellSyntaxTree.ParseText(text, dialect).GetRoot().ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void ParseText_NeverThrows(string text)
    {
        foreach (var dialect in Dialects)
        {
            Assert.Null(Record.Exception(() => ShellSyntaxTree.ParseText(text, dialect)));
        }
    }

    [Fact]
    public void Command_ExposesNameAndArguments()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("Get-ChildItem -Path C:\\temp -Recurse", ShellDialect.PowerShellCore));

        Assert.Equal("Get-ChildItem", command.NameValue);
        Assert.Equal(3, command.Arguments.Count);
        Assert.Equal("-Path", command.Arguments[0].Value);
    }

    [Fact]
    public void Pipeline_IsBuiltFromCommands()
    {
        var pipeline = Assert.IsType<ShellPipelineSyntax>(ShellSyntaxTree.ParseCommand("Get-Process | Where-Object { $_ } | Select-Object Name", ShellDialect.PowerShellCore));

        Assert.Equal(3, pipeline.Commands.Count);
    }

    [Fact]
    public void Assignment_IsAnExpressionStatement()
    {
        var statement = Assert.IsType<PowerShellExpressionStatementSyntax>(ShellSyntaxTree.ParseCommand("$x = 1 + 2", ShellDialect.PowerShellCore));
        var assignment = Assert.IsType<PowerShellAssignmentExpressionSyntax>(statement.Expression);

        Assert.Equal("x", Assert.IsType<PowerShellVariableExpressionSyntax>(assignment.Target).Name);
        Assert.Equal("=", assignment.OperatorToken.Text);
        Assert.IsType<PowerShellBinaryExpressionSyntax>(assignment.Value);
    }

    [Fact]
    public void Variable_ExposesScopeQualifiedName()
    {
        var tree = ShellSyntaxTree.ParseText("$env:PATH", ShellDialect.PowerShellCore);
        var variable = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());

        Assert.Equal("env:PATH", variable.Name);
        Assert.False(variable.IsSplatted);
    }

    [Fact]
    public void SplattedVariable_IsDetected()
    {
        var tree = ShellSyntaxTree.ParseText("Get-Thing @params", ShellDialect.PowerShellCore);
        var variable = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());

        Assert.True(variable.IsSplatted);
    }

    [Fact]
    public void ExpandableString_KeepsEmbeddedExpansions()
    {
        var tree = ShellSyntaxTree.ParseText("$m = \"name is $($user.Name) ok\"", ShellDialect.PowerShellCore);
        var text = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellExpandableStringSyntax>());

        Assert.Contains(text.Parts, part => part is PowerShellSubExpressionSyntax);
        Assert.Equal(SyntaxKind.PowerShellExpandableString, text.Kind());
    }

    [Fact]
    public void VerbatimString_ResolvesDoubledQuotes()
    {
        var tree = ShellSyntaxTree.ParseText("$m = 'it''s'", ShellDialect.PowerShellCore);
        var literal = tree.GetRoot().DescendantNodes().OfType<PowerShellLiteralExpressionSyntax>()
            .Single(node => node.Kind() == SyntaxKind.PowerShellStringLiteral);

        Assert.Equal("it's", literal.Value);
    }

    [Fact]
    public void HashLiteral_ExposesEntries()
    {
        var tree = ShellSyntaxTree.ParseText("$h = @{ a = 1; b = 'two' }", ShellDialect.PowerShellCore);
        var hash = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellHashLiteralSyntax>());

        Assert.Equal(2, hash.Entries.Count);
    }

    [Fact]
    public void TypeLiteralAndCast_AreDistinguished()
    {
        var castTree = ShellSyntaxTree.ParseText("$x = [int]'42'", ShellDialect.PowerShellCore);
        var cast = Assert.Single(castTree.GetRoot().DescendantNodes().OfType<PowerShellCastExpressionSyntax>());
        Assert.Equal("int", cast.Type.Name);

        var staticTree = ShellSyntaxTree.ParseText("[System.IO.Path]::GetFileName($p)", ShellDialect.PowerShellCore);
        var access = Assert.Single(staticTree.GetRoot().DescendantNodes().OfType<PowerShellMemberAccessExpressionSyntax>());
        Assert.True(access.IsStatic);
        Assert.Equal("GetFileName", access.MemberNameToken.Text);
    }

    [Fact]
    public void IfStatement_ExposesClauses()
    {
        var statement = Assert.IsType<PowerShellIfStatementSyntax>(
            ShellSyntaxTree.ParseCommand("if ($a) { 1 } elseif ($b) { 2 } else { 3 }", ShellDialect.PowerShellCore));

        Assert.Single(statement.ElseIfClauses);
        Assert.NotNull(statement.ElseClause);
    }

    [Fact]
    public void DoStatement_DistinguishesWhileFromUntil()
    {
        Assert.False(Assert.IsType<PowerShellDoStatementSyntax>(ShellSyntaxTree.ParseCommand("do { $i } while ($c)", ShellDialect.PowerShellCore)).IsUntil);
        Assert.True(Assert.IsType<PowerShellDoStatementSyntax>(ShellSyntaxTree.ParseCommand("do { $i } until ($c)", ShellDialect.PowerShellCore)).IsUntil);
    }

    [Fact]
    public void TryStatement_ExposesTypedCatches()
    {
        var statement = Assert.IsType<PowerShellTryStatementSyntax>(
            ShellSyntaxTree.ParseCommand("try { a } catch [System.IO.IOException], [ArgumentException] { b } finally { c }", ShellDialect.PowerShellCore));

        var catchClause = Assert.Single(statement.CatchClauses);
        Assert.Equal(2, catchClause.TypeFilters.Count);
        Assert.Equal("System.IO.IOException", catchClause.TypeFilters[0].Name);
        Assert.NotNull(statement.FinallyClause);
    }

    [Fact]
    public void FunctionDefinition_ExposesNameAndInlineParameters()
    {
        var definition = Assert.IsType<PowerShellFunctionDefinitionSyntax>(
            ShellSyntaxTree.ParseCommand("function Add-Two($a, $b) { $a + $b }", ShellDialect.PowerShellCore));

        Assert.Equal("Add-Two", definition.Name);
        Assert.Equal(2, definition.Parameters.Count);
        Assert.False(definition.IsFilter);
    }

    [Fact]
    public void Filter_IsDistinguishedFromFunction()
    {
        var definition = Assert.IsType<PowerShellFunctionDefinitionSyntax>(
            ShellSyntaxTree.ParseCommand("filter Only-Even { $_ }", ShellDialect.PowerShellCore));

        Assert.True(definition.IsFilter);
    }

    [Fact]
    public void ParamBlock_ExposesAttributesAndDefaults()
    {
        var statement = Assert.IsType<PowerShellParamBlockSyntax>(
            ShellSyntaxTree.ParseCommand("param([Parameter(Mandatory)][string]$Name = 'x', [int]$Age)", ShellDialect.PowerShellCore));

        Assert.Equal(2, statement.Parameters.Count);
        Assert.Equal(2, statement.Parameters[0].Attributes.Count);
        Assert.False(statement.Parameters[0].Attributes[0].IsTypeConstraint);
        Assert.True(statement.Parameters[0].Attributes[1].IsTypeConstraint);
        Assert.NotNull(statement.Parameters[0].DefaultValue);
    }

    [Fact]
    public void TypeDefinition_DistinguishesClassFromEnum()
    {
        Assert.Equal(SyntaxKind.PowerShellClassDefinition, ShellSyntaxTree.ParseCommand("class A { }", ShellDialect.PowerShellCore).Kind());
        Assert.Equal(SyntaxKind.PowerShellEnumDefinition, ShellSyntaxTree.ParseCommand("enum A { }", ShellDialect.PowerShellCore).Kind());

        var definition = Assert.IsType<PowerShellTypeDefinitionSyntax>(ShellSyntaxTree.ParseCommand("class Widget : Base { }", ShellDialect.PowerShellCore));
        Assert.Equal("Widget", definition.Name);
        Assert.Single(definition.BaseTypes);
    }

    [Fact]
    public void LabeledStatement_ExposesItsLabel()
    {
        var statement = Assert.IsType<PowerShellLabeledStatementSyntax>(
            ShellSyntaxTree.ParseCommand(":outer while ($true) { break }", ShellDialect.PowerShellCore));

        Assert.Equal("outer", statement.Label);
        Assert.IsType<PowerShellWhileStatementSyntax>(statement.Statement);
    }

    [Fact]
    public void TernaryAndNullCoalescing_AreCoreOnly()
    {
        var core = ShellSyntaxTree.ParseText("$x = $a ? 1 : 2", ShellDialect.PowerShellCore);
        Assert.Single(core.GetRoot().DescendantNodes().OfType<PowerShellTernaryExpressionSyntax>());

        var windows = ShellSyntaxTree.ParseText("$x = $a ? 1 : 2", ShellDialect.PowerShell);
        Assert.Empty(windows.GetRoot().DescendantNodes().OfType<PowerShellTernaryExpressionSyntax>());
    }

    [Fact]
    public void PipelineChainOperators_AreCoreOnly()
    {
        Assert.IsType<ShellCommandListSyntax>(ShellSyntaxTree.ParseCommand("build && test", ShellDialect.PowerShellCore));
        Assert.IsNotType<ShellCommandListSyntax>(ShellSyntaxTree.ParseCommand("build && test", ShellDialect.PowerShell));
    }

    [Fact]
    public void CleanBlock_IsCoreOnly()
    {
        Assert.IsType<PowerShellNamedBlockSyntax>(ShellSyntaxTree.ParseCommand("clean { 'x' }", ShellDialect.PowerShellCore));
        Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("clean { 'x' }", ShellDialect.PowerShell));
    }

    [Fact]
    public void Comments_AreTrivia()
    {
        var tree = ShellSyntaxTree.ParseText("# line\n<# block #>\nGet-Date\n", ShellDialect.PowerShellCore);

        var comments = tree.GetRoot().DescendantTrivia()
            .Where(trivia => trivia.Kind() is SyntaxKind.SingleLineCommentTrivia or SyntaxKind.MultiLineCommentTrivia)
            .ToArray();

        Assert.HasCount(2, comments);
        Assert.Equal("# line", comments[0].ToString());
        Assert.Equal("<# block #>", comments[1].ToString());
    }

    [Fact]
    public void HereStrings_KeepTheirBodyVerbatim()
    {
        var tree = ShellSyntaxTree.ParseText("$a = @\"\nline $x\n\"@\n", ShellDialect.PowerShellCore);
        var hereString = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellExpandableStringSyntax>());

        Assert.Equal(SyntaxKind.PowerShellHereString, hereString.Kind());
        Assert.Equal("@\"", hereString.OpenToken.Text);
        Assert.Equal("\"@", hereString.CloseToken.Text);
    }

    [Fact]
    public void ScriptBlockArgument_IsAnEmbeddedExpression()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("Where-Object { $_.Name }", ShellDialect.PowerShellCore));
        var argument = Assert.Single(command.Arguments);

        var embedded = Assert.IsType<ShellEmbeddedExpressionSyntax>(Assert.Single(argument.Parts));
        Assert.IsType<PowerShellScriptBlockSyntax>(embedded.Expression);
    }

    [Fact]
    public void MalformedInput_ProducesDiagnosticsWithoutThrowing()
    {
        var tree = ShellSyntaxTree.ParseText("if ($a) {", ShellDialect.PowerShellCore);

        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.Equal("if ($a) {", tree.GetRoot().ToFullString());
    }

    // Every input below was run through `[System.Management.Automation.Language.Parser]::ParseInput` on pwsh 7 and
    // reported no error, so a diagnostic here would be a false positive.

    [Theory]
    // Loop clauses accept a command or a pipeline where an expression would also fit.
    [InlineData("foreach ($d in Get-ChildItem -Path $p -Directory) { $d }")]
    [InlineData("foreach ($d in Get-ChildItem | Sort-Object) { $d }")]
    [InlineData("foreach ($d in (Get-ChildItem)) { $d }")]
    [InlineData("for ($i = 0; Test-Path $p; $i++) { $i }")]
    [InlineData("while (Test-Path $p) { break }")]
    // `return`, `throw`, and `exit` take a whole pipeline.
    [InlineData("return $x | Where-Object { $_ }")]
    [InlineData("return Get-Item -Path x")]
    [InlineData("throw New-Object System.Exception")]
    [InlineData("exit $LASTEXITCODE")]
    // Increment and decrement may be separated from their operand by spaces.
    [InlineData("$count ++")]
    [InlineData("$count --")]
    [InlineData("$a.b ++")]
    [InlineData("$a[0] ++")]
    // Null-conditional access, added in PowerShell 7.
    [InlineData("$x = $y?.z")]
    [InlineData("$x = $y?.z()")]
    [InlineData("$x = $y?[0]")]
    // Attributes carrying arguments in front of an assignment.
    [InlineData("[ValidateNotNull()]$x = 1")]
    [InlineData("[ValidateRange(1, 5)][int]$x = 1")]
    [InlineData("[Parameter()][string]$x = 'a'")]
    [InlineData("[ValidateSet('a', 'b')][string]$x = 'a'")]
    // Assembly-qualified type names keep the comma that separates type from assembly.
    [InlineData("[Some.Name.Space.Type, Some.Assembly]$x = $y")]
    [InlineData("function f { param([Some.Type, Some.Assembly]$x) }")]
    // Hash literals take expression keys, array values, and pipeline values.
    [InlineData("$x = @{ $parameter.Name = $parameter.Value }")]
    [InlineData("$x = @{ $global:state.Id = $global:state }")]
    [InlineData("$x = @{ Names = $items | Sort-Object }")]
    [InlineData("$x = @{ ids = $a, $b, $c }")]
    [InlineData("$x = @{ a = 1; b = 2 }")]
    // Member names may be quoted or given by a scoped variable.
    [InlineData("$x = $xml.results.'test-case'")]
    [InlineData("$x = $xml.results.\"test-case\"")]
    [InlineData("$x = $info.$script:Version")]
    [InlineData("$x = $info.$name")]
    // Automatic variables keep their scope prefix.
    [InlineData("$x = [int]!$global:?")]
    // The unary comma builds a one-element array.
    [InlineData("$x = ,1")]
    [InlineData(",1")]
    [InlineData("$x = ,$y")]
    // Switch clauses and hash entries may be separated by `;`, also at the start of a line.
    [InlineData("switch ($x) { 'a' { 1 } ; 'b' { 2 } }")]
    [InlineData("switch ($x) { 'a' { };; }")]
    [InlineData("switch ($x) { [int] { 1 } }")]
    [InlineData("$h = @{ a = 1\n; b = 2 }")]
    [InlineData("$h = @{ a = 1 ;; b = 2 }")]
    // Data sections: the block may start on the next line, and -SupportedCommand takes a list of commands.
    [InlineData("data\n{ 'a' }")]
    [InlineData("data x -SupportedCommand a, b { 'a' }")]
    [InlineData("try { } catch [a]\n, [b] { }")]
    // Pipeline chains are allowed wherever a pipeline is.
    [InlineData("foreach ($a in a && b) { }")]
    [InlineData("@{ a = b && c }")]
    [InlineData("return a && b")]
    // The value of an assignment is a statement.
    [InlineData("$x = if ($a) { 1 } else { 2 }")]
    [InlineData("$x = foreach ($i in 1..2) { $i }")]
    [InlineData("$x = switch ($a) { 1 { 2 } }")]
    [InlineData("$x = $y = 1")]
    // Assignment targets PowerShell accepts.
    [InlineData(",$x = 1")]
    [InlineData("$a.b, $c[0] = 1, 2")]
    [InlineData("$x.M() = 1")]
    [InlineData("[ValidateNotNull()][int]$x = 1")]
    [InlineData("$repoRoot == Split-Path x")]
    // Command arguments: arrays across `,` and member access on values.
    [InlineData("Write-Output a , b")]
    [InlineData("Write-Output a,\nb")]
    [InlineData("Write-Host -Path:a,b")]
    [InlineData("Write-Host $a.b.c()")]
    [InlineData("Write-Host (Get-Date).Year")]
    [InlineData("Write-Host $(1).ToString()")]
    [InlineData("Write-Host \"a\".Length")]
    [InlineData("Write-Host $a. b")]
    [InlineData("Get-Item $x ; Get-Date")]
    // Member access forms.
    [InlineData("$x.Where{ $_ }.Count")]
    [InlineData("$Matches.1")]
    [InlineData("$x. y")]
    [InlineData("$x.($name)")]
    [InlineData("$x.$($name)")]
    [InlineData("[int]::(1)")]
    [InlineData("[int]:: MaxValue")]
    [InlineData("$a::b()")]
    [InlineData("{ 1 }.Invoke()")]
    // Casts and numbers.
    [InlineData("$x -is [int] -and $y")]
    [InlineData("$x = [int] -1")]
    [InlineData("$x = [int]!$a")]
    [InlineData("$x = [int] { 1 }")]
    [InlineData("1.")]
    [InlineData("$x = 5.")]
    [InlineData("1kb + 0x10L")]
    // Statements that end with a block need no terminator.
    [InlineData("if ($true) { 1 } 2")]
    [InlineData("function f { } f")]
    [InlineData("param($a) Get-Date")]
    // Named blocks, and their keywords used as commands outside of that position.
    [InlineData("function f { begin { } ; process { } }")]
    [InlineData("function f { [CmdletBinding()] param($a) begin { } end { } }")]
    [InlineData("Get-Date; process { }")]
    // Background jobs, added in PowerShell 7.
    [InlineData("Start-Sleep 1 &")]
    [InlineData("(Start-Sleep 1 &)")]
    [InlineData("a && b &")]
    // Words that look like operators but are commands.
    [InlineData("$ foo")]
    [InlineData("!abc")]
    [InlineData("-abc")]
    [InlineData("+1")]
    [InlineData("$x = $y>out.txt")]
    // Variables.
    [InlineData("\"${a}:\"")]
    [InlineData("${a`}b}")]
    [InlineData("$env::x")]
    [InlineData("$a?b = 1")]
    [InlineData("\"$a?\"")]
    [InlineData("Write-Host $x@")]
    [InlineData("Write-Output a@b")]
    // Attributes may stand on their own line, before a comment, above what they apply to.
    [InlineData("[CmdletBinding()]\n# note\nparam()")]
    [InlineData("([Parameter()]\n$x)")]
    [InlineData("if ($a) { param($b) }")]
    [InlineData("param($a = (,1))")]
    [InlineData("function Print-Usage=() { }")]
    // Separators with whitespace between them.
    [InlineData("@{ a = 1 ;\n; b = 2 }")]
    [InlineData("switch ($x) { 1 {} ; ; 2 {} }")]
    // Class members, which the tree keeps as plain statements.
    [InlineData("class A { M() { } }")]
    [InlineData("class A { A() : base() { } }")]
    [InlineData("class A { hidden [string]$y = 'a'; static [int] N([int]$a) { return $a } }")]
    public void ConstructsAcceptedByPowerShell_ParseWithoutDiagnostics(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void ForEachOverACommand_KeepsTheCommandAsTheCollection()
    {
        var statement = Assert.IsType<PowerShellForEachStatementSyntax>(
            ShellSyntaxTree.ParseCommand("foreach ($d in Get-ChildItem -Directory) { $d }", ShellDialect.PowerShellCore));

        var command = Assert.IsType<ShellCommandSyntax>(statement.Collection);
        Assert.Equal("Get-ChildItem", command.NameValue);
    }

    [Fact]
    public void ForEachOverAPipeline_KeepsThePipelineAsTheCollection()
    {
        var statement = Assert.IsType<PowerShellForEachStatementSyntax>(
            ShellSyntaxTree.ParseCommand("foreach ($d in Get-ChildItem | Sort-Object) { $d }", ShellDialect.PowerShellCore));

        Assert.HasCount(2, Assert.IsType<ShellPipelineSyntax>(statement.Collection).Commands);
    }

    [Fact]
    public void ForConditionMayBeACommand()
    {
        var statement = Assert.IsType<PowerShellForStatementSyntax>(
            ShellSyntaxTree.ParseCommand("for ($i = 0; Test-Path $p; $i++) { $i }", ShellDialect.PowerShellCore));

        Assert.Equal("Test-Path", Assert.IsType<ShellCommandSyntax>(statement.Condition).NameValue);
        Assert.IsType<PowerShellAssignmentExpressionSyntax>(statement.Initializer);
    }

    [Fact]
    public void ReturnTakesTheWholeCommand()
    {
        var tree = ShellSyntaxTree.ParseText("return Get-Item -Path x", ShellDialect.PowerShellCore);
        var statement = Assert.IsType<PowerShellFlowStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));

        var command = Assert.IsType<ShellCommandSyntax>(statement.Value);
        Assert.Equal("Get-Item", command.NameValue);
        Assert.HasCount(2, command.Arguments);
    }

    [Fact]
    public void ReturnTakesTheWholePipeline()
    {
        var tree = ShellSyntaxTree.ParseText("return $x | Where-Object { $_ }", ShellDialect.PowerShellCore);
        var statement = Assert.IsType<PowerShellFlowStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));

        Assert.HasCount(2, Assert.IsType<ShellPipelineSyntax>(statement.Value).Commands);
    }

    [Fact]
    public void ReturnWithAPlainExpression_KeepsTheExpressionUnwrapped()
    {
        var tree = ShellSyntaxTree.ParseText("return $x", ShellDialect.PowerShellCore);
        var statement = Assert.IsType<PowerShellFlowStatementSyntax>(Assert.Single(tree.GetRoot().Statements.Statements));

        Assert.IsType<PowerShellVariableExpressionSyntax>(statement.Value);
    }

    [Theory]
    [InlineData("break outer")]
    [InlineData("continue outer")]
    public void BreakAndContinueTakeALabelRatherThanACommand(string text)
    {
        var statement = Assert.IsType<PowerShellFlowStatementSyntax>(ShellSyntaxTree.ParseCommand(text, ShellDialect.PowerShellCore));

        Assert.Equal("outer", Assert.IsType<ShellWordSyntax>(statement.Value).Value);
    }

    [Theory]
    [InlineData("$count++")]
    [InlineData("$count ++")]
    [InlineData("$count\t++")]
    public void PostfixIncrement_AllowsWhitespaceBeforeTheOperator(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);
        var unary = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellUnaryExpressionSyntax>());

        Assert.Equal(SyntaxKind.PowerShellPostfixUnaryExpression, unary.Kind());
        Assert.Equal("++", unary.PostfixOperatorToken.Text);
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void IncrementOnTheNextLine_IsNotAPostfixOperator()
    {
        var tree = ShellSyntaxTree.ParseText("$count\n++$other", ShellDialect.PowerShellCore);

        Assert.HasCount(2, tree.GetRoot().Statements.Statements);
    }

    [Theory]
    // A `?` is a variable name character, so pwsh reads `$y?.z` as the member `z` of the variable `y?`; the operator
    // only applies after a braced variable or another expression.
    [InlineData("$x = ${y}?.z", "?.")]
    [InlineData("$x = $y.z?.w", "?.")]
    [InlineData("$x = $y?.z", ".")]
    [InlineData("$x = $y.z", ".")]
    public void NullConditionalMemberAccess_KeepsItsOperatorText(string text, string expectedOperator)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);
        var access = tree.GetRoot().DescendantNodes().OfType<PowerShellMemberAccessExpressionSyntax>().First();

        Assert.Equal(expectedOperator, access.OperatorToken.Text);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Theory]
    [InlineData("$x = ${y}?[0]", "?[", "y")]
    [InlineData("$x = $y?[0]", "[", "y?")]
    public void NullConditionalIndex_IsAnIndexExpression(string text, string expectedBracket, string expectedVariable)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);
        var index = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellIndexExpressionSyntax>());

        Assert.Equal(expectedBracket, index.OpenBracketToken.Text);
        Assert.Equal(expectedVariable, Assert.IsType<PowerShellVariableExpressionSyntax>(index.Target).Name);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void NullConditionalIsNotConfusedWithTheTernaryOperator()
    {
        var tree = ShellSyntaxTree.ParseText("$x = $a ? $b.c : $d", ShellDialect.PowerShellCore);

        Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellTernaryExpressionSyntax>());
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void WindowsPowerShellDoesNotHaveNullConditionalAccess()
    {
        // `?.` arrived with PowerShell 7, so in Windows PowerShell the `?` cannot bind to the member access.
        var tree = ShellSyntaxTree.ParseText("$x = ${y}?.z", ShellDialect.PowerShell);

        Assert.Equal("$x = ${y}?.z", tree.GetRoot().ToFullString());
        Assert.DoesNotContain(tree.GetRoot().DescendantNodes().OfType<PowerShellMemberAccessExpressionSyntax>(), access => access.OperatorToken.Text == "?.");
        Assert.NotEmpty(tree.GetDiagnostics());
    }

    [Fact]
    public void AttributeWithArguments_IsOneTypeLiteral()
    {
        var tree = ShellSyntaxTree.ParseText("[ValidateRange(1, 5)][int]$x = 1", ShellDialect.PowerShellCore);
        var types = tree.GetRoot().DescendantNodes().OfType<PowerShellTypeLiteralSyntax>().ToArray();

        Assert.Equal(["ValidateRange(1, 5)", "int"], types.Select(type => type.Name));
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void AssemblyQualifiedTypeName_KeepsItsComma()
    {
        var tree = ShellSyntaxTree.ParseText("[Some.Type, Some.Assembly]$x = $y", ShellDialect.PowerShellCore);
        var type = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellTypeLiteralSyntax>());

        Assert.Equal("Some.Type, Some.Assembly", type.Name);
    }

    [Fact]
    public void TypeNameDoesNotRunPastItsLine()
    {
        // An unterminated bracket must not swallow the rest of the file.
        var tree = ShellSyntaxTree.ParseText("[Some.Type\nGet-Date\n", ShellDialect.PowerShellCore);

        Assert.Equal("[Some.Type\nGet-Date\n", tree.GetRoot().ToFullString());
        Assert.Contains(tree.GetRoot().DescendantNodes().OfType<ShellCommandSyntax>(), command => command.NameValue == "Get-Date");
    }

    [Fact]
    public void HashEntry_AcceptsAnExpressionKey()
    {
        var tree = ShellSyntaxTree.ParseText("$x = @{ $parameter.Name = $parameter.Value }", ShellDialect.PowerShellCore);
        var entry = Assert.Single(Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellHashLiteralSyntax>()).Entries);

        Assert.IsType<PowerShellMemberAccessExpressionSyntax>(entry.Key);
        Assert.IsType<PowerShellMemberAccessExpressionSyntax>(entry.Value);
    }

    [Fact]
    public void HashEntry_AcceptsAPipelineValue()
    {
        var tree = ShellSyntaxTree.ParseText("$x = @{ Names = $items | Sort-Object }", ShellDialect.PowerShellCore);
        var entry = Assert.Single(Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellHashLiteralSyntax>()).Entries);

        Assert.HasCount(2, Assert.IsType<ShellPipelineSyntax>(entry.Value).Commands);
    }

    [Fact]
    public void HashEntry_AcceptsAStatementValue()
    {
        var tree = ShellSyntaxTree.ParseText("$x = @{ a = if ($y) { 1 } else { 2 } }", ShellDialect.PowerShellCore);
        var entry = Assert.Single(Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellHashLiteralSyntax>()).Entries);

        Assert.IsType<PowerShellIfStatementSyntax>(entry.Value);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void AssignmentValue_MayBeAStatement()
    {
        var statement = Assert.IsType<PowerShellExpressionStatementSyntax>(ShellSyntaxTree.ParseCommand("$x = foreach ($i in 1..2) { $i }", ShellDialect.PowerShellCore));

        Assert.IsType<PowerShellForEachStatementSyntax>(Assert.IsType<PowerShellAssignmentExpressionSyntax>(statement.Expression).Value);
    }

    [Fact]
    public void CommandArgument_MayUseMemberAccessAndMethodCalls()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("Write-Host $item.Name.ToUpper() (Get-Date).Year", ShellDialect.PowerShellCore));

        Assert.HasCount(2, command.Arguments);
        var embedded = Assert.IsType<ShellEmbeddedExpressionSyntax>(Assert.Single(command.Arguments[0].Parts));
        Assert.IsType<PowerShellInvocationExpressionSyntax>(embedded.Expression);
    }

    [Fact]
    public void MethodCallWithAScriptBlock_NeedsNoParentheses()
    {
        var tree = ShellSyntaxTree.ParseText("$items.Where{ $_ }.Count", ShellDialect.PowerShellCore);
        var invocation = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellInvocationExpressionSyntax>());

        Assert.IsType<PowerShellScriptBlockSyntax>(Assert.Single(invocation.Arguments));
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void SwitchClauses_KeepTheirSemicolonSeparator()
    {
        var statement = Assert.IsType<PowerShellSwitchStatementSyntax>(ShellSyntaxTree.ParseCommand("switch ($x) { 1 { 'a' }; 2 { 'b' } }", ShellDialect.PowerShellCore));

        Assert.HasCount(2, statement.Clauses);
        Assert.Equal(";", statement.Clauses[0].SeparatorToken.Text);
    }

    [Fact]
    public void ParenthesizedExpression_HoldsASinglePipeline()
    {
        var tree = ShellSyntaxTree.ParseText("$x = (1; 2)", ShellDialect.PowerShellCore);

        Assert.NotEmpty(tree.GetDiagnostics());
        Assert.Equal("$x = (1; 2)", tree.GetRoot().ToFullString());
        Assert.Empty(ShellSyntaxTree.ParseText("$x = $(1; 2)", ShellDialect.PowerShellCore).GetDiagnostics());
    }

    [Fact]
    public void HashEntry_AcceptsAnArrayValue()
    {
        var tree = ShellSyntaxTree.ParseText("$x = @{ ids = $a, $b, $c }", ShellDialect.PowerShellCore);
        var entry = Assert.Single(Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellHashLiteralSyntax>()).Entries);

        Assert.HasCount(3, Assert.IsType<PowerShellArrayLiteralSyntax>(entry.Value).Elements);
    }

    [Theory]
    [InlineData("$x = $xml.results.'test-case'", "'test-case'")]
    [InlineData("$x = $xml.results.\"test-case\"", "\"test-case\"")]
    [InlineData("$x = $xml.results.$name", "$name")]
    [InlineData("$x = $xml.results.$script:name", "$script:name")]
    public void MemberName_MayBeQuotedOrAVariable(string text, string expectedName)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);
        // Member access nests to the left, so the outermost node in source order carries the last member name.
        var access = tree.GetRoot().DescendantNodes().OfType<PowerShellMemberAccessExpressionSyntax>().First();

        Assert.Equal(expectedName, access.MemberNameToken.Text);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Theory]
    [InlineData("$?", "?")]
    [InlineData("$^", "^")]
    [InlineData("$global:?", "global:?")]
    [InlineData("$script:?", "script:?")]
    public void AutomaticVariables_KeepTheirScopePrefix(string text, string expectedName)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.PowerShellCore);
        var variable = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());

        Assert.Equal(expectedName, variable.Name);
        Assert.Equal(text, tree.GetRoot().ToFullString());
    }

    [Fact]
    public void HereStringNestedInASubexpression_DoesNotEndTheOuterHereString()
    {
        const string Text = "$a = @\"\nouter $(if ($true)\n{\n@\"\ninner\n\"@\n})\ntail\n\"@\nGet-Date\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(Text, tree.GetRoot().ToFullString());

        // The outer here-string ends at the last `"@`, so `Get-Date` is still a command of its own.
        var hereString = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellExpandableStringSyntax>());
        Assert.Equal("@\"\nouter $(if ($true)\n{\n@\"\ninner\n\"@\n})\ntail\n\"@", tree.GetText().Text[hereString.Span.Start..hereString.Span.End]);
        Assert.Contains(tree.GetRoot().DescendantNodes().OfType<ShellCommandSyntax>(), command => command.NameValue == "Get-Date");
    }

    [Fact]
    public void VerbatimHereStringDoesNotExpandASubexpression()
    {
        // `@'` keeps everything verbatim, so the `$(` inside it is plain text and the first `'@` ends it.
        const string Text = "$a = @'\n$(1)\n'@\nGet-Date\n";
        var tree = ShellSyntaxTree.ParseText(Text, ShellDialect.PowerShellCore);

        Assert.Empty(tree.GetDiagnostics());
        Assert.Equal(Text, tree.GetRoot().ToFullString());
        Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellExpandableStringSyntax>());
    }

    [Fact]
    public void UnaryComma_BuildsAOneElementArray()
    {
        var tree = ShellSyntaxTree.ParseText("$x = ,1", ShellDialect.PowerShellCore);
        var assignment = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellAssignmentExpressionSyntax>());
        var unary = Assert.IsType<PowerShellUnaryExpressionSyntax>(assignment.Value);

        Assert.Equal(",", unary.PrefixOperatorToken.Text);
        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void CommaIsNotAStatementSeparator()
    {
        var tree = ShellSyntaxTree.ParseText("Write-Output a,b", ShellDialect.PowerShellCore);

        Assert.Single(tree.GetRoot().Statements.Statements);
        Assert.Empty(tree.GetDiagnostics());
    }

    // The checks below rebuild the text from the children rather than reading it off the root, which is the only way
    // to notice a node that dropped a character or points at the wrong span. See ShellSyntaxAssert.

    [Theory]
    // A prefix operator or an attribute list comes before the part these nodes used to take their position from.
    [InlineData("$a = -not $b")]
    [InlineData("if (-not (Test-Path $x)) { 1 }")]
    [InlineData("$c | Where-Object { -not $_ }")]
    [InlineData("function f { param([int]$a, [string]$b) }")]
    [InlineData("[CmdletBinding()]\nparam ($a)")]
    [InlineData("[CmdletBinding()]\nclass C { }")]
    // A `;` that follows a line-separated statement has to be rebuilt against the statement it belongs to.
    [InlineData("Get-Item\nGet-Date; Get-Host\n")]
    [InlineData("Get-Item\nGet-Date\nGet-Host; Get-Random\n")]
    // Malformed input still has to keep every character.
    [InlineData("@{;a=1}")]
    [InlineData("0x1Fclass::(|| $x[")]
    [InlineData("\0;b")]
    public void EveryCharacterStaysInTheTree(string text)
    {
        ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.PowerShellCore);
    }

    [Fact]
    public void UnaryExpressionSpanStartsAtItsOperator()
    {
        var tree = ShellSyntaxTree.ParseText("$a = -not $b", ShellDialect.PowerShellCore);
        var unary = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellUnaryExpressionSyntax>());

        Assert.Equal("-not $b", tree.GetText().Text[unary.Span.Start..unary.Span.End]);
    }

    [Fact]
    public void ParameterSpanStartsAtItsFirstAttribute()
    {
        var tree = ShellSyntaxTree.ParseText("function f { param([int]$a) }", ShellDialect.PowerShellCore);
        var parameter = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellParameterSyntax>());

        Assert.Equal("[int]$a", tree.GetText().Text[parameter.Span.Start..parameter.Span.End]);
    }

    [Fact]
    public void ParamBlockSpanStartsAtItsFirstAttribute()
    {
        var tree = ShellSyntaxTree.ParseText("[CmdletBinding()]\nparam ($a)", ShellDialect.PowerShellCore);
        var block = Assert.Single(tree.GetRoot().DescendantNodes().OfType<PowerShellParamBlockSyntax>());

        Assert.Equal("[CmdletBinding()]\nparam ($a)", tree.GetText().Text[block.Span.Start..block.Span.End]);
    }
}
