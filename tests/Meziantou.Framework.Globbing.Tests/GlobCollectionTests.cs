using System.IO.Enumeration;

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
    public void DocumentationExample_IncludeExclude()
    {
        #region GlobCollection_IncludeExclude
        GlobCollection globs =
        [
            Glob.Parse("**/*.cs", GlobDialect.Standard),
            Glob.Parse("!**/obj/**", GlobDialect.Standard),
        ];

        Assert.True(globs.IsMatch("src/Program.cs"));
        Assert.False(globs.IsMatch("src/obj/Debug/AssemblyInfo.cs"));
        Assert.False(globs.IsMatch("readme.md"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_ExcludeWins()
    {
        #region GlobCollection_ExcludeWins
        // The order does not matter: a path that matches an exclude pattern is excluded
        GlobCollection globs =
        [
            Glob.Parse("!*.log", GlobDialect.Standard),
            Glob.Parse("*", GlobDialect.Standard),
            Glob.Parse("important.log", GlobDialect.Standard),
        ];

        Assert.True(globs.IsMatch("readme.md"));
        Assert.False(globs.IsMatch("important.log"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_ExcludeDirectoryContent()
    {
        #region GlobCollection_ExcludeDirectoryContent
        // '!obj/' excludes the directory itself, not the files it holds
        GlobCollection directoryOnly = [Glob.Parse("**/*", GlobDialect.Standard), Glob.Parse("!obj/", GlobDialect.Standard)];
        Assert.True(directoryOnly.IsMatch("obj/project.assets.json"));

        // '!obj/**' excludes everything below the directory
        GlobCollection content = [Glob.Parse("**/*", GlobDialect.Standard), Glob.Parse("!obj/**", GlobDialect.Standard)];
        Assert.False(content.IsMatch("obj/project.assets.json"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_EnumerateFiles()
    {
        #region GlobCollection_EnumerateFilesWithExclusions
        using var rootDirectory = TemporaryDirectory.Create();
        rootDirectory.CreateEmptyFile("src/App/Program.cs");
        rootDirectory.CreateEmptyFile("src/App/obj/Debug/AssemblyInfo.cs");
        rootDirectory.CreateEmptyFile("tests/AppTests.cs");

        GlobCollection globs =
        [
            Glob.Parse("src/**/*.cs", GlobDialect.Standard),
            Glob.Parse("!**/obj/**", GlobDialect.Standard),
        ];

        var files = globs.EnumerateFiles(rootDirectory.FullPath).ToList();
        Assert.Equal([Path.Combine(rootDirectory.FullPath, "src", "App", "Program.cs")], files);
        #endregion
    }

    [Fact]
    public void DocumentationExample_ParseGitIgnore()
    {
        #region GlobCollection_ParseGitIgnore
        var gitIgnore = GlobCollection.ParseGitIgnore("""
            # Build output
            bin/
            obj/
            *.log
            !important.log
            """);

        // IsMatch returns true when the path is ignored
        Assert.True(gitIgnore.IsMatch("src/App/bin/Debug/App.dll"));
        Assert.True(gitIgnore.IsMatch("trace.log"));
        Assert.False(gitIgnore.IsMatch("important.log"));
        Assert.False(gitIgnore.IsMatch("src/App/Program.cs"));
        #endregion
    }

    [Fact]
    public async Task DocumentationExample_LoadGitIgnore()
    {
        #region GlobCollection_LoadGitIgnoreAsync
        using var rootDirectory = TemporaryDirectory.Create();
        var path = rootDirectory.CreateTextFile(".gitignore", "bin/\nobj/\n");

        var gitIgnore = await GlobCollection.LoadGitIgnoreAsync(path);

        Assert.True(gitIgnore.IsMatch("src/App/obj/project.assets.json"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_GitIgnoreRules()
    {
        #region GlobCollection_GitIgnoreRules
        // The last pattern that matches a path decides
        var lastMatchWins = GlobCollection.ParseGitIgnore("""
            *.log
            !important.log
            """);
        Assert.False(lastMatchWins.IsMatch("important.log"));

        // A file cannot be re-included when its directory is ignored
        var directoryIgnored = GlobCollection.ParseGitIgnore("""
            bin/
            !bin/keep.txt
            """);
        Assert.True(directoryIgnored.IsMatch("bin/keep.txt"));

        // Ignore the content of the directory instead of the directory itself
        var contentIgnored = GlobCollection.ParseGitIgnore("""
            /bin/*
            !/bin/keep.txt
            """);
        Assert.False(contentIgnored.IsMatch("bin/keep.txt"));
        Assert.True(contentIgnored.IsMatch("bin/App.dll"));
        #endregion
    }

    [Fact]
    public void DocumentationExample_EnumerateFilesNotIgnored()
    {
        #region GlobCollection_EnumerateFilesNotIgnored
        using var rootDirectory = TemporaryDirectory.Create();
        rootDirectory.CreateEmptyFile("src/Program.cs");
        rootDirectory.CreateEmptyFile("src/bin/App.dll");
        rootDirectory.CreateEmptyFile("trace.log");

        var gitIgnore = GlobCollection.ParseGitIgnore("""
            bin/
            *.log
            """);

        // Skip the ignored files, and do not visit the ignored directories
        var files = new FileSystemEnumerable<string>(
            rootDirectory.FullPath,
            (ref FileSystemEntry entry) => entry.ToFullPath(),
            new EnumerationOptions { RecurseSubdirectories = true })
        {
            ShouldIncludePredicate = (ref FileSystemEntry entry) => !entry.IsDirectory && !gitIgnore.IsMatch(ref entry),
            ShouldRecursePredicate = (ref FileSystemEntry entry) => !gitIgnore.IsMatch(ref entry),
        };

        Assert.Equal([Path.Combine(rootDirectory.FullPath, "src", "Program.cs")], files.ToList());
        #endregion
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

    [Theory]
    [InlineData("utf-8")]
    [InlineData("utf-16")]
    [InlineData("utf-16BE")]
    [InlineData("utf-32")]
    public async Task GitIgnoreStreamSkipsOnlyTheFirstByteOrderMark(string encodingName)
    {
        // git skips one byte order mark, so a second one belongs to the first entry
        var encoding = System.Text.Encoding.GetEncoding(encodingName);
        using var stream = new MemoryStream([.. encoding.GetPreamble(), .. encoding.GetBytes("\uFEFFy\n")]);
        var loaded = await GlobCollection.LoadGitIgnoreAsync(stream);
        var parsed = GlobCollection.ParseGitIgnore("\uFEFF\uFEFFy\n".AsSpan());

        Assert.Equal(1, loaded.Count);
        Assert.Equal(parsed[0].ToString(), loaded[0].ToString());
        Assert.True(loaded.IsMatch("\uFEFFy"));
        Assert.False(loaded.IsMatch("y"));
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

    [Fact]
    public void DocumentationExample_ExcludedDirectoriesAreNotVisited()
    {
        #region GlobCollection_ExcludedDirectoriesAreNotVisited
        GlobCollection globs =
        [
            Glob.Parse("**/*", GlobDialect.Standard),
            Glob.Parse("!**/node_modules/**", GlobDialect.Standard),
        ];

        // The exclude pattern matches the whole content of node_modules: the enumeration does not go into it
        Assert.False(globs.IsPartialMatch("src/node_modules"));
        Assert.True(globs.IsPartialMatch("src/app"));
        #endregion
    }

    [Theory]
    [InlineData("!**/node_modules/**", "node_modules", false)]
    [InlineData("!**/node_modules/**", "a/b/node_modules", false)]
    [InlineData("!**/node_modules/**", "a/node_modules/b", false)]
    [InlineData("!**/node_modules/**", "node_modules2", true)]
    [InlineData("!**/node_modules/*", "node_modules", true)] // only the direct children are excluded
    [InlineData("!node_modules/**", "node_modules", false)]
    [InlineData("!node_modules/**", "a/node_modules", true)] // anchored to the root
    [InlineData("!**/obj/**/*", "src/obj", false)]
    [InlineData("!**/*.txt", "src", true)] // only some files are excluded
    [InlineData("!**/obj/", "obj", true)] // the directory itself, not its content
    [InlineData("!**", "src", false)]
    public void IsPartialMatch_PrunesDirectoriesWhoseContentIsExcluded(string exclude, string directory, bool expected)
    {
        GlobCollection globs = [Glob.Parse("**/*", GlobDialect.Standard), Glob.Parse(exclude, GlobDialect.Standard)];
        Assert.Equal(expected, globs.IsPartialMatch(directory));
    }

    [Fact]
    public void IsPartialMatch_ExcludeThatSkipsHiddenSegmentsOnlyPrunesWhenIncludesSkipThemToo()
    {
        // '**' does not match '.bin' in the standard dialect, so 'node_modules/.bin/x' is not excluded
        GlobCollection standardInclude = [Glob.Parse("**/*", GlobDialect.Standard), Glob.Parse("!**/node_modules/**", GlobDialect.Standard)];
        Assert.False(standardInclude.IsPartialMatch("node_modules"));

        GlobCollection hiddenInclude = [Glob.Parse("**/*", GlobDialect.Standard, GlobOptions.MatchLeadingDot), Glob.Parse("!**/node_modules/**", GlobDialect.Standard)];
        Assert.True(hiddenInclude.IsPartialMatch("node_modules"));
        Assert.True(hiddenInclude.IsMatch("node_modules/.bin/x"));

        GlobCollection hiddenExclude = [Glob.Parse("**/*", GlobDialect.Standard, GlobOptions.MatchLeadingDot), Glob.Parse("!**/node_modules/**", GlobDialect.Standard, GlobOptions.MatchLeadingDot)];
        Assert.False(hiddenExclude.IsPartialMatch("node_modules"));
    }

    [Fact]
    public void IsPartialMatch_ExcludeMustCoverEveryKindOfItem()
    {
        // The exclude pattern only matches directories, so the files below node_modules are still included
        GlobCollection globs = [Glob.Parse("**/*", GlobDialect.Standard), Glob.Parse("!**/node_modules/**/", GlobDialect.Standard)];
        Assert.True(globs.IsPartialMatch("node_modules"));
        Assert.True(globs.IsMatch("node_modules/a.js"));
    }

    [Fact]
    public void EnumerateFiles_DoesNotVisitExcludedDirectories()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        rootDirectory.CreateEmptyFile("src/app.js");
        rootDirectory.CreateEmptyFile("src/node_modules/lib/index.js");
        rootDirectory.CreateEmptyFile("node_modules/lib/index.js");

        GlobCollection globs =
        [
            Glob.Parse("**/*", GlobDialect.Standard),
            Glob.Parse("!**/node_modules/**", GlobDialect.Standard),
        ];

        using var enumerator = new RecordingEnumerator(globs, rootDirectory.FullPath);
        var files = new List<string>();
        while (enumerator.MoveNext())
        {
            files.Add(enumerator.Current);
        }

        Assert.Equal([Path.Combine(rootDirectory.FullPath, "src", "app.js")], files);
        Assert.Equal(["src"], enumerator.VisitedDirectories);
    }

    [Fact]
    public void EnumerateFiles_GitIgnoreDoesNotVisitDirectoriesReIncludedAsAWhole()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        rootDirectory.CreateEmptyFile("a.log");
        rootDirectory.CreateEmptyFile("docs/b.log");
        rootDirectory.CreateEmptyFile("docs/sub/c.log");

        var gitIgnore = GlobCollection.ParseGitIgnore("""
            *.log
            !/docs/**
            """);

        Assert.False(gitIgnore.IsPartialMatch("docs"));

        using var enumerator = new RecordingEnumerator(gitIgnore, rootDirectory.FullPath);
        var files = new List<string>();
        while (enumerator.MoveNext())
        {
            files.Add(enumerator.Current);
        }

        Assert.Equal([Path.Combine(rootDirectory.FullPath, "a.log")], files);
        Assert.Empty(enumerator.VisitedDirectories);
    }

    [Fact]
    public void EnumerateFiles_GitIgnoreReturnsTheContentOfAnAnchoredExcludedDirectory()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        rootDirectory.CreateEmptyFile("bin/a.dll");
        rootDirectory.CreateEmptyFile("src/bin/b.dll");

        // '/bin' has no trailing '/': it matches the directory, and git excludes everything below it
        var gitIgnore = GlobCollection.ParseGitIgnore("/bin");
        Assert.True(gitIgnore.IsMatch("bin/a.dll"));
        Assert.True(gitIgnore.IsPartialMatch("bin"));

        // Only look at the files: the directory matches too
        var files = gitIgnore.EnumerateFiles(rootDirectory.FullPath).Where(File.Exists).ToList();
        Assert.Equal([Path.Combine(rootDirectory.FullPath, "bin", "a.dll")], files);
    }

    [Fact]
    public void DocumentationExample_ParseGitIgnoreWithBaseDirectory()
    {
        #region GlobCollection_ParseGitIgnoreWithBaseDirectory
        // The content of src/App/.gitignore
        var gitIgnore = GlobCollection.ParseGitIgnore("""
            /generated/
            *.tmp
            """, baseDirectory: "src/App");

        // Paths are relative to the root of the repository
        Assert.True(gitIgnore.IsMatch("src/App/generated/Model.cs"));
        Assert.True(gitIgnore.IsMatch("src/App/Data/cache.tmp"));
        Assert.False(gitIgnore.IsMatch("generated/Model.cs"));
        Assert.False(gitIgnore.IsMatch("src/Lib/cache.tmp"));
        #endregion
    }

    [Theory]
    [InlineData("src/App", "", "x.tmp", false)]
    [InlineData("src/App", "src", "App", false)] // the base directory itself
    [InlineData("src/App", "src/App", "", false)]
    [InlineData("src/App", "src/App/", "", false)]
    [InlineData("src/App", "src/App", "x.tmp", true)]
    [InlineData("src/App", "src/App/", "x.tmp", true)]
    [InlineData("src/App", "src/App/a", "x.tmp", true)]
    [InlineData("src/App", "src/AppX", "x.tmp", false)]
    [InlineData("src/App", "src/App/a/x.tmp", "", true)]
    [InlineData("./src/App/", "src/App/a/x.tmp", "", true)]
    [InlineData(".", "x.tmp", "", true)]
    [InlineData("", "x.tmp", "", true)]
    [InlineData(null, "x.tmp", "", true)]
    public void ParseGitIgnoreWithBaseDirectory_MatchesPathsBelowTheBaseDirectory(string? baseDirectory, string directory, string filename, bool expected)
    {
        var gitIgnore = GlobCollection.ParseGitIgnore("*.tmp\nApp\n", baseDirectory);
        Assert.Equal(expected, gitIgnore.IsMatch(directory, filename));
    }

    [Fact]
    public void ParseGitIgnoreWithBaseDirectory_AnchorsPatternsToTheBaseDirectory()
    {
        var gitIgnore = GlobCollection.ParseGitIgnore("/local.txt\nbin/\n", "src");

        Assert.True(gitIgnore.IsMatch("src/local.txt"));
        Assert.False(gitIgnore.IsMatch("src/a/local.txt"));
        Assert.False(gitIgnore.IsMatch("local.txt"));
        Assert.True(gitIgnore.IsMatch("src/a/bin/x.dll"));
        Assert.False(gitIgnore.IsMatch("bin/x.dll"));

        Assert.True(gitIgnore.IsPartialMatch(""));
        Assert.True(gitIgnore.IsPartialMatch("src"));
        Assert.True(gitIgnore.IsPartialMatch("src/a/bin"));
        Assert.False(gitIgnore.IsPartialMatch("tests"));
    }

    [Fact]
    public async Task LoadGitIgnoreAsyncWithBaseDirectory()
    {
        using var rootDirectory = TemporaryDirectory.Create();
        var path = rootDirectory.CreateTextFile("src/.gitignore", "*.tmp\n");

        var gitIgnore = await GlobCollection.LoadGitIgnoreAsync(path, "src");

        Assert.True(gitIgnore.IsMatch("src/a.tmp"));
        Assert.False(gitIgnore.IsMatch("a.tmp"));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void GitIgnoreRuleIndexAgreesWithEvaluatingEveryRule()
    {
        // Every rule evaluated from the last one is the reference: the collection only evaluates the rules whose name
        // or extension can match the path
        string[] rulePool =
        [
            "*.log", "*.txt", "!*.txt", "!important.log", "bin/", "!bin/", "bin", "/bin", "obj/", "a/bin/", "**/bin",
            "*.tar.gz", "*~", "*.", ".*", "!.keep", "build*/", "a", "!a", "a/", "b/a", "/a/b", "*.a", "!*.a/", "a.b",
            "x/**/y", "x/**", "!x/**", "*/", "**/", "[ab]", "a.log/", "README", "*.Log", "!/a/*", "\\!a", "a\\ ",
        ];
        string[] pathPool =
        [
            "a", "b", "a/b", "b/a", "a/a", "bin", "a/bin", "x/bin/y", "a.log", "a/important.log", "important.log",
            "a.txt", "a/b.txt", "x.tar.gz", "y.gz", "x~", "a.", ".a", ".keep", "a/.keep", "build1", "a/build", "a.a",
            "a/b.a", "a.b", "x/y", "x/a/y", "x/y/z", "README", "a/README", "a.Log", "a/a.log", "!a", "a ", "a/a.log/c",
            "a/.b.txt", "a.b.c",
        ];

        var random = new Random(42);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var rules = Enumerable.Range(0, random.Next(1, 12)).Select(_ => rulePool[random.Next(rulePool.Length)]).ToArray();
            var gitIgnore = GlobCollection.ParseGitIgnore(string.Join('\n', rules));
            foreach (var path in pathPool)
            {
                foreach (var itemType in new PathItemType?[] { PathItemType.File, PathItemType.Directory, null })
                {
                    var expected = IsIgnoredReference(gitIgnore, path, itemType);
                    Assert.Equal(expected, gitIgnore.IsMatch(path, [], itemType), $"Rules: {string.Join(" | ", rules)}; path: {path}; type: {itemType}");

                    var index = path.LastIndexOf('/', StringComparison.Ordinal);
                    if (index >= 0)
                    {
                        Assert.Equal(expected, gitIgnore.IsMatch(path.AsSpan(0, index), path.AsSpan(index + 1), itemType), $"Rules: {string.Join(" | ", rules)}; path: {path}; type: {itemType} (split)");
                    }
                }
            }
        }

        static bool IsIgnoredReference(GlobCollection gitIgnore, string path, PathItemType? itemType)
        {
            var segments = path.Split('/');
            for (var i = 1; i < segments.Length; i++)
            {
                if (GetLastMatch(gitIgnore, string.Join('/', segments[..i]), PathItemType.Directory) is GlobMode.Include)
                    return true;
            }

            return GetLastMatch(gitIgnore, path, itemType) is GlobMode.Include;
        }

        static GlobMode? GetLastMatch(GlobCollection gitIgnore, string path, PathItemType? itemType)
        {
            for (var i = gitIgnore.Count - 1; i >= 0; i--)
            {
                if (gitIgnore[i].IsMatch(path, [], itemType))
                    return gitIgnore[i].Mode;
            }

            return null;
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void EnumerateFileSystemEntriesAgreesWithIsMatch()
    {
        // The enumeration resolves each directory once and prunes directories, which must not change the results
        string[] files =
        [
            "a.txt", "a.log", ".hidden/a.txt", "src/a.txt", "src/b.log", "src/bin/a.dll", "src/bin/sub/b.dll", "bin/c.dll",
            "docs/a.txt", "docs/sub/b.log", "node_modules/x/index.js", "src/node_modules/y/index.js", "x/y/z/a.txt",
            "x/y/.keep", "build1/out.txt",
        ];

        using var rootDirectory = TemporaryDirectory.Create();
        foreach (var file in files)
        {
            rootDirectory.CreateEmptyFile(file);
        }

        var allEntries = Directory.EnumerateFileSystemEntries(rootDirectory.FullPath, "*", SearchOption.AllDirectories)
            .Select(path => (Path: Path.GetRelativePath(rootDirectory.FullPath, path).Replace('\\', '/'), IsDirectory: Directory.Exists(path)))
            .ToArray();

        string[] gitIgnoreRulePool = ["*.log", "!b.log", "bin/", "/bin", "!src/bin/", "src/*", "!src/a.txt", "node_modules", "!/docs/**", "docs/", ".*", "x/**", "!x/y/", "build*/", "!*.txt", "**/sub", "*"];
        string[] globPool = ["**/*", "**/*.txt", "src/**", "!**/node_modules/**", "!**/bin/**", "!src/**", "!**/sub/**/*", "!.hidden/**", "!x/**/", "*", "!**/*.log", "**/"];

        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = default(FileAttributes) };
        var random = new Random(1);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var gitIgnoreRules = Enumerable.Range(0, random.Next(1, 8)).Select(_ => gitIgnoreRulePool[random.Next(gitIgnoreRulePool.Length)]).ToArray();
            AssertEnumeration(GlobCollection.ParseGitIgnore(string.Join('\n', gitIgnoreRules)), string.Join(" | ", gitIgnoreRules));

            var globs = Enumerable.Range(0, random.Next(1, 5)).Select(_ => globPool[random.Next(globPool.Length)]).ToArray();
            var options2 = random.Next(2) == 0 ? GlobOptions.None : GlobOptions.MatchLeadingDot;
            AssertEnumeration(new GlobCollection([.. globs.Select(glob => Glob.Parse(glob, GlobDialect.Standard, options2))]), string.Join(" | ", globs) + " " + options2);
        }

        void AssertEnumeration(GlobCollection collection, string description)
        {
            var expected = allEntries
                .Where(entry => entry.IsDirectory ? ((IGlobEvaluatable)collection).CanMatchDirectories : ((IGlobEvaluatable)collection).CanMatchFiles)
                .Where(entry => collection.IsMatch(entry.Path, [], entry.IsDirectory ? PathItemType.Directory : PathItemType.File))
                .Select(entry => entry.Path)
                .Order(StringComparer.Ordinal)
                .ToArray();

            var actual = collection.EnumerateFileSystemEntries(rootDirectory.FullPath, options)
                .Select(path => Path.GetRelativePath(rootDirectory.FullPath, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected, actual, description);
        }
    }

    private sealed class RecordingEnumerator(IGlobEvaluatable glob, string directory)
        : GlobFileSystemEnumerator<string>(glob, directory, new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = default(FileAttributes) })
    {
        public List<string> VisitedDirectories { get; } = [];

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
        {
            var result = base.ShouldRecurseIntoEntry(ref entry);
            if (result)
            {
                VisitedDirectories.Add(Path.GetRelativePath(RootDirectory, entry.ToFullPath()).Replace('\\', '/'));
            }

            return result;
        }

        protected override string TransformEntry(ref FileSystemEntry entry) => entry.ToFullPath();

        private string RootDirectory { get; } = directory;
    }
}
