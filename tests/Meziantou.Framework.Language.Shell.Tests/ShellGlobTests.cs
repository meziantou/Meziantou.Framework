namespace Meziantou.Framework.Language.Shell.Tests;

/// <summary>Pathname-expansion metacharacters, which must be exposed as globs but only where they are unquoted.</summary>
public sealed class ShellGlobTests
{
    public static TheoryData<ShellDialect> PosixDialects => [ShellDialect.Sh, ShellDialect.Bash, ShellDialect.Zsh];

    private static ShellWordSyntax FirstArgument(string argumentText, ShellDialect dialect)
    {
        var statement = ShellSyntaxTree.ParseCommand("ls " + argumentText, dialect);

        return Assert.IsType<ShellCommandSyntax>(statement).Arguments[0];
    }

    [Theory]
    [MemberData(nameof(PosixDialects))]
    public void SimpleGlobs_AreExposed(ShellDialect dialect)
    {
        Assert.Equal(ShellSyntaxKind.AsteriskToken, Assert.Single(FirstArgument("*", dialect).Parts.OfType<ShellGlobSyntax>()).GlobToken.Kind);
        Assert.Equal(ShellSyntaxKind.QuestionToken, Assert.Single(FirstArgument("?", dialect).Parts.OfType<ShellGlobSyntax>()).GlobToken.Kind);
    }

    [Theory]
    [MemberData(nameof(PosixDialects))]
    public void GlobInsideALongerWord_IsExposed(ShellDialect dialect)
    {
        var word = FirstArgument("local:*", dialect);

        Assert.Equal("local:*", word.Value);
        Assert.Equal(ShellSyntaxKind.AsteriskToken, Assert.Single(word.Parts.OfType<ShellGlobSyntax>()).GlobToken.Kind);
        Assert.Equal("local:", Assert.IsType<ShellLiteralWordPartSyntax>(word.Parts[0]).Value);
    }

    [Theory]
    [MemberData(nameof(PosixDialects))]
    public void RecursiveGlob_IsOneNode(ShellDialect dialect)
    {
        var word = FirstArgument("**/*.cs", dialect);
        var globs = word.Parts.OfType<ShellGlobSyntax>().ToArray();

        Assert.HasCount(2, globs);
        Assert.True(globs[0].IsRecursive);
        Assert.Equal("**", globs[0].GlobToken.Text);
        Assert.False(globs[1].IsRecursive);
        Assert.Equal("**/*.cs", word.Value);
    }

    [Theory]
    [MemberData(nameof(PosixDialects))]
    public void BracketExpressions_AreGlobs(ShellDialect dialect)
    {
        foreach (var (text, expected) in new[] { ("[abc].txt", "[abc]"), ("[!a-z]*", "[!a-z]"), ("[^0-9]", "[^0-9]"), ("[]a]", "[]a]") })
        {
            var glob = Assert.Single(FirstArgument(text, dialect).Parts.OfType<ShellGlobSyntax>(), node => node.IsBracketExpression);

            Assert.Equal(expected, glob.GlobToken.Text);
        }
    }

    [Fact]
    public void TestCommandBracket_IsNotAGlob()
    {
        var command = Assert.IsType<ShellCommandSyntax>(ShellSyntaxTree.ParseCommand("[ -f file ]", ShellDialect.Bash));

        Assert.Equal("[", command.NameValue);
        Assert.Empty(command.DescendantNodes().OfType<ShellGlobSyntax>());
    }

    [Fact]
    public void UnclosedBracket_IsLiteralText()
    {
        var word = FirstArgument("[unclosed", ShellDialect.Bash);

        Assert.Equal("[unclosed", word.Value);
        Assert.Empty(word.Parts.OfType<ShellGlobSyntax>());
    }

    [Theory]
    [MemberData(nameof(PosixDialects))]
    public void QuotingSuppressesGlobs(ShellDialect dialect)
    {
        foreach (var text in new[] { "'*'", "\"*\"", "\"[abc]\"", @"\*" })
        {
            Assert.Empty(FirstArgument(text, dialect).Parts.OfType<ShellGlobSyntax>());
        }
    }

    [Fact]
    public void CmdExposesGlobsToo()
    {
        var word = FirstArgument("*.txt", ShellDialect.Cmd);

        Assert.Single(word.Parts.OfType<ShellGlobSyntax>());
        Assert.Equal("*.txt", word.Value);
    }

    [Theory]
    [MemberData(nameof(PosixDialects))]
    public void GlobsRoundTripInsideCommands(ShellDialect dialect)
    {
        foreach (var text in new[] { "ls *", "ls local:*", "ls **/*.cs", "ls [abc]?.txt", "ls '*'", "ls a[0-9]b" })
        {
            Assert.Equal(text, ShellSyntaxTree.ParseText(text, dialect).Root.ToFullString());
        }
    }
}
