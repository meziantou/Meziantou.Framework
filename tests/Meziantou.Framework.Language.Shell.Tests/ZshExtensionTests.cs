namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>
/// The zsh-only grammar behind <see cref="ShellDialectFeatures.ZshExtensions"/>. Every expectation here was checked
/// against zsh 5.9 with <c>zsh -n</c>, and each construct is also asserted to stay a plain command under
/// <see cref="ShellDialect.Bash"/>, which rejects all of them.
/// </summary>
public sealed class ZshExtensionTests
{
    private static ShellStatementSyntax SingleStatement(string text)
    {
        var tree = ShellSyntaxAssert.TextIsFaithful(text, ShellDialect.Zsh);

        Assert.Empty(tree.Diagnostics);

        return Assert.Single(tree.Root.Statements.Statements);
    }

    [Theory]
    [InlineData("{ echo a }\n")]
    [InlineData("{ echo a}\n")]
    [InlineData("{ { echo a } }\n")]
    [InlineData("{ echo ${x} }\n")]
    public void BraceGroup_ClosesWithoutASeparator(string text)
    {
        Assert.Equal(ShellSyntaxKind.PosixGroup, SingleStatement(text).Kind);
    }

    [Fact]
    public void BraceGroup_StillNeedsASeparatorInBash()
    {
        // bash requires `{ echo a; }`; without the separator the `}` is just another argument.
        var tree = ShellSyntaxTree.ParseText("{ echo a }\n", ShellDialect.Bash);

        Assert.NotEmpty(tree.Diagnostics);
    }

    [Theory]
    [InlineData("() { echo hi }\n")]
    [InlineData("() echo hi\n")]
    [InlineData("(){ echo hi }\n")]
    public void AnonymousFunction_IsAFunctionDefinitionWithNoName(string text)
    {
        var definition = Assert.IsType<PosixFunctionDefinitionSyntax>(SingleStatement(text));

        Assert.True(definition.IsAnonymous);
        Assert.Empty(definition.Name);
    }

    [Fact]
    public void NamedFunction_IsNotAnonymous()
    {
        var definition = Assert.IsType<PosixFunctionDefinitionSyntax>(SingleStatement("greet() { echo hi }\n"));

        Assert.False(definition.IsAnonymous);
        Assert.Equal("greet", definition.Name);
    }

    [Theory]
    [InlineData("foreach f (a b)\necho $f\nend\n", "f", 2, true)]
    [InlineData("foreach f (a b) { echo }\n", "f", 2, false)]
    [InlineData("for f (a b) echo $f\n", "f", 2, false)]
    [InlineData("for f (a b) { echo }\n", "f", 2, false)]
    [InlineData("foreach item ($LIST)\necho\nend\n", "item", 1, true)]
    public void ParenthesizedLoops_ExposeVariableAndItems(string text, string variable, int itemCount, bool hasEnd)
    {
        var statement = Assert.IsType<ZshForeachStatementSyntax>(SingleStatement(text));

        Assert.Equal(variable, statement.VariableName);
        Assert.HasCount(itemCount, statement.Items);
        Assert.Equal(hasEnd, statement.EndKeyword is not null);
        Assert.NotEmpty(statement.Body.Statements);
    }

    [Theory]
    [InlineData("repeat 3 echo hi\n", false)]
    [InlineData("repeat 3 { echo }\n", false)]
    [InlineData("repeat 3 do echo; done\n", true)]
    [InlineData("repeat 3; do echo; done\n", true)]
    public void RepeatLoop_ExposesCountAndBody(string text, bool hasDoDone)
    {
        var statement = Assert.IsType<ZshRepeatStatementSyntax>(SingleStatement(text));

        Assert.Equal("3", statement.Count.Value);
        Assert.Equal(hasDoDone, statement.DoKeyword is not null);
        Assert.Equal(hasDoDone, statement.DoneKeyword is not null);
        Assert.NotEmpty(statement.Body.Statements);
    }

    [Fact]
    public void AlwaysBlock_WrapsTheGroupItFollows()
    {
        var statement = Assert.IsType<ZshAlwaysStatementSyntax>(SingleStatement("{ echo a } always { echo b }\n"));

        Assert.Equal(ShellSyntaxKind.PosixGroup, statement.Body.Kind);
        Assert.Equal("always", statement.AlwaysKeyword.Text);
        Assert.Equal(ShellSyntaxKind.PosixGroup, statement.AlwaysBody.Kind);
    }

    [Fact]
    public void AlwaysWithoutAPrecedingGroup_IsAnOrdinaryCommand()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("always foo", ShellDialect.Zsh));

        Assert.Equal("always", command.NameValue);
    }

    [Fact]
    public void FileSubstitution_IsProcessSubstitutionWithATemporaryFile()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("diff =(sort a) =(sort b)", ShellDialect.Zsh));
        var substitutions = command.DescendantNodes().OfType<PosixProcessSubstitutionSyntax>().ToArray();

        Assert.HasCount(2, substitutions);
        Assert.All(substitutions, node => Assert.True(node.IsFileSubstitution));
        Assert.All(substitutions, node => Assert.False(node.IsInput));
        Assert.Equal("sort", Assert.IsType<ShellCommandSyntax>(substitutions[0].Statements.Statements[0]).NameValue);
    }

    [Theory]
    [InlineData("*(.)", "*(.)")]
    [InlineData("*(N)", "*(N)")]
    [InlineData("*(.om[1])", "*(.om[1])")]
    [InlineData("foo*(N)", "*(N)")]
    public void GlobQualifiers_ArePartOfTheGlob(string argument, string expectedGlobText)
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("ls " + argument, ShellDialect.Zsh));
        var glob = Assert.Single(command.Arguments[0].Parts.OfType<ShellGlobSyntax>());

        Assert.Equal(expectedGlobText, glob.GlobToken.Text);
        Assert.True(glob.HasQualifier);
        Assert.Equal(argument, command.Arguments[0].Value);
    }

    [Fact]
    public void GlobWithoutAQualifier_IsUnchanged()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("ls *.txt", ShellDialect.Zsh));
        var glob = Assert.Single(command.Arguments[0].Parts.OfType<ShellGlobSyntax>());

        Assert.False(glob.HasQualifier);
        Assert.Equal("*", glob.GlobToken.Text);
    }

    [Theory]
    // Everything above is zsh-only: under bash the same text is a plain command, or a parenthesized group.
    [InlineData("foreach f (a b)\necho\nend\n")]
    [InlineData("for f (a b) echo $f\n")]
    [InlineData("repeat 3 echo hi\n")]
    [InlineData("() { echo hi }\n")]
    [InlineData("diff =(sort a)\n")]
    [InlineData("ls *(.)\n")]
    public void BashDoesNotGetTheZshGrammar(string text)
    {
        var tree = ShellSyntaxTree.ParseText(text, ShellDialect.Bash);

        ShellSyntaxAssert.TextIsFaithful(text, tree);
        Assert.DoesNotContain(tree.Root.DescendantNodes(), node =>
            node.Kind is ShellSyntaxKind.ZshForeachStatement or ShellSyntaxKind.ZshRepeatStatement or ShellSyntaxKind.ZshAlwaysStatement);
    }

    [Fact]
    public void ShHasNoZshExtensionsEither()
    {
        Assert.False(ShellDialect.Sh.HasFeature(ShellDialectFeatures.ZshExtensions));
        Assert.False(ShellDialect.Bash.HasFeature(ShellDialectFeatures.ZshExtensions));
        Assert.True(ShellDialect.Zsh.HasFeature(ShellDialectFeatures.ZshExtensions));
    }
}
