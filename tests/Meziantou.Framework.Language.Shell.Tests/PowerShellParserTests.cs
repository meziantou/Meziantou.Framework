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
            Assert.Equal(text, ShellSyntaxTree.ParseText(text, dialect).Root.ToFullString());
        }
    }

    [Theory]
    [MemberData(nameof(CoreOnlySamples))]
    public void ParseText_RoundTripsCoreOnlySyntaxInBothDialects(string text)
    {
        foreach (var dialect in Dialects)
        {
            Assert.Equal(text, ShellSyntaxTree.ParseText(text, dialect).Root.ToFullString());
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
        var variable = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());

        Assert.Equal("env:PATH", variable.Name);
        Assert.False(variable.IsSplatted);
    }

    [Fact]
    public void SplattedVariable_IsDetected()
    {
        var tree = ShellSyntaxTree.ParseText("Get-Thing @params", ShellDialect.PowerShellCore);
        var variable = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellVariableExpressionSyntax>());

        Assert.True(variable.IsSplatted);
    }

    [Fact]
    public void ExpandableString_KeepsEmbeddedExpansions()
    {
        var tree = ShellSyntaxTree.ParseText("$m = \"name is $($user.Name) ok\"", ShellDialect.PowerShellCore);
        var text = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellExpandableStringSyntax>());

        Assert.Contains(text.Parts, part => part is PowerShellSubExpressionSyntax);
        Assert.Equal(ShellSyntaxKind.PowerShellExpandableString, text.Kind);
    }

    [Fact]
    public void VerbatimString_ResolvesDoubledQuotes()
    {
        var tree = ShellSyntaxTree.ParseText("$m = 'it''s'", ShellDialect.PowerShellCore);
        var literal = tree.Root.DescendantNodes().OfType<PowerShellLiteralExpressionSyntax>()
            .Single(node => node.Kind == ShellSyntaxKind.PowerShellStringLiteral);

        Assert.Equal("it's", literal.Value);
    }

    [Fact]
    public void HashLiteral_ExposesEntries()
    {
        var tree = ShellSyntaxTree.ParseText("$h = @{ a = 1; b = 'two' }", ShellDialect.PowerShellCore);
        var hash = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellHashLiteralSyntax>());

        Assert.Equal(2, hash.Entries.Count);
    }

    [Fact]
    public void TypeLiteralAndCast_AreDistinguished()
    {
        var castTree = ShellSyntaxTree.ParseText("$x = [int]'42'", ShellDialect.PowerShellCore);
        var cast = Assert.Single(castTree.Root.DescendantNodes().OfType<PowerShellCastExpressionSyntax>());
        Assert.Equal("int", cast.Type.Name);

        var staticTree = ShellSyntaxTree.ParseText("[System.IO.Path]::GetFileName($p)", ShellDialect.PowerShellCore);
        var access = Assert.Single(staticTree.Root.DescendantNodes().OfType<PowerShellMemberAccessExpressionSyntax>());
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
        Assert.Equal(ShellSyntaxKind.PowerShellClassDefinition, ShellSyntaxTree.ParseCommand("class A { }", ShellDialect.PowerShellCore).Kind);
        Assert.Equal(ShellSyntaxKind.PowerShellEnumDefinition, ShellSyntaxTree.ParseCommand("enum A { }", ShellDialect.PowerShellCore).Kind);

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
        Assert.Single(core.Root.DescendantNodes().OfType<PowerShellTernaryExpressionSyntax>());

        var windows = ShellSyntaxTree.ParseText("$x = $a ? 1 : 2", ShellDialect.PowerShell);
        Assert.Empty(windows.Root.DescendantNodes().OfType<PowerShellTernaryExpressionSyntax>());
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

        var comments = tree.Root.DescendantTrivia()
            .Where(trivia => trivia.Kind is ShellSyntaxKind.SingleLineCommentTrivia or ShellSyntaxKind.MultiLineCommentTrivia)
            .ToArray();

        Assert.HasCount(2, comments);
        Assert.Equal("# line", comments[0].Text);
        Assert.Equal("<# block #>", comments[1].Text);
    }

    [Fact]
    public void HereStrings_KeepTheirBodyVerbatim()
    {
        var tree = ShellSyntaxTree.ParseText("$a = @\"\nline $x\n\"@\n", ShellDialect.PowerShellCore);
        var hereString = Assert.Single(tree.Root.DescendantNodes().OfType<PowerShellExpandableStringSyntax>());

        Assert.Equal(ShellSyntaxKind.PowerShellHereString, hereString.Kind);
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

        Assert.NotEmpty(tree.Diagnostics);
        Assert.Equal("if ($a) {", tree.Root.ToFullString());
    }
}
