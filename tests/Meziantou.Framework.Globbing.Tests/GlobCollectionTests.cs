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
    public void GitIgnoreEntryWithTrailingSlashMatchesTheDirectoryItself()
    {
        var globs = GlobCollection.ParseGitIgnore("bin/\n".AsSpan());

        Assert.True(globs.IsMatch("", "bin", PathItemType.Directory));
        Assert.True(globs.IsMatch("src", "bin", PathItemType.Directory));
        Assert.True(globs.IsMatch("src/a", "bin", PathItemType.Directory));
        Assert.True(globs.IsMatch("bin", "test.txt", PathItemType.File));
        Assert.True(globs.IsMatch("bin/nested", "test.txt", PathItemType.File));

        Assert.False(globs.IsMatch("", "bin", PathItemType.File));
        Assert.False(globs.IsMatch("", "binary", PathItemType.Directory));
        Assert.True(((IGlobEvaluatable)globs).CanMatchDirectories);
    }

    [Fact]
    public void GitIgnoreExcludesTheContentOfAnExcludedDirectory()
    {
        // 'node_modules' matches the directory, and everything below an excluded directory is excluded too.
        var globs = GlobCollection.ParseGitIgnore("node_modules\n".AsSpan());

        Assert.True(globs.IsMatch("", "node_modules", PathItemType.Directory));
        Assert.True(globs.IsMatch("node_modules", "package.json", PathItemType.File));
        Assert.True(globs.IsMatch("node_modules/a/b", "package.json", PathItemType.File));
        Assert.True(globs.IsMatch("node_modules/package.json"));
        Assert.False(globs.IsMatch("src", "package.json", PathItemType.File));
    }

    [Fact]
    public void GitIgnoreCannotReIncludeAPathUnderAnExcludedDirectory()
    {
        var globs = GlobCollection.ParseGitIgnore("""
bin/
!bin/keep.txt
""".AsSpan());

        Assert.True(globs.IsMatch("bin", "keep.txt", PathItemType.File));
        Assert.True(globs.IsMatch("bin", "other.txt", PathItemType.File));
    }

    [Fact]
    public void GitIgnoreAppliesTheChildRulesWhenTheParentIsReIncluded()
    {
        var globs = GlobCollection.ParseGitIgnore("""
bin/
!bin/
""".AsSpan());

        Assert.False(globs.IsMatch("", "bin", PathItemType.Directory));
        Assert.False(globs.IsMatch("bin", "keep.txt", PathItemType.File));
    }

    [Fact]
    public void GitIgnoreResolvesEachAncestorAgainstTheLastMatchingPattern()
    {
        var globs = GlobCollection.ParseGitIgnore("""
/*
!/bin/
bin/keep.txt
""".AsSpan());

        Assert.False(globs.IsMatch("", "bin", PathItemType.Directory));
        Assert.True(globs.IsMatch("bin", "keep.txt", PathItemType.File));
        Assert.False(globs.IsMatch("bin", "other.txt", PathItemType.File));
    }

    [Fact]
    public void GitIgnoreNegatedDirectoryEntryDoesNotReIncludeTheDirectoryContent()
    {
        // git re-includes the directory so that it is walked into, but not the files it holds.
        var globs = GlobCollection.ParseGitIgnore("""
*
!*/
""".AsSpan());

        Assert.False(globs.IsMatch("", "bin", PathItemType.Directory));
        Assert.False(globs.IsMatch("src", "bin", PathItemType.Directory));
        Assert.True(globs.IsMatch("bin", "keep.txt", PathItemType.File));
        Assert.True(globs.IsMatch("src/bin", "f.txt", PathItemType.File));
    }

    [Fact]
    public void GitIgnoreDirectoryEntryMatchingEveryDirectory()
    {
        // The recursive wildcard has to consume the whole path before the directory entry matches what is left.
        var globs = GlobCollection.ParseGitIgnore("**/\n".AsSpan());

        Assert.True(globs.IsMatch("", "bin", PathItemType.Directory));
        Assert.True(globs.IsMatch("src/a", "bin", PathItemType.Directory));
        Assert.True(globs.IsMatch("src/a", "b.txt", PathItemType.File)); // below an excluded directory
        Assert.False(globs.IsMatch("", "b.txt", PathItemType.File));
    }

    [Fact]
    public void GitIgnoreTrimsTrailingSpacesButNotTrailingTabs()
    {
        Assert.True(GlobCollection.ParseGitIgnore("foo\t".AsSpan()).IsMatch("foo\t"));
        Assert.False(GlobCollection.ParseGitIgnore("foo\t".AsSpan()).IsMatch("foo"));

        Assert.True(GlobCollection.ParseGitIgnore("foo  ".AsSpan()).IsMatch("foo"));
        Assert.False(GlobCollection.ParseGitIgnore("foo  ".AsSpan()).IsMatch("foo  "));

        // An escaped space is kept, and so is a tab that precedes trimmed spaces.
        Assert.True(GlobCollection.ParseGitIgnore("foo\\ ".AsSpan()).IsMatch("foo "));
        Assert.True(GlobCollection.ParseGitIgnore("foo\t  ".AsSpan()).IsMatch("foo\t"));
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
        Assert.True(parsed.IsMatch("a.log"));
        Assert.False(parsed.IsMatch("important.log"));
    }

    [Fact]
    public async Task GitIgnoreLoneCarriageReturnBelongsToTheEntry()
    {
        // git only breaks lines on LF, so a CR that is not followed by a LF is part of the entry
        var content = "y\rz\n";

        var parsed = GlobCollection.ParseGitIgnore(content.AsSpan());
        var loaded = await GlobCollection.LoadGitIgnoreAsync(new StringReader(content));

        foreach (var globs in new[] { parsed, loaded })
        {
            Assert.Equal(1, globs.Count);
            Assert.True(globs.IsMatch("y\rz"));
            Assert.False(globs.IsMatch("y"));
            Assert.False(globs.IsMatch("z"));
        }
    }

    [Fact]
    public async Task GitIgnoreSkipsTheByteOrderMark()
    {
        var parsed = GlobCollection.ParseGitIgnore("\uFEFFy\n".AsSpan());
        var loaded = await GlobCollection.LoadGitIgnoreAsync(new StringReader("\uFEFFy\n"));
        using var stream = new MemoryStream([0xEF, 0xBB, 0xBF, (byte)'y', (byte)'\n']);
        var loadedFromStream = await GlobCollection.LoadGitIgnoreAsync(stream);

        Assert.True(parsed.IsMatch("y"));
        Assert.True(loaded.IsMatch("y"));
        Assert.True(loadedFromStream.IsMatch("y"));
    }

    [Fact]
    public void GitIgnoreSkipsTheEntriesThatNeverMatch()
    {
        // git accepts any entry. One that is not a valid pattern never matches, so it has no effect instead of making
        // the whole file invalid.
        var globs = GlobCollection.ParseGitIgnore("""
[a
*.log
y\
[[:foo:]]
//x
a//b
!
/
!/
""".AsSpan());

        Assert.Equal(1, globs.Count);
        Assert.True(globs.IsMatch("x.log"));
        Assert.False(globs.IsMatch("[a"));
        Assert.False(globs.IsMatch("y"));
        Assert.False(globs.IsMatch("x"));
    }

    [Fact]
    public void GitIgnoreNegationWithoutAPatternHasNoEffect()
    {
        var globs = GlobCollection.ParseGitIgnore("""
*.log
!
y
/
""".AsSpan());

        Assert.True(globs.IsMatch("a.log"));
        Assert.True(globs.IsMatch("y"));
        Assert.False(globs.IsMatch("z"));
        Assert.False(globs.IsMatch("", "z", PathItemType.Directory));
    }

    [Fact]
    public void GitIgnoreDirectoryEntryEndingWithARecursiveWildcard()
    {
        // '*/**/' matches the directories inside a top-level directory, but not the top-level directory itself
        var globs = GlobCollection.ParseGitIgnore("*/**/\n".AsSpan());

        Assert.False(globs.IsMatch("", "a", PathItemType.Directory));
        Assert.True(globs.IsMatch("a", "b", PathItemType.Directory));
        Assert.False(globs.IsMatch("a", "d", PathItemType.File));
        Assert.True(globs.IsMatch("a/b", "x", PathItemType.File)); // below an excluded directory
    }

    [Fact]
    public void GitIgnoreCharacterClassesAndCaretNegation()
    {
        var globs = GlobCollection.ParseGitIgnore("""
*[[:digit:]]
![^0]
""".AsSpan());

        Assert.True(globs.IsMatch("a0"));
        Assert.False(globs.IsMatch("5"));
        Assert.False(globs.IsMatch("a"));
    }
}
