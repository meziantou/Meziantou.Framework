namespace Meziantou.Framework.Globbing.Tests;
public sealed class GlobCollectionTests
{
    [Fact]
    public void CanUseCollectionInitializer()
    {
        var a = Glob.Parse("a", GlobDialect.Standard);
        var b = Glob.Parse("b", GlobDialect.Standard);

        GlobCollection globs = [a, b];
        Assert.Collection(globs,
            item => Assert.Equal(a, item),
            item => Assert.Equal(b, item));
    }

    [Fact]
    public void LoadGitIgnore_ParsesPatterns()
    {
        var gitignore = """
# Comment
bin/
*.log
!important.log
\#literal
\!literal
""";

        var globs = GlobCollection.ParseGitIgnore(gitignore.AsSpan());

        Assert.True(globs.IsMatch("bin/test.txt"));
        Assert.True(globs.IsMatch("src/bin/test.txt"));
        Assert.True(globs.IsMatch("trace.log"));
        Assert.False(globs.IsMatch("important.log"));
        Assert.True(globs.IsMatch("#literal"));
        Assert.True(globs.IsMatch("!literal"));
    }

    [Fact]
    public void GitIgnoreEntryWithoutTrailingSlashMatchesADirectory()
    {
        var globs = GlobCollection.ParseGitIgnore("node_modules\n".AsSpan());

        Assert.True(globs.IsMatch("", "node_modules", PathItemType.Directory));
        Assert.True(globs.IsMatch("src", "node_modules", PathItemType.Directory));
        Assert.True(globs.IsMatch("", "node_modules", PathItemType.File));
        Assert.True(((IGlobEvaluatable)globs).CanMatchDirectories);
    }

    [Fact]
    public void GitIgnoreEntryWithTrailingSlashDoesNotMatchAFile()
    {
        var globs = GlobCollection.ParseGitIgnore("bin/\n".AsSpan());

        Assert.True(globs.IsMatch("bin/test.txt"));
        Assert.False(globs.IsMatch("", "bin", PathItemType.File));
    }

    [Fact]
    public void GitIgnoreResolvesAPathAgainstTheLastMatchingPattern()
    {
        var globs = GlobCollection.ParseGitIgnore("""
*.log
!important.log
important.log
""".AsSpan());

        Assert.True(globs.IsMatch("important.log"));
        Assert.True(globs.IsMatch("trace.log"));
    }

    [Fact]
    public void GitIgnoreNegationAfterAMatchReIncludesThePath()
    {
        var globs = GlobCollection.ParseGitIgnore("""
*.log
!important.log
""".AsSpan());

        Assert.False(globs.IsMatch("important.log"));
        Assert.True(globs.IsMatch("trace.log"));
    }

    [Fact]
    public void GitIgnoreOrderMatters()
    {
        var globs = GlobCollection.ParseGitIgnore("""
!important.log
*.log
""".AsSpan());

        Assert.True(globs.IsMatch("important.log"));
    }

    [Fact]
    public void HandBuiltCollectionKeepsAnyExcludeWins()
    {
        GlobCollection globs = [
            Glob.Parse("!important.log", GlobDialect.Standard),
            Glob.Parse("*.log", GlobDialect.Standard),
        ];

        Assert.False(globs.IsMatch("important.log"));
        Assert.True(globs.IsMatch("trace.log"));
    }

    [Theory]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public async Task ParseGitIgnoreAndLoadGitIgnoreAgreeOnLineBreaks(string character)
    {
        var content = "a" + character + "b.txt";

        var parsed = GlobCollection.ParseGitIgnore(content.AsSpan());
        var loaded = await GlobCollection.LoadGitIgnoreAsync(new StringReader(content));

        Assert.Equal(loaded.Count, parsed.Count);
        Assert.Equal(1, parsed.Count);
        Assert.True(parsed.IsMatch(content));
        Assert.True(loaded.IsMatch(content));
    }

    [Fact]
    public async Task ParseGitIgnoreAndLoadGitIgnoreAgreeOnCarriageReturns()
    {
        var content = "*.log\r\n!important.log\r\n";

        var parsed = GlobCollection.ParseGitIgnore(content.AsSpan());
        var loaded = await GlobCollection.LoadGitIgnoreAsync(new StringReader(content));

        Assert.Equal(loaded.Count, parsed.Count);
        Assert.Equal(2, parsed.Count);
    }
}
