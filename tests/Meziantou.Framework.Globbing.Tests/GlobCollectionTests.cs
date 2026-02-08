namespace Meziantou.Framework.Globbing.Tests;
public sealed class GlobCollectionTests
{
    [Fact]
    public void CanUseCollectionInitializer()
    {
        var a = Glob.Parse("a", GlobOptions.None);
        var b = Glob.Parse("b", GlobOptions.None);

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
}
