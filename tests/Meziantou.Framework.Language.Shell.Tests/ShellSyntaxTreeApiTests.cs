namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>Behaviour of the tree-level APIs: expression parsing, diffing, and structural comparison.</summary>
public sealed class ShellSyntaxTreeApiTests
{
    [Theory]
    [InlineData("1 + 2", typeof(PowerShellBinaryExpressionSyntax))]
    [InlineData("$a -eq $b", typeof(PowerShellBinaryExpressionSyntax))]
    [InlineData("$x.Length", typeof(PowerShellMemberAccessExpressionSyntax))]
    [InlineData("$x.Method(1)", typeof(PowerShellInvocationExpressionSyntax))]
    [InlineData("@{ a = 1 }", typeof(PowerShellHashLiteralSyntax))]
    [InlineData("@(1, 2)", typeof(PowerShellSubExpressionSyntax))]
    [InlineData("'text'", typeof(PowerShellLiteralExpressionSyntax))]
    [InlineData("[int]", typeof(PowerShellTypeLiteralSyntax))]
    [InlineData("$a ? 1 : 2", typeof(PowerShellTernaryExpressionSyntax))]
    public void ParseExpression_UsesThePowerShellGrammar(string text, Type expected)
    {
        var expression = ShellSyntaxTree.ParseExpression(text, ShellDialect.PowerShellCore);

        Assert.IsType(expected, expression);
        Assert.Equal(text, expression.ToFullString());
        Assert.NotNull(expression.SyntaxTree);
    }

    [Theory]
    [InlineData(ShellDialectFamily.Posix)]
    [InlineData(ShellDialectFamily.Cmd)]
    public void ParseExpression_KeepsTextVerbatimWhereThereIsNoExpressionGrammar(ShellDialectFamily family)
    {
        var dialect = family == ShellDialectFamily.Posix ? ShellDialect.Bash : ShellDialect.Cmd;

        var expression = Assert.IsType<ShellRawExpressionSyntax>(ShellSyntaxTree.ParseExpression("1 + 2", dialect));

        Assert.Equal("1 + 2", expression.Text);
    }

    [Fact]
    public void ParseExpression_ReportsTrailingContent()
    {
        var expression = ShellSyntaxTree.ParseExpression("1 + 2 extra", ShellDialect.PowerShellCore);

        Assert.Contains(expression.SyntaxTree!.Diagnostics, diagnostic => diagnostic.Id == "SHELL0101");
    }

    [Fact]
    public void ParseExpression_OnEmptyTextDoesNotThrow()
    {
        Assert.NotNull(ShellSyntaxTree.ParseExpression("", ShellDialect.PowerShellCore));
        Assert.NotNull(ShellSyntaxTree.ParseExpression("   ", ShellDialect.PowerShellCore));
    }

    [Fact]
    public void GetChanges_ReportsOnlyTheTextThatDiffers()
    {
        var original = ShellSyntaxTree.ParseText("echo one\necho two\necho three\n", ShellDialect.Bash);
        var updated = original.WithChanges(new ShellTextChange(new TextSpan(14, 3), "TWO"));

        var change = Assert.Single(updated.GetChanges(original));

        Assert.Equal(new TextSpan(14, 3), change.Span);
        Assert.Equal("TWO", change.NewText);
    }

    [Theory]
    [InlineData("echo a", "echo b", "b")]
    [InlineData("abc", "axc", "x")]
    [InlineData("same", "same tail", " tail")]
    [InlineData("head same", "same", "")]
    public void GetChanges_TrimsTheCommonPrefixAndSuffix(string oldText, string newText, string expectedInsert)
    {
        var oldTree = ShellSyntaxTree.ParseText(oldText, ShellDialect.Bash);
        var newTree = ShellSyntaxTree.ParseText(newText, ShellDialect.Bash);

        var change = Assert.Single(newTree.GetChanges(oldTree));

        Assert.Equal(expectedInsert, change.NewText);

        // Applying the change to the old text must reproduce the new text exactly.
        Assert.Equal(newText, SourceText.From(oldText).WithChanges([change]).Text);
    }

    [Fact]
    public void GetChanges_ReturnsNothingForIdenticalText()
    {
        var tree = ShellSyntaxTree.ParseText("echo a", ShellDialect.Bash);

        Assert.Empty(tree.GetChanges(ShellSyntaxTree.ParseText("echo a", ShellDialect.Bash)));
    }

    [Fact]
    public void GetChanges_DoesNotSplitASurrogatePair()
    {
        var oldTree = ShellSyntaxTree.ParseText("echo \U0001F600", ShellDialect.Bash);
        var newTree = ShellSyntaxTree.ParseText("echo \U0001F601", ShellDialect.Bash);

        var change = Assert.Single(newTree.GetChanges(oldTree));

        Assert.Equal("echo \U0001F601", SourceText.From("echo \U0001F600").WithChanges([change]).Text);
    }

    [Theory]
    [InlineData("echo   a", "echo a")]
    [InlineData("echo a # comment", "echo a")]
    [InlineData("if true; then x; fi", "if true;then x;fi")]
    [InlineData("echo a\n\n\n", "echo a\n")]
    [InlineData("# leading\necho a", "echo a")]
    public void IsEquivalentTo_IgnoresFormatting(string left, string right)
    {
        var a = ShellSyntaxTree.ParseText(left, ShellDialect.Bash);
        var b = ShellSyntaxTree.ParseText(right, ShellDialect.Bash);

        Assert.True(a.IsEquivalentTo(b));
        Assert.True(b.IsEquivalentTo(a));
    }

    [Theory]
    [InlineData("echo a", "echo b")]
    [InlineData("echo a", "echo a b")]
    [InlineData("echo a", "printf a")]
    [InlineData("echo 'a'", "echo a")]
    [InlineData("if x; then y; fi", "if x; then z; fi")]
    public void IsEquivalentTo_SeesRealDifferences(string left, string right)
    {
        var a = ShellSyntaxTree.ParseText(left, ShellDialect.Bash);
        var b = ShellSyntaxTree.ParseText(right, ShellDialect.Bash);

        Assert.False(a.IsEquivalentTo(b));
    }

    [Fact]
    public void IsEquivalentTo_NeverMatchesAcrossDialects()
    {
        var bash = ShellSyntaxTree.ParseText("echo a", ShellDialect.Bash);
        var zsh = ShellSyntaxTree.ParseText("echo a", ShellDialect.Zsh);

        Assert.False(bash.IsEquivalentTo(zsh));
        Assert.False(bash.IsEquivalentTo(null));
    }

    [Fact]
    public void NodesCanBeComparedStructurallyToo()
    {
        var a = ShellSyntaxTree.ParseText("echo   a", ShellDialect.Bash).Root.Statements.Statements[0];
        var b = ShellSyntaxTree.ParseText("echo a", ShellDialect.Bash).Root.Statements.Statements[0];

        Assert.True(a.IsEquivalentTo(b));
        Assert.True(a.IsEquivalentTo(a));
        Assert.False(a.IsEquivalentTo(null));
    }
}
