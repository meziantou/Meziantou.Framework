namespace Meziantou.Framework.Globbing.Tests;

public class GlobTests
{
    [Theory]
    [InlineData("")] // Empty is not valid
    [InlineData("../*.txt")] // Cannot start with '..'
    [InlineData("**/../test")] // Cannot have '..' after a starting '**'
    [InlineData("a\\")] // Cannot ends with the escape character '\'
    [InlineData("{a")] // Missing '}'
    [InlineData("[a")] // Missing ']'
    [InlineData("a[/]b")]  // literal contains '/'
    [InlineData("a[a/]b")]  // literal contains '/'
    [InlineData("a[.-0]b")] // literal contains '/'
    [InlineData("a{/}b")]  // literal contains '/'
    [InlineData("a{a,/}b")] // literal contains '/'
    public void ParseInvalid(string pattern)
    {
        Assert.False(Glob.TryParse(pattern, GlobDialect.Standard, GlobOptions.None, out var result));
        Assert.Null(result);

        Assert.Throws<ArgumentException>(() => Glob.Parse(pattern, GlobDialect.Standard));
    }

    [Theory]
    [InlineData("**/*", "test")]
    [InlineData("test/*.txt", "test")]
    [InlineData("**/a.txt", "test/a")]
    [InlineData("**/*", "test/a")]
    [InlineData("**/*.txt", "test/a")]
    [InlineData("test/**/a*.txt", "test/a")]
    [InlineData("test/**/a*.txt", "test/a/b/c/d")]
    [InlineData("!test/**/a*.txt", "test/a/b/c/d")]
    public void ShouldRecurse(string pattern, string folderPath)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        var globi = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase);
        Assert.True(glob.IsPartialMatch(folderPath));
        Assert.True(globi.IsPartialMatch(folderPath));
    }

    [Theory]
    [InlineData("test/*.txt", "titi")]
    [InlineData("test/**/a*.txt", "titi/a")]
    [InlineData("test/**/a*.txt", "titi/b/c/d")]
    public void ShouldNotRecurse(string pattern, string folderPath)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        var globi = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase);
        Assert.False(glob.IsPartialMatch(folderPath));
        Assert.False(globi.IsPartialMatch(folderPath));
    }

    [Theory]
    [InlineData("a/b", "a/b")]
    [InlineData("a?c", "abc")]
    [InlineData("a?c", "adc")]
    [InlineData("*.txt", "test.txt")]
    [InlineData(".*", ".gitignore")]
    [InlineData("*.*", "a.txt")]
    [InlineData("!*.txt", "a.txt")]
    [InlineData("*/test.txt", "a/test.txt")]
    [InlineData("a/*.txt", "a/test.txt")]
    [InlineData("**/test.txt", "test.txt")]
    [InlineData("**/test.txt", "a/test.txt")]
    [InlineData("**/test.txt", "a/b/test.txt")]
    [InlineData("src/**/test.txt", "src/a/b/test.txt")]
    [InlineData("test/**/*", "test/a.txt")]
    [InlineData("test/**/*", "test/a/b/c.txt")]
    [InlineData("a/**/test.txt", "a/test.txt")]
    [InlineData("a/**/test.txt", "a/b/test.txt")]
    [InlineData("a/./b", "a/b")]
    [InlineData("a/../b", "b")]
    [InlineData("{a,b}", "a")]
    [InlineData("{a,b}", "b")]
    [InlineData("{a,b}.txt", "b.txt")]
    [InlineData("{ab,cd,edg,h,s}.txt", "cd.txt")]
    [InlineData("*{ab,cd,edg,h,s}.txt", "abcd.txt")]
    [InlineData("[ab]", "a")]
    [InlineData("[ab]", "b")]
    [InlineData("[abcd]", "c")]
    [InlineData("[!ab]", "c")]
    [InlineData("[!abcd]", "z")]
    [InlineData("[a-a]", "a")]
    [InlineData("[a-d]", "a")]
    [InlineData("[a-d]", "b")]
    [InlineData("[a-d]", "c")]
    [InlineData("[a-d]", "d")]
    [InlineData("[-]", "-")]
    [InlineData("[a-]", "a")]
    [InlineData("[a-]", "-")]
    [InlineData("[,--]", "-")]
    [InlineData("[--.]", "-")]
    [InlineData("[!a-d]", "e")]
    [InlineData("[a-df-i]", "d")]
    [InlineData("[a-df-i]", "g")]
    [InlineData("[a-df-ik]", "i")]
    [InlineData("[a-df-ik]", "k")]
    [InlineData("\\a", "a")]
    [InlineData("\\[ab\\]", "[ab]")]
    [InlineData("{a\\,,b}", "a,")]
    [InlineData("{a\\,,b}", "b")]
    [InlineData("\\*", "*")]
    [InlineData("fol[d]e[r][0-1]a", "folder0a")]
    [InlineData("fol[d]e[r][0-1]*", "folder0ab")]
    [InlineData("folder[0-1]/**/f{ab,il}[aei]*.{txt,png,ico}", "folder0/folder1/file001.txt")]
    [InlineData("*[abc].{txt,png,ico}", "file001a.txt")]
    [InlineData("*[a-c].{txt,ico}", "file001a.ico")]
    [InlineData("literal", "literal")]
    [InlineData("a/literal", "a/literal")]
    [InlineData("path/*atstand", "path/fooatstand")]
    [InlineData("path/hats*nd", "path/hatsforstand")]
    [InlineData("path/?atstand", "path/hatstand")]
    [InlineData("path/?atstand?", "path/hatstands")]
    [InlineData("p?th/*a[bcd]", "pAth/fooooac")]
    [InlineData("p?th/*a[bcd]b[e-g]a[1-4]", "pAth/fooooacbfa2")]
    [InlineData("p?th/*a[bcd]b[e-g]a[1-4][!wxyz]", "pAth/fooooacbfa2v")]
    [InlineData("p?th/*a[bcd]b[e-g]a[1-4][!wxyz][!a-c][!1-3].*", "pAth/fooooacbfa2vd4.txt")]
    [InlineData("path/**/somefile.txt", "path/foo/bar/baz/somefile.txt")]
    [InlineData("p?th/*a[bcd]b[e-g]a[1-4][!wxyz][!a-c][!1-3].*", "pGth/yGKNY6acbea3rm8.")]
    [InlineData("**/file.*", "folder/file.csv")]
    [InlineData("**/file.*", "file.txt")]
    [InlineData("*file.txt", "file.txt")]
    [InlineData("THIS_IS_A_DIR/*", "THIS_IS_A_DIR/somefile")]
    [InlineData("DIR1/*/*", "DIR1/DIR2/file.txt")]
    [InlineData("~/*~3", "~/abc123~3")]
    [InlineData("**/Shock* 12", "HKEY_LOCAL_MACHINE/SOFTWARE/Adobe/Shockwave 12")]
    [InlineData("**/*ave*2", "HKEY_LOCAL_MACHINE/SOFTWARE/Adobe/Shockwave 12")]
    [InlineData("Stuff, *", "Stuff, x")]
    [InlineData("path/**/somefile.txt", "path//somefile.txt")]
    [InlineData("**/app*.js", "dist/app.js")]
    [InlineData("**/app*.js", "dist/app.a72ka8234.js")]
    [InlineData("**/y", "y")]
    [InlineData("**/gfx/*.gfx", "HKEY_LOCAL_MACHINE/gfx/foo.gfx")]
    [InlineData("**/gfx/**/*.gfx", "a_b/gfx/bar/foo.gfx")]
    [InlineData("foo/bar!.baz", "foo/bar!.baz")]
    [InlineData("foo/bar[!!].baz", "foo/bar7.baz")]
    [InlineData("foo/bar[!]].baz", "foo/bar9.baz")]
    [InlineData("foo/bar[!?].baz", "foo/bar7.baz")]
    [InlineData("foo/bar[![].baz", "foo/bar7.baz")]
    [InlineData("myergen/[[]a]tor", "myergen/[a]tor")]
    [InlineData("myergen/[[]ator", "myergen/[ator")]
    [InlineData("myergen/[[][]]ator", "myergen/[]ator")]
    [InlineData("myergen[*]ator", "myergen*ator")]
    [InlineData("myergen[*][]]ator", "myergen*]ator")]
    [InlineData("myergen[*]]ator", "myergen*]ator")]
    [InlineData("myergen[?]ator", "myergen?ator")]
    [InlineData("**/[#!]*", "#test3")]
    [InlineData("**/[#!]*", "#this is a comment")]
    [InlineData("[#!]*", @"#test3")]
    [InlineData("[#!]*", "#this is a comment")]
    [InlineData("a/**/b", "a/b")]
    [InlineData("a/**/b/c", "a/b/c")]
    [InlineData("a/**/b/c", "a/x/y/b/c")]
    [InlineData("**/*", "a")]
    [InlineData("**/*", "a/b")]
    [InlineData("**/*/", "a/b/")]
    [InlineData("**/test/", "test/")]
    [InlineData("**/test/", "a/test/")]
    public void Match(string pattern, string path)
    {
        var isDirectory = path.EndsWith('/', StringComparison.Ordinal);
        var pathWithoutEndingSlash = isDirectory ? path.TrimEnd('/') : path;
        var directoryName = Path.GetDirectoryName(pathWithoutEndingSlash);
        var fileName = Path.GetFileName(pathWithoutEndingSlash);
        var itemType = isDirectory ? PathItemType.Directory : PathItemType.File;

        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        var globi = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(directoryName, fileName, itemType));
        Assert.True(globi.IsMatch(path));
        Assert.True(globi.IsMatch(directoryName, fileName, itemType));
        Assert.True(glob.IsPartialMatch(directoryName!));
        Assert.True(globi.IsPartialMatch(directoryName!));

        if (OperatingSystem.IsWindows())
        {
            Assert.True(glob.IsMatch(path.Replace('/', '\\')));
            Assert.True(glob.IsMatch(directoryName!.Replace('/', '\\'), fileName, itemType));
        }
    }

    [Theory]
    [InlineData("a?c", "a?C")]
    [InlineData("a?c", "adC")]
    [InlineData("*.txt", "test.Txt")]
    [InlineData(".*", ".GitIgnore")]
    [InlineData("!*.txt", "A.TXT")]
    [InlineData("*/test.txt", "A/tEst.txt")]
    [InlineData("a/*.txt", "a/test.txT")]
    [InlineData("**/test.txt", "tesT.txt")]
    [InlineData("**/test.txt", "a/tEst.txt")]
    [InlineData("**/test.txt", "a/B/tesT.txt")]
    [InlineData("test/**/*", "test/a.tXt")]
    [InlineData("test/**/*", "test/a/B/c.txt")]
    [InlineData("a/**/test.txt", "A/tEst.txt")]
    [InlineData("a/**/test.txt", "A/b/tEst.txt")]
    [InlineData("a/./b", "a/B")]
    [InlineData("a/../b", "B")]
    [InlineData("{a,b}", "A")]
    [InlineData("{a,b}", "B")]
    [InlineData("{a,b}.txt", "B.txt")]
    [InlineData("{ab,cd,edg,h,s}.txt", "cD.txt")]
    [InlineData("*{ab,cd,edg,h,s}.txt", "aBcd.txt")]
    [InlineData("[ab]", "A")]
    [InlineData("[ab]", "B")]
    [InlineData("[abcd]", "C")]
    [InlineData("[!ab]", "C")]
    [InlineData("[!abcd]", "Z")]
    [InlineData("[a-a]", "A")]
    [InlineData("[a-d]", "A")]
    [InlineData("[a-d]", "B")]
    [InlineData("[a-d]", "C")]
    [InlineData("[a-d]", "D")]
    [InlineData("[A-D]", "d")]
    [InlineData("[a-]", "A")]
    [InlineData("[!a-d]", "E")]
    [InlineData("[a-df-i]", "D")]
    [InlineData("[a-df-i]", "G")]
    [InlineData("[a-df-ik]", "I")]
    [InlineData("[a-df-ik]", "K")]
    [InlineData("[0-9]", "0")]
    [InlineData("[0-9]", "9")]
    [InlineData("[0-9]", "5")]
    [InlineData("[é]", "É")]
    [InlineData("\\a", "A")]
    [InlineData("\\[ab\\]", "[Ab]")]
    [InlineData("{a\\,,b}", "A,")]
    [InlineData("{a\\,,b}", "B")]
    [InlineData("*abc", "ZABC")]
    public void MatchIgnoreCase(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("*", ".hidden")]
    [InlineData("?", ".")]
    [InlineData("[.]", ".")]
    [InlineData("**/*.txt", ".hidden/test.txt")]
    [InlineData("**/*.txt", "src/.hidden/test.txt")]
    public void DoesNotMatchLeadingDotByDefault(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        Assert.False(glob.IsMatch(path));
        Assert.False(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("*", ".hidden")]
    [InlineData("?", ".")]
    [InlineData("[.]", ".")]
    [InlineData("**/*.txt", ".hidden/test.txt")]
    [InlineData("**/*.txt", "src/.hidden/test.txt")]
    public void MatchLeadingDotOption(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData(".*", ".hidden")]
    [InlineData("**/.hidden/*.txt", ".hidden/test.txt")]
    [InlineData("**/.hidden/*.txt", "src/.hidden/test.txt")]
    public void MatchExplicitLeadingDot(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("a.txt", "a")]
    [InlineData("a.txt", "test.png")]
    [InlineData("a.txt", "test/a.txt")]
    [InlineData("**/*.txt", "test.png")]
    [InlineData("**/*.txt", "a/test.png")]
    [InlineData("**/*.txt", "a/b/test.png")]
    [InlineData("src/**/test.txt", "src/a/b/test.png")]
    [InlineData("test/*.txt", "test/test.png")]
    [InlineData("test/*.txt", "foo/bar.txt")]
    [InlineData("test/[ab].txt", "test/c.txt")]
    [InlineData("[abcd]", "e")]
    [InlineData("[!a-d]", "a")]
    [InlineData("[!abcd]", "d")]
    [InlineData("[!a-d]", "d")]
    [InlineData("[!a-df-g][!z]", "eb")]
    [InlineData("[!a-df-g][!z]", "ee")]
    [InlineData("folder[0-1]/**/f{ab,il}[aei]*.{txt,png,ico}", "file001.txt")]
    [InlineData("a/b", "ab")]
    [InlineData("a/b", "acb")]
    [InlineData("file*test*", "test")]
    [InlineData("file*test*", "testa")]
    [InlineData("file*test*", "btesta")]
    [InlineData("file*test*", "fil_btesta")]
    [InlineData("literal", "literals/foo")]
    [InlineData("literal", "literals")]
    [InlineData("literal", "foo/literal")]
    [InlineData("literal", "fliteral")]
    [InlineData("path/hats*nd", "path/hatsblahn")]
    [InlineData("path/hats*nd", "path/hatsblahndt")]
    [InlineData("path/?atstand", "path/moatstand")]
    [InlineData("path/?atstand", "path/batstands")]
    [InlineData("**/file.csv", "file.txt")]
    [InlineData("*file.txt", "folder")]
    [InlineData("Shock* 12", "HKEY_LOCAL_MACHINE/SOFTWARE/Adobe/Shockwave 12")]
    [InlineData("*ave*2", "HKEY_LOCAL_MACHINE/SOFTWARE/Adobe/Shockwave 12")]
    [InlineData("*ave 12", "HKEY_LOCAL_MACHINE/SOFTWARE/Adobe/Shockwave 12")]
    [InlineData("Bumpy/**/AssemblyInfo.cs", "Bumpy.Test/Properties/AssemblyInfo.cs")]
    [InlineData("abc/**", "abcd")]
    [InlineData("**/segment1/**/segment2/**", "test/segment1/src/segment2")]
    [InlineData("**/.*", "foobar.")]
    [InlineData("**/*/", "a/b")]
    [InlineData("**/test/", "test")]
    [InlineData("**/test/", "a/test")]
    [InlineData("**/*", "a/b/")]
    [InlineData("**/test", "test/")]
    [InlineData("**/test", "a/test/")]
    [InlineData("a/**/b/c", "a/x/y/b/d")]
    public void DoesNotMatch(string pattern, string path)
    {
        var isDirectory = path.EndsWith('/', StringComparison.Ordinal);
        var pathWithoutEndingSlash = isDirectory ? path.TrimEnd('/') : path;
        var directoryName = Path.GetDirectoryName(pathWithoutEndingSlash);
        var fileName = Path.GetFileName(pathWithoutEndingSlash);
        var itemType = isDirectory ? PathItemType.Directory : PathItemType.File;

        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        var globi = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase);
        Assert.False(glob.IsMatch(path));
        Assert.False(glob.IsMatch(directoryName, fileName, itemType));
        Assert.False(globi.IsMatch(path));
        Assert.False(globi.IsMatch(directoryName, fileName, itemType));
    }

    // Corpus source: https://raw.githubusercontent.com/git/git/master/t/t3070-wildmatch.sh
    [Theory]
    [InlineData("*[al]?", "ball")]
    [InlineData("t[a-g]n", "ten")]
    [InlineData("a[]]b", "a]b")]
    [InlineData("a[]-]b", "a-b")]
    [InlineData("a[]-]b", "a]b")]
    [InlineData("foo/**/bar", "foo/baz/bar")]
    [InlineData("foo/**/**/bar", "foo/b/a/z/bar")]
    [InlineData("**/foo", "bar/baz/foo")]
    [InlineData("**/bar/*/*", "deep/foo/bar/baz/x")]
    [InlineData("*/*/*", "foo/bba/arr")]
    [InlineData("**/*X*/**/*i", "ab/cXd/efXg/hi")]
    public void Match_FromGitWildMatchCorpus(string pattern, string path)
    {
        var isDirectory = path.EndsWith('/', StringComparison.Ordinal);
        var pathWithoutEndingSlash = isDirectory ? path.TrimEnd('/') : path;
        var directoryName = Path.GetDirectoryName(pathWithoutEndingSlash);
        var fileName = Path.GetFileName(pathWithoutEndingSlash);
        var itemType = isDirectory ? PathItemType.Directory : PathItemType.File;

        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(directoryName, fileName, itemType));
    }

    // Corpus source: https://raw.githubusercontent.com/git/git/master/t/t3070-wildmatch.sh
    [Theory]
    [InlineData("*f", "foo")]
    [InlineData("[ten]", "ten")]
    [InlineData("t[!a-g]n", "ten")]
    [InlineData("a[]-]b", "aab")]
    [InlineData("foo*bar", "foo/baz/bar")]
    [InlineData("foo?bar", "foo/bar")]
    [InlineData("*/foo", "bar/baz/foo")]
    [InlineData("**/bar*", "foo/bar/baz")]
    [InlineData("**/bar/*", "deep/foo/bar")]
    [InlineData("**/bar/*", "deep/foo/bar/baz/")]
    [InlineData("**/bar**", "foo/bar/baz")]
    [InlineData("*/bar/**", "deep/foo/bar/baz/x")]
    [InlineData("*X*i", "ab/cXd/efXg/hi")]
    public void DoesNotMatch_FromGitWildMatchCorpus(string pattern, string path)
    {
        var isDirectory = path.EndsWith('/', StringComparison.Ordinal);
        var pathWithoutEndingSlash = isDirectory ? path.TrimEnd('/') : path;
        var directoryName = Path.GetDirectoryName(pathWithoutEndingSlash);
        var fileName = Path.GetFileName(pathWithoutEndingSlash);
        var itemType = isDirectory ? PathItemType.Directory : PathItemType.File;

        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        Assert.False(glob.IsMatch(path));
        Assert.False(glob.IsMatch(directoryName, fileName, itemType));
    }

    [Theory]
    [InlineData("literal1", "LITERAL1")]
    [InlineData("*ral*", "LITERAL1")]
    [InlineData("[list]s", "LS")]
    [InlineData("[list]s", "iS")]
    [InlineData("[list]s", "Is")]
    [InlineData("range/[a-b][C-D]", "range/ac")]
    [InlineData("range/[a-b][C-D]", "range/Ad")]
    [InlineData("range/[a-b][C-D]", "range/BD")]
    public void DoesNotMatch_CaseSensitive(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard);
        Assert.False(glob.IsMatch(path));
        Assert.False(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData(GlobOptions.None)]
    [InlineData(GlobOptions.IgnoreCase)]
    public void EnumerateFiles1(GlobOptions options)
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("d1/d2/f1.txt");
        directory.CreateEmptyFile("d1/d2/f2.txt");
        directory.CreateEmptyFile("d1/f3.txt");
        directory.CreateEmptyFile("d1/f3.png");

        var glob = Glob.Parse("**/*.txt", GlobDialect.Standard, options);

        AssertEnumerateFiles(directory, glob, ["d1/d2/f1.txt", "d1/d2/f2.txt", "d1/f3.txt"]);
    }

    [Theory]
    [InlineData(GlobOptions.None)]
    [InlineData(GlobOptions.IgnoreCase)]
    public void EnumerateFiles2(GlobOptions options)
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("d1/d2/f1.txt");
        directory.CreateEmptyFile("d1/d2/f2.txt");
        directory.CreateEmptyFile("d1/f3.txt");

        var glob = Glob.Parse("d1/*.txt", GlobDialect.Standard, options);
        AssertEnumerateFiles(directory, glob, ["d1/f3.txt"]);
    }

    [Theory]
    [InlineData(GlobOptions.None)]
    [InlineData(GlobOptions.IgnoreCase)]
    public void EnumerateFileSystemEntries1(GlobOptions options)
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("d1/d2/f1.txt");
        directory.CreateEmptyFile("d1/d2/f2.txt");
        directory.CreateEmptyFile("d1/f3.txt");

        var glob = Glob.Parse("d1/*.txt", GlobDialect.Standard, options);
        AssertEnumerateFileSystemEntries(directory, glob, ["d1/f3.txt"]);
    }

    [Theory]
    [InlineData(GlobOptions.None)]
    [InlineData(GlobOptions.IgnoreCase)]
    public void EnumerateFileSystemEntries2(GlobOptions options)
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("d1/d2/f1.txt");
        directory.CreateEmptyFile("d1/d2/f2.txt");
        directory.CreateEmptyFile("d1/d3/f2.txt");
        directory.CreateEmptyFile("d1/f3.txt");

        var glob = Glob.Parse("d1/*/", GlobDialect.Standard, options);
        AssertEnumerateFileSystemEntries(directory, glob, ["d1/d2", "d1/d3"]);
    }

    [Theory]
    [InlineData(GlobOptions.None)]
    [InlineData(GlobOptions.IgnoreCase)]
    public void GlobCollection1(GlobOptions options)
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("d1/d2/f1.txt");
        directory.CreateEmptyFile("d1/d2/f2.txt");
        directory.CreateEmptyFile("d1/f3.txt");
        directory.CreateEmptyFile("d3/f4.txt");

        var glob = new GlobCollection(
            Glob.Parse("**/*.txt", GlobDialect.Standard, options),
            Glob.Parse("!d1/*.txt", GlobDialect.Standard, options));

        AssertEnumerateFiles(directory, glob,
        [
            "d1/d2/f1.txt",
            "d1/d2/f2.txt",
            "d3/f4.txt",
        ]);
    }

    // Repro: https://github.com/meziantou/Meziantou.Framework/issues/923
    [Fact]
    public void GlobCollection2()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("f1.txt");
        directory.CreateEmptyFile("System Volume Information/f2.txt");

        var glob = new GlobCollection(
            Glob.Parse("**/*.txt", GlobDialect.Standard, GlobOptions.IgnoreCase),
            Glob.Parse("!*/System Volume Information/", GlobDialect.Standard, GlobOptions.IgnoreCase),
            Glob.Parse("!*/System Volume Information/**/*", GlobDialect.Standard, GlobOptions.IgnoreCase));

        Assert.True(glob.IsMatch("System Volume Information/f1.txt"));
        AssertEnumerateFiles(directory, glob,
        [
            "System Volume Information/f2.txt",
            "f1.txt",
        ]);
    }

    // Repro: https://github.com/meziantou/Meziantou.Framework/issues/923
    [Fact]
    public void GlobCollection3()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("f1.txt");
        directory.CreateEmptyFile("System Volume Information/f2.txt");

        var glob = new GlobCollection(
            Glob.Parse("**/*.txt", GlobDialect.Standard, GlobOptions.IgnoreCase),
            Glob.Parse("!System Volume Information/", GlobDialect.Standard, GlobOptions.IgnoreCase),
            Glob.Parse("!System Volume Information/**/*", GlobDialect.Standard, GlobOptions.IgnoreCase));

        Assert.False(glob.IsMatch("System Volume Information/f1.txt"));

        AssertEnumerateFiles(directory, glob,
        [
            "f1.txt",
        ]);
    }

    [Theory]
    [InlineData(GlobOptions.None)]
    [InlineData(GlobOptions.IgnoreCase)]
    public void GlobCollection4(GlobOptions options)
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("d1/d1.1/f1.txt");
        directory.CreateEmptyFile("d1/d1.1/f2.txt");
        directory.CreateEmptyFile("d1/d1.2/f3.txt");
        directory.CreateEmptyFile("d1/f4.txt");
        directory.CreateEmptyFile("d3/f5.txt");

        var glob = new GlobCollection(
            Glob.Parse("**/*", GlobDialect.Standard, options),
            Glob.Parse("**/*/", GlobDialect.Standard, options),
            Glob.Parse("!d1/*.txt", GlobDialect.Standard, options),
            Glob.Parse("!d1/d1.2/", GlobDialect.Standard, options));

        AssertEnumerateFileSystemEntries(directory, glob,
        [
            "d1",
            "d1/d1.1",
            "d1/d1.1/f1.txt",
            "d1/d1.1/f2.txt",
            "d1/d1.2/f3.txt",
            "d3",
            "d3/f5.txt",
        ]);
    }

    [Fact]
    public void EnumerateFiles_LeadingDot()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile(".hidden/f1.txt");
        directory.CreateEmptyFile("visible/f2.txt");

        AssertEnumerateFiles(directory, Glob.Parse("**/*.txt", GlobDialect.Standard), ["visible/f2.txt"]);
        AssertEnumerateFiles(directory, Glob.Parse("**/*.txt", GlobDialect.Standard, GlobOptions.MatchLeadingDot), [".hidden/f1.txt", "visible/f2.txt"]);
    }

    [Theory]
    [InlineData("readme.md", "readme.md")]
    [InlineData("readme.md", "a/readme.md")]
    [InlineData("readme.md", "a/b/readme.md")]
    [InlineData("a/", "a/b/readme.md")]
    [InlineData("a/", "b/a/a")]
    [InlineData("a/b.txt", "a/b.txt")]
    [InlineData("a/**/b.txt", "a/b.txt")]
    [InlineData("a/**/b.txt", "a/c/b.txt")]
    [InlineData("a/**/b.txt", "a/c/d/b.txt")]
    [InlineData("a/**/*.txt", "a/c/d/b.txt")]
    [InlineData("a/**/?.txt", "a/c/d/b.txt")]
    public void MatchGit(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        var globi = Glob.Parse(pattern, GlobDialect.Git, GlobOptions.IgnoreCase);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
        Assert.True(globi.IsMatch(path));
        Assert.True(globi.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
        Assert.True(glob.IsPartialMatch(Path.GetDirectoryName(path)!));
        Assert.True(globi.IsPartialMatch(Path.GetDirectoryName(path)!));

        if (OperatingSystem.IsWindows())
        {
            Assert.True(glob.IsMatch(path.Replace('/', '\\')));
            Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!.Replace('/', '\\'), Path.GetFileName(path)));
        }
    }

    [Theory]
    [InlineData("*", ".hidden")]
    [InlineData("**/*.txt", ".hidden/test.txt")]
    [InlineData("**/*.txt", "src/.hidden/test.txt")]
    public void MatchGitLeadingDot(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("**/.*", "foobar.")]
    [InlineData("a/", "sample")]
    [InlineData("a/", "b/a")]
    [InlineData("/a/", "b/a/a")]
    [InlineData("a.txt/", "a.txt")]
    [InlineData("a/b.txt", "c/a/b.txt")]
    [InlineData("a/*", "a/b/c.txt")]
    public void DoesNotMatchGit(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        var globi = Glob.Parse(pattern, GlobDialect.Git, GlobOptions.IgnoreCase);
        Assert.False(glob.IsMatch(path));
        Assert.False(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
        Assert.False(globi.IsMatch(path));
        Assert.False(globi.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    // Corpus source: https://raw.githubusercontent.com/git/git/master/Documentation/gitignore.adoc
    [Theory]
    [InlineData("hello.*", "hello.txt")]
    [InlineData("hello.*", "a/hello.java")]
    [InlineData("/hello.*", "hello.c")]
    [InlineData("foo/", "foo/bar.txt")]
    [InlineData("foo/*", "foo/test.json")]
    [InlineData("foo/*", "foo/bar")]
    [InlineData("doc/frotz", "doc/frotz")]
    [InlineData("/doc/frotz", "doc/frotz")]
    public void MatchGit_FromGitIgnoreDocumentationExamples(string pattern, string path)
    {
        var isDirectory = path.EndsWith('/', StringComparison.Ordinal);
        var pathWithoutEndingSlash = isDirectory ? path.TrimEnd('/') : path;
        var directoryName = Path.GetDirectoryName(pathWithoutEndingSlash);
        var fileName = Path.GetFileName(pathWithoutEndingSlash);
        var itemType = isDirectory ? PathItemType.Directory : PathItemType.File;

        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(directoryName, fileName, itemType));
    }

    // Corpus source: https://raw.githubusercontent.com/git/git/master/Documentation/gitignore.adoc
    [Theory]
    [InlineData("/hello.*", "a/hello.java")]
    [InlineData("foo/", "foo")]
    [InlineData("foo/*", "foo/bar/hello.c")]
    [InlineData("foo/*", "a/foo/bar")]
    [InlineData("doc/frotz", "a/doc/frotz")]
    [InlineData("/doc/frotz", "a/doc/frotz")]
    public void DoesNotMatchGit_FromGitIgnoreDocumentationExamples(string pattern, string path)
    {
        var isDirectory = path.EndsWith('/', StringComparison.Ordinal);
        var pathWithoutEndingSlash = isDirectory ? path.TrimEnd('/') : path;
        var directoryName = Path.GetDirectoryName(pathWithoutEndingSlash);
        var fileName = Path.GetFileName(pathWithoutEndingSlash);
        var itemType = isDirectory ? PathItemType.Directory : PathItemType.File;

        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.False(glob.IsMatch(path));
        Assert.False(glob.IsMatch(directoryName, fileName, itemType));
    }

    [Theory]
    [InlineData("**/*.cs", "src/Program.cs")]
    [InlineData(@"src\**\*.cs", "src/Generated/Program.cs")]
    [InlineData(@"src\*.cs", "src/Program.cs")]
    [InlineData("%2A.cs", "*.cs")]
    [InlineData("%3F.cs", "?.cs")]
    [InlineData("[abc].cs", "[abc].cs")]
    [InlineData("{a,b}.cs", "{a,b}.cs")]
    [InlineData("!file.cs", "!file.cs")]
    public void MatchMSBuild(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.MSBuild);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("a**")]
    [InlineData("**a")]
    [InlineData("a**b")]
    public void MSBuildRecursiveWildcardMustBeAPathSegment(string pattern)
    {
        Assert.False(Glob.TryParse(pattern, GlobDialect.MSBuild, GlobOptions.None, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("*", "src/Program.cs")]
    [InlineData("a?b", "a/b")]
    [InlineData("a[/]b", "a/b")]
    [InlineData("**", "src/Program.cs")]
    [InlineData("*.cs", "src/Program.cs")]
    [InlineData("!file.cs", "!file.cs")]
    [InlineData("{a,b}.cs", "{a,b}.cs")]
    [InlineData("*", ".hidden")]
    public void MatchPosix(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Posix);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("*", "src/Program.cs")]
    [InlineData("a?b", "a/b")]
    [InlineData("**", "src/Program.cs")]
    [InlineData("*.cs", "src/Program.cs")]
    public void DoesNotMatchPosixPathAcrossPathSeparators(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.PosixPath);
        Assert.False(glob.IsMatch(path));
        Assert.False(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("src/*.cs", "src/Program.cs")]
    [InlineData("src/?/Program.cs", "src/a/Program.cs")]
    [InlineData("!file.cs", "!file.cs")]
    [InlineData("{a,b}.cs", "{a,b}.cs")]
    [InlineData("*", ".hidden")]
    public void MatchPosixPath(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.PosixPath);
        Assert.True(glob.IsMatch(path));
        Assert.True(glob.IsMatch(Path.GetDirectoryName(path)!, Path.GetFileName(path)));
    }

    [Fact]
    public void EnumerateFiles_PosixMatchesAcrossPathSeparators()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("Program.cs");
        directory.CreateEmptyFile("src/Program.cs");

        AssertEnumerateFiles(directory, Glob.Parse("*.cs", GlobDialect.Posix), ["Program.cs", "src/Program.cs"]);
    }

    [Theory]
    [InlineData("*?a", "ab")]
    [InlineData("*.md?", "readme.md")]
    [InlineData("v*.?", "v1.")]
    [InlineData("***[ab]", "aaac")]
    [InlineData("*[!a]b", "ab")]
    [InlineData("*[a-c]d", "abe")]
    [InlineData("*?a*?b", "ab")]
    public void SingleCharacterSubSegmentDoesNotReadPastTheEndOfTheSegment(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.False(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("*?a", "aba")]
    [InlineData("*.md?", "readme.mdx")]
    [InlineData("v*.?", "v1.2")]
    [InlineData("***[ab]", "aaab")]
    [InlineData("*[!a]b", "acb")]
    [InlineData("*[a-c]d", "abd")]
    [InlineData("*[ab]c", "ac")]
    public void SingleCharacterSubSegmentStillMatchesWhenTheSegmentIsLongEnough(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("*[!a]a", "aa/a")]
    [InlineData("*[!a]*a", "aa/a")]
    [InlineData("*[!a-c]a", "aa/a")]
    [InlineData("*?a", "aa/a")]
    public void SingleCharacterSubSegmentDoesNotMatchAPathSeparator(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.False(glob.IsMatch(path));
    }

    [Fact]
    public void EnumerateFiles_TrailingAnyCharacterAfterWildcard()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("readme.md");
        directory.CreateEmptyFile("readme.mdx");

        AssertEnumerateFiles(directory, Glob.Parse("*.md?", GlobDialect.Standard), ["readme.mdx"]);
    }

    [Theory]
    [InlineData("*a*?b", "abbcb")]
    [InlineData("*b*a?", "baaaa")]
    [InlineData("*a*[a-c]", "abba")]
    [InlineData("*a*b*c", "axxbxxc")]
    [InlineData("*[ab]*?", "acca")]
    public void SegmentWithSeveralWildcardsTriesEverySplitPoint(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("*a*b*c", "axxbxxd")]
    [InlineData("*a*b*c", "axxcxxb")]
    public void SegmentWithSeveralWildcardsStillRejectsNonMatchingPaths(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.False(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("**/b.txt", "b.txt")]
    [InlineData("**/b.txt", "a/b.txt")]
    [InlineData("**/b.txt", "a/nested/b.txt")]
    [InlineData("a/**/b.txt", "a/b.txt")]
    [InlineData("a/**/b.txt", "a/nested/b.txt")]
    public void RecursiveWildcardFollowedByASingleSegmentMatchesAtEveryDepth(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("**/b.txt", "c.txt")]
    [InlineData("**/b.txt", "a/c.txt")]
    [InlineData("a/**/b.txt", "c/b.txt")]
    public void RecursiveWildcardFollowedByASingleSegmentStillRejectsOtherPaths(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.False(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("**", "a.txt")]
    [InlineData("**", "src/a.txt")]
    [InlineData("src/**", "src/a.txt")]
    [InlineData("src/**", "src/nested/a.txt")]
    [InlineData("src/**/**", "src/nested/a.txt")]
    public void TrailingRecursiveWildcardMatchesTheRestOfThePath(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("src/**", "src")]
    [InlineData("src/**", "other/a.txt")]
    [InlineData("src/**", "srcx/a.txt")]
    public void TrailingRecursiveWildcardRequiresAtLeastOneSegment(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.False(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("src/**", "src/.hidden")]
    [InlineData("src/**", "src/.hidden/a.txt")]
    [InlineData("**", ".hidden")]
    public void TrailingRecursiveWildcardHonorsLeadingDot(string pattern, string path)
    {
        Assert.False(Glob.Parse(pattern, GlobDialect.Standard).IsMatch(path));
        Assert.True(Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot).IsMatch(path));
    }

    [Theory]
    [InlineData(GlobDialect.Git)]
    [InlineData(GlobDialect.MSBuild)]
    public void TrailingRecursiveWildcardMatchesTheRestOfThePathInEveryDialect(GlobDialect dialect)
    {
        var glob = Glob.Parse("src/**", dialect, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch("src/a.txt"));
        Assert.True(glob.IsMatch("src/nested/a.txt"));
    }

    [Fact]
    public void EnumerateFiles_TrailingRecursiveWildcard()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("src/a.txt");
        directory.CreateEmptyFile("src/nested/b.txt");
        directory.CreateEmptyFile("other/c.txt");

        AssertEnumerateFiles(directory, Glob.Parse("src/**", GlobDialect.Standard), ["src/a.txt", "src/nested/b.txt"]);
    }

    private static void AssertEnumerateFiles(TemporaryDirectory directory, IGlobEvaluatable glob, string[] expectedResult)
    {
        var items = glob.EnumerateFiles(directory.FullPath)
            .AsEnumerable()
            .Select(path => FullPath.FromPath(path).MakePathRelativeTo(directory.FullPath).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expectedResult, items);
    }

    private static void AssertEnumerateFileSystemEntries(TemporaryDirectory directory, IGlobEvaluatable glob, string[] expectedResult)
    {
        var items = glob.EnumerateFileSystemEntries(directory.FullPath)
            .AsEnumerable()
            .Select(path => FullPath.FromPath(path).MakePathRelativeTo(directory.FullPath).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expectedResult, items);
    }
}
