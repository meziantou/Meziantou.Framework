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
}
