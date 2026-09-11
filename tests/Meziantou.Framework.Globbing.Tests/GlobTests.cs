using System.IO.Enumeration;

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
    [InlineData(@"a{a,\/}b")] // literal contains an escaped '/'
    [InlineData("[z-a]")] // reversed range
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
    [InlineData("[!a-df-g][!z]", "eb")]
    [InlineData("[!a-df-g][!z]", "ee")]
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
    [InlineData(@"a\/b", "a/b")] // an escaped separator is still a separator
    [InlineData(@"**\/b", "a/b")]
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
    [InlineData(GlobDialect.Standard, @"a\\b")]
    [InlineData(GlobDialect.Standard, @"a\\*")]
    [InlineData(GlobDialect.Standard, @"*\\b")]
    [InlineData(GlobDialect.Git, @"a\\*")]
    [InlineData(GlobDialect.PosixPath, @"a\\b")]
    public void EscapedBackslashIsAnOrdinaryCharacter(GlobDialect dialect, string pattern)
    {
        // '\' is a path separator on Windows, so no path segment can hold one there
        Assert.Equal(!OperatingSystem.IsWindows(), Glob.Parse(pattern, dialect).IsMatch(@"a\b"));
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
    [InlineData("[!a-df-g][!z]", "az")]
    [InlineData("[!a-df-g][!z]", "ez")]
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

    // The expected results were verified with 'git check-ignore', which evaluates an entry with wildmatch.
    [Theory]
    // Character classes
    [InlineData("[[:digit:]]", "5", true)]
    [InlineData("[[:digit:]]", "c", false)]
    [InlineData("[[:upper:]]", "B", true)]
    [InlineData("[[:upper:]]", "c", false)]
    [InlineData("x[[:alpha:][:digit:]]", "x5", true)]
    [InlineData("[![:digit:]]", "c", true)]
    // '^' negates a bracket expression, like '!'
    [InlineData("[^a]", "c", true)]
    [InlineData("[^a]", "a", false)]
    // A backslash escapes a character in a bracket expression too
    [InlineData(@"[\]]", "]", true)]
    [InlineData(@"[a\-c]", "-", true)]
    [InlineData(@"[a\-c]", "b", false)]
    // wildmatch compares the start of a range before it reads the '-', so a reversed range still matches its start
    [InlineData("[c-a]", "c", true)]
    [InlineData("[c-a]", "a", false)]
    [InlineData("[c-a]", "b", false)]
    [InlineData("[b--0]", "0", true)]
    [InlineData("[b--0]", "-", false)]
    // A '/' in a bracket expression never matches, but it does not split the entry
    [InlineData("a[b/c]d", "abd", true)]
    [InlineData("a[b/c]d", "a/d", false)]
    [InlineData("[.-0]x", ".x", true)]
    [InlineData("[.-0]x", "0x", true)]
    // Any run of '*' that makes a whole path segment is a '**', and a run inside a segment is a '*'
    [InlineData("***/x", "a/b/x", true)]
    [InlineData("***/x", "x", true)]
    [InlineData("a/***", "a/b/c", true)]
    [InlineData("foo**/bar", "fooz/bar", true)]
    [InlineData("foo**/bar", "foo/x/bar", false)]
    [InlineData("a/**b", "a/b", true)]
    [InlineData("a/**b", "a/x/b", false)]
    // '.' and '..' are ordinary names, and git paths never contain them
    [InlineData("./a", "a", false)]
    [InlineData("a/./b", "a/b", false)]
    [InlineData("a/../b", "b", false)]
    [InlineData(".", "a", false)]
    // An escaped character is the character itself, '/' included
    [InlineData(@"a\/b", "a/b", true)]
    [InlineData(@"\[a]", "[a]", true)]
    public void GitMatchesLikeGitCheckIgnore(string pattern, string path, bool expected)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.Equal(expected, glob.IsMatch(path));

        var index = path.AsSpan().LastIndexOf('/');
        Assert.Equal(expected, glob.IsMatch(path.AsSpan(0, Math.Max(index, 0)), path.AsSpan(index + 1), PathItemType.File));
    }

    [Theory]
    // 'abc/**' matches everything inside 'abc' but not 'abc' itself, and the trailing '/' does not change it
    [InlineData("*/**/", "", "a", false)]
    [InlineData("*/**/", "a", "b", true)]
    [InlineData("a/**/", "", "a", false)]
    [InlineData("a/**/", "a", "b", true)]
    [InlineData("**/", "", "a", true)]
    [InlineData("**/", "a/b", "c", true)]
    public void GitDirectoryEntryEndingWithARecursiveWildcard(string pattern, string directory, string name, bool expected)
    {
        Assert.Equal(expected, Glob.Parse(pattern, GlobDialect.Git).IsMatch(directory, name, PathItemType.Directory));
    }

    [Theory]
    [InlineData("[a")] // unterminated bracket expression
    [InlineData("a[b")]
    [InlineData(@"a\")] // escape character without a character to escape
    [InlineData(@"[\")]
    [InlineData("[[:foo:]]")] // unknown character class
    [InlineData("[[::]]")]
    [InlineData("//a")] // empty path segment
    [InlineData("a//b")]
    [InlineData("a//")]
    [InlineData("/")] // nothing is left once the '!' and the '/' are set aside
    [InlineData("//")]
    [InlineData("!")]
    [InlineData("!/")]
    public void GitRejectsEntriesThatNeverMatch(string pattern)
    {
        Assert.False(Glob.TryParse(pattern, GlobDialect.Git, GlobOptions.None, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("[[:upper:]]", "c")]
    [InlineData("[[:lower:]]", "B")]
    public void GitIgnoreCaseAppliesToCharacterClasses(string pattern, string path)
    {
        // Verified with 'git check-ignore' and core.ignorecase=true
        Assert.True(Glob.Parse(pattern, GlobDialect.Git, GlobOptions.IgnoreCase).IsMatch(path));
        Assert.False(Glob.Parse(pattern, GlobDialect.Git).IsMatch(path));
    }

    [Fact]
    public void GitMatchesLeadingDotsWithoutTheOption()
    {
        Assert.True(Glob.Parse("*/*.txt", GlobDialect.Git).IsMatch(".a/.b.txt"));
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
    [InlineData("***")]
    [InlineData("***/a")]
    [InlineData("a/***")]
    public void MSBuildRecursiveWildcardMustBeAPathSegment(string pattern)
    {
        Assert.False(Glob.TryParse(pattern, GlobDialect.MSBuild, GlobOptions.None, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("**/../a")]
    [InlineData("*/../a")]
    [InlineData("src/*/../a")]
    public void MSBuildRejectsAParentSegmentAfterAWildcard(string pattern)
    {
        // MSBuild does not expand such a file spec, it keeps it as a literal item
        Assert.False(Glob.TryParse(pattern, GlobDialect.MSBuild, GlobOptions.None, out var result));
        Assert.Null(result);
    }

    // The expected results were verified with a real MSBuild item evaluation ('dotnet msbuild -getItem').
    [Theory]
    // MSBuild matches the names that start with a dot
    [InlineData("*", ".editorconfig", true)]
    [InlineData(".*", ".editorconfig", true)]
    [InlineData("**/*.cs", ".git/x.cs", true)]
    [InlineData("**/.git/*", ".git/config", true)]
    [InlineData("src/**", "src/.hidden/a.cs", true)]
    // A file name made of '*.*' matches every file, including the ones without an extension
    [InlineData("*.*", "LICENSE", true)]
    [InlineData("*.*", "a.b", true)]
    [InlineData("wwwroot/**/*.*", "wwwroot/LICENSE", true)]
    [InlineData("wwwroot/**/*.*", "wwwroot/css/site.css", true)]
    [InlineData("a*.*", "abc", false)]
    [InlineData("a*.*", "ab.c", true)]
    [InlineData("*.*/f.cs", "dir1/f.cs", false)]
    [InlineData("*.*/f.cs", "dir.2/f.cs", true)]
    [InlineData("%2A.%2A", "abc", false)]
    [InlineData("%2A.%2A", "*.*", true)]
    // An escaped separator is still a separator
    [InlineData("a%2Fb%2Fc.cs", "a/b/c.cs", true)]
    [InlineData("a%5Cb%5Cc.cs", "a/b/c.cs", true)]
    [InlineData("**%2F*.cs", "a/b/c.cs", true)]
    // '.' and '..' are resolved, and a leading '..' goes above the project directory
    [InlineData("src/./x.cs", "src/x.cs", true)]
    [InlineData("src/../*.*", "LICENSE", true)]
    [InlineData("../shared/*.cs", "../shared/s.cs", true)]
    [InlineData("../shared/**/*.cs", "../shared/sub/t.cs", true)]
    [InlineData("../shared/*.cs", "shared/s.cs", false)]
    [InlineData("a/../../b/*.cs", "../b/c.cs", true)]
    [InlineData("a/../../b/*.cs", "b/c.cs", false)]
    // A leading separator makes the pattern absolute
    [InlineData("/usr/src/*.cs", "/usr/src/a.cs", true)]
    [InlineData("/usr/src/*.cs", "usr/src/a.cs", false)]
    [InlineData(@"\usr\src\*.cs", "/usr/src/a.cs", true)]
    [InlineData("/usr/**/*.cs", "/usr/src/a/b.cs", true)]
    // Separators that follow another one are collapsed
    [InlineData("src//*.cs", "src/a.cs", true)]
    public void MSBuildMatchesLikeMSBuildItemEvaluation(string pattern, string path, bool expected)
    {
        var glob = Glob.Parse(pattern, GlobDialect.MSBuild);
        Assert.Equal(expected, glob.IsMatch(path));

        // The root directory keeps its separator: "/a" is the file "a" in the directory "/"
        var index = path.AsSpan().LastIndexOf('/');
        Assert.Equal(expected, glob.IsMatch(path.AsSpan(0, index <= 0 ? index + 1 : index), path.AsSpan(index + 1), PathItemType.File));
    }

    [Fact]
    public void MSBuildMatchesLeadingDotsWithoutTheOption()
    {
        Assert.True(Glob.Parse("**/*", GlobDialect.MSBuild).IsMatch(".git/config"));
        Assert.True(((IGlobEvaluatable)Glob.Parse("**/*", GlobDialect.MSBuild)).TraverseDirectories);
    }

    [Fact]
    public void EnumerateFiles_MSBuildIncludesHiddenFilesAndFilesWithoutExtension()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("LICENSE");
        directory.CreateEmptyFile(".editorconfig");
        directory.CreateEmptyFile("wwwroot/.well-known/a.json");
        directory.CreateEmptyFile("wwwroot/css/site.css");

        AssertEnumerateFiles(directory, Glob.Parse("**/*.*", GlobDialect.MSBuild), [".editorconfig", "LICENSE", "wwwroot/.well-known/a.json", "wwwroot/css/site.css"]);
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

    // The expected results were verified with glibc's fnmatch(3), using the flags 0 for Posix and FNM_PATHNAME for
    // PosixPath. The BSD libc disagrees on the cases that POSIX leaves unspecified, and on an unterminated '[', which
    // POSIX reads as an ordinary character.
    [Theory]
    // '^' negates a bracket expression, like '!'
    [InlineData("[^a]", "b", true, true)]
    [InlineData("[^a]", "a", false, false)]
    [InlineData("[^]a]", "b", true, true)]
    [InlineData("[^]a]", "]", false, false)]
    // A backslash escapes a character in a bracket expression too
    [InlineData(@"[\]]", "]", true, true)]
    [InlineData(@"[\]]", @"\]", false, false)]
    [InlineData(@"[!\]]", "a", true, true)]
    [InlineData(@"[!\]]", "]", false, false)]
    [InlineData(@"[a\-c]", "-", true, true)]
    [InlineData(@"[a\-c]", "b", false, false)]
    [InlineData(@"[\\]", @"\", true, true)]
    [InlineData(@"[a-\c]", "b", true, true)]
    // A range whose start comes after its end contains no character
    [InlineData("[z-a]", "z", false, false)]
    [InlineData("[z-a]", "a", false, false)]
    [InlineData("[z-ab]", "b", true, true)]
    [InlineData("[!z-a]", "z", true, true)]
    // A '[' that does not open a complete bracket expression is an ordinary character
    [InlineData("[", "[", true, true)]
    [InlineData("[a", "[a", true, true)]
    [InlineData("a[", "a[", true, true)]
    [InlineData("[!", "[!", true, true)]
    [InlineData("[]", "[]", true, true)]
    [InlineData("[!]", "[!]", true, true)]
    [InlineData("[a-z", "[a-z", true, true)]
    [InlineData("[*", "[abc", true, true)]
    [InlineData("[?", "[a", true, true)]
    [InlineData("[[:alpha:]", "[a", true, true)]
    [InlineData(@"[a\]", "[a]", true, true)]
    // Character classes
    [InlineData("[[:alpha:][:digit:]]", "5", true, true)]
    [InlineData("[^[:alpha:]]", "5", true, true)]
    [InlineData("[^[:alpha:]]", "a", false, false)]
    [InlineData("[[:alpha:]-]", "-", true, true)]
    [InlineData("[[:alpha:]-z]", "-", true, true)] // a class cannot start a range
    [InlineData("[[:alpha:]-z]", "z", true, true)]
    [InlineData("[[:DIGIT:]]", "D]", true, true)] // only lowercase letters make a class name
    [InlineData("[[:a]", "a", true, true)]
    [InlineData("[[:]", ":", true, true)]
    // Equivalence classes hold a single character in the POSIX locale
    [InlineData("[[=a=]]", "a", true, true)]
    [InlineData("[[=a=]b]", "b", true, true)]
    [InlineData("[[===]]", "=", true, true)]
    [InlineData("[[==]]", "=]", true, true)] // not an equivalence class
    [InlineData("[[=ab=]]", "a]", true, true)] // not an equivalence class
    [InlineData("[[=a=]-z]", "-", true, true)] // an equivalence class cannot start a range
    [InlineData("[[=a=]-z]", "z", true, true)]
    [InlineData("[[=a=]-z]", "b", false, false)]
    // Collating symbols can be a range bound
    [InlineData("[[.-.]]", "-", true, true)]
    [InlineData("[[...]]", ".", true, true)]
    [InlineData("[[.].]]", "]", true, true)]
    [InlineData("[[.[.]]", "[", true, true)]
    [InlineData("[[.a.]-c]", "b", true, true)]
    [InlineData("[a-[.c.]]", "b", true, true)]
    // A '-' that comes first or last is an ordinary character
    [InlineData("[-a]", "-", true, true)]
    [InlineData("[a-]", "-", true, true)]
    [InlineData("[!-a]", "-", false, false)]
    [InlineData("[a-b-c]", "-", true, true)]
    [InlineData("[a-b-c]", "c", true, true)]
    [InlineData("[]-a]", "]", true, true)]
    [InlineData("[]-a]", "^", true, true)]
    [InlineData("[--/]", ".", true, true)]
    // With FNM_PATHNAME, a '/' is only matched by a '/' of the pattern
    [InlineData("a/b", "a/b", true, true)]
    [InlineData("a?b", "a/b", true, false)]
    [InlineData("a*b", "a/b", true, false)]
    [InlineData("a[/]b", "a/b", true, false)]
    [InlineData("a[!x]b", "a/b", true, false)]
    [InlineData("a[b/c]d", "abd", true, true)]
    [InlineData("a[b/c]d", "a/d", true, false)]
    [InlineData("[.-0]", ".", true, true)]
    [InlineData("[.-0]", "/", true, false)]
    [InlineData(@"a\/b", "a/b", true, true)]
    [InlineData("**", "a/b", true, false)]
    [InlineData("**/b", "a/b", true, true)]
    [InlineData("a/**/b", "a/b", false, false)]
    [InlineData("a/**/b", "a/x/b", true, true)]
    [InlineData("*/", "a/", true, true)]
    // Every '/' is significant
    [InlineData("/a", "/a", true, true)]
    [InlineData("/a", "a", false, false)]
    [InlineData("/*", "/a", true, true)]
    [InlineData("/*/b", "/x/b", true, true)]
    [InlineData("a//b", "a//b", true, true)]
    [InlineData("a//b", "a/b", false, false)]
    [InlineData("a/b", "a//b", false, false)]
    // '.' and '..' are ordinary names
    [InlineData("a/./b", "a/b", false, false)]
    [InlineData("a/./b", "a/./b", true, true)]
    [InlineData("a/../b", "b", false, false)]
    [InlineData("./a", "./a", true, true)]
    [InlineData(".", ".", true, true)]
    [InlineData("..", "..", true, true)]
    // Without FNM_PERIOD, a wildcard matches a leading dot
    [InlineData("*", ".a", true, true)]
    [InlineData("?a", ".a", true, true)]
    [InlineData("a/*", "a/.b", true, true)]
    public void PosixMatchesLikeFnmatch(string pattern, string text, bool expectedPosix, bool expectedPosixPath)
    {
        Assert.Equal(expectedPosix, Glob.Parse(pattern, GlobDialect.Posix).IsMatch(text));

        // PosixPath splits the path on the separators of the platform, and '\' is one on Windows
        if (OperatingSystem.IsWindows() && text.Contains('\\', StringComparison.Ordinal))
            return;

        Assert.Equal(expectedPosixPath, Glob.Parse(pattern, GlobDialect.PosixPath).IsMatch(text));
    }

    [Theory]
    [InlineData("[[:foo:]]")] // unknown character class
    [InlineData("[a[:foo:]]")]
    [InlineData("[[::]]")]
    [InlineData("[[.a]")] // unterminated collating symbol
    [InlineData("[[..]]")] // empty collating symbol
    [InlineData("[[.ab.]]")] // multi-character collating element
    [InlineData(@"[\")] // escape character without a character to escape
    [InlineData(@"[a-\")]
    [InlineData(@"a\")]
    public void PosixRejectsPatternsThatFnmatchNeverMatches(string pattern)
    {
        Assert.False(Glob.TryParse(pattern, GlobDialect.Posix, GlobOptions.None, out _));
        Assert.False(Glob.TryParse(pattern, GlobDialect.PosixPath, GlobOptions.None, out _));
    }

    [Fact]
    public void PosixPathReadsATrailingSeparatorAsADirectory()
    {
        // fnmatch compares strings, so FNM_PATHNAME lets 'a/*' match "a/" with an empty '*'. Here a path that ends
        // with a separator is a directory, and a pattern that does not end with one only matches files.
        Assert.False(Glob.Parse("a/*", GlobDialect.PosixPath).IsMatch("a/"));
        Assert.True(Glob.Parse("a/*", GlobDialect.PosixPath).IsMatch("a/b"));
        Assert.True(Glob.Parse("a/*/", GlobDialect.PosixPath).IsMatch("a/b/"));
    }

    [Fact]
    public void PosixMatchesAnyKindOfItem()
    {
        // fnmatch compares strings: a directory name is matched like a file name
        var glob = Glob.Parse("*.d", GlobDialect.Posix);
        Assert.True(glob.IsMatch("src", "x.d", PathItemType.Directory));
        Assert.True(glob.IsMatch("src", "x.d", PathItemType.File));
        Assert.False(glob.IsMatch("src", "x.e", PathItemType.Directory));
    }

    [Fact]
    public void EnumerateFileSystemEntries_PosixMatchesDirectories()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("a.d");
        directory.CreateEmptyFile("src/x.d/f.txt");

        AssertEnumerateFileSystemEntries(directory, Glob.Parse("*.d", GlobDialect.Posix), ["a.d", "src/x.d"]);
    }

    [Theory]
    [InlineData(GlobDialect.Standard, "src/*.txt", "src/", "a.txt")]
    [InlineData(GlobDialect.Standard, "**/a.txt", "src/", "a.txt")]
    [InlineData(GlobDialect.Standard, "**/src/a.txt", "src/", "a.txt")]
    [InlineData(GlobDialect.Git, "src/a.txt", "src/", "a.txt")]
    [InlineData(GlobDialect.MSBuild, "src/*.txt", "src/", "a.txt")]
    [InlineData(GlobDialect.Posix, "src/*.txt", "src/", "a.txt")]
    [InlineData(GlobDialect.PosixPath, "src/*.txt", "src/", "a.txt")]
    [InlineData(GlobDialect.PosixPath, "/*", "/", "a")]
    [InlineData(GlobDialect.MSBuild, "/*", "/", "a")]
    [InlineData(GlobDialect.MSBuild, "/usr/*", "/usr/", "a")]
    public void DirectoryWithATrailingSeparator(GlobDialect dialect, string pattern, string directory, string filename)
    {
        // The trailing separator only separates the directory from the file name
        var glob = Glob.Parse(pattern, dialect, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(directory, filename));
        Assert.True(glob.IsMatch(directory, filename, PathItemType.File));
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

    [Theory]
    // A negated bracket expression consumes exactly one character, whichever of its parts rejects the character.
    [InlineData("[!a-cx]", "z")]
    [InlineData("[!a-cx]z", "zz")]
    [InlineData("[!a-cx]*", "zy")]
    [InlineData("[!a-cx][!a-cx]", "yz")]
    [InlineData("a[!b-cx]c", "azc")]
    public void NegatedBracketExpressionWithBothARangeAndACharacterMatchesOneCharacter(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("[!a-cx]", "a")] // in the range
    [InlineData("[!a-cx]", "x")] // in the character set
    [InlineData("[!a-cx]", "")] // there is no character to consume
    [InlineData("[!a-cx]", "yz")] // it consumes a single character
    [InlineData("[!a-cx]z*", "zy")]
    [InlineData("a[!x-zq]b", "a/b")] // it does not consume a path separator
    public void NegatedBracketExpressionWithBothARangeAndACharacterRejectsOtherPaths(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.False(glob.IsMatch(path));
    }

    [Theory]
    // The first alternative that matches is not necessarily the one that lets the rest of the pattern match.
    [InlineData("{a,ab}", "ab")]
    [InlineData("{ab,a}", "ab")]
    [InlineData("{ab,a}b", "ab")]
    [InlineData("{a,ab}b", "ab")]
    [InlineData("{a,ab}c", "abc")]
    [InlineData("*{a,ab}c", "xabc")]
    [InlineData("{a,ab}*c", "abxc")]
    public void LiteralSetTriesEveryAlternativeAgainstTheRestOfThePattern(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("{a,ab}", "b")]
    [InlineData("{a,ab}", "abc")]
    [InlineData("{a,ab}c", "abbc")]
    [InlineData("*{a,ab}c", "xabd")]
    public void LiteralSetStillRejectsNonMatchingPaths(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.False(glob.IsMatch(path));
    }

    [Theory]
    // An empty alternative consumes nothing, so the segment requires no particular first character.
    [InlineData("{,a}b", "b")]
    [InlineData("{,a}b", "ab")]
    [InlineData("{a,}b", "b")]
    [InlineData("*{,a}b", "xb")]
    [InlineData("*{,a}b", "xab")]
    [InlineData("x{,a}", "x")]
    [InlineData("x{,a}", "xa")]
    public void LiteralSetWithAnEmptyAlternativeConsumesNothing(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    [InlineData("{,a}b", "cb")]
    [InlineData("{,a}b", "aab")]
    [InlineData("x{,a}", "xb")]
    public void LiteralSetWithAnEmptyAlternativeStillRejectsNonMatchingPaths(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot);
        Assert.False(glob.IsMatch(path));
    }

    [Theory]
    // Ordinal case-insensitive comparison relates more than two characters to each other: it treats the greek
    // capital sigma, the small sigma and the final small sigma as equal.
    [InlineData("Σ?", "ςx")]
    [InlineData("*Σ?", "aςx")]
    [InlineData("σ", "ς")]
    [InlineData("**/Σ", "a/ς")]
    [InlineData("**/*Σ", "a/bς")]
    [InlineData("a/Σ*", "a/ςb")]
    public void IgnoreCaseMatchesEveryOrdinalCaseInsensitiveEquivalent(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase | GlobOptions.MatchLeadingDot);
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    // The candidate must be tested against the range as written: lowering the bounds of '[@-B]' would drop both
    // '@' and 'A', which the range does contain. '[@-B]' with 'a' covers a range that is not entirely uppercase
    // matching through the uppercase form of the candidate. A range that runs from an uppercase letter to a
    // lowercase one cannot be used here: it spans U+005C, which is a path separator on Windows, and the parser
    // rejects such a range.
    [InlineData("[@-B]", "A")]
    [InlineData("[@-B]", "a")]
    [InlineData("[@-B]", "@")]
    [InlineData("[@-B]", "B")]
    [InlineData("[A-Z]", "a")]
    [InlineData("[a-z]", "A")]
    public void IgnoreCaseRangeKeepsTheCharactersOfTheOriginalRange(string pattern, string path)
    {
        Assert.True(Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase | GlobOptions.MatchLeadingDot).IsMatch(path));
        Assert.False(Glob.Parse("[!" + pattern[1..], GlobDialect.Standard, GlobOptions.IgnoreCase | GlobOptions.MatchLeadingDot).IsMatch(path));
    }

    [Theory]
    [InlineData("[@-B]", "C")]
    [InlineData("[@-B]", "c")]
    [InlineData("[@-B]", "?")]
    [InlineData("[A-Z]", "0")]
    public void IgnoreCaseRangeRejectsCharactersOutsideOfTheRange(string pattern, string path)
    {
        Assert.False(Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.IgnoreCase | GlobOptions.MatchLeadingDot).IsMatch(path));
        Assert.True(Glob.Parse("[!" + pattern[1..], GlobDialect.Standard, GlobOptions.IgnoreCase | GlobOptions.MatchLeadingDot).IsMatch(path));
    }

    [Theory]
    // A 'char' counter wraps around when a range ends at char.MaxValue, which used to make the parser allocate
    // until it ran out of memory, or read past the end of the array it was filling.
    [InlineData("[￾-￿]", "￿")]
    [InlineData("[￾-￿]x", "￾x")]
    [InlineData("*[￾-￿]", "a￿")]
    [InlineData("**/[￾-￿]/x", "a/￿/x")]
    [InlineData("[￿-￿]", "￿")]
    [InlineData("[￸-￿]", "￻")]
    public void RangeEndingAtTheLastCharacterIsParsedAndMatched(string pattern, string path)
    {
        Assert.True(Glob.TryParse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot, out var glob));
        Assert.True(glob.IsMatch(path));
    }

    [Theory]
    // git reads '{' and '}' as ordinary characters.
    [InlineData("{a,b}", "{a,b}")]
    [InlineData("{a,b}.cs", "{a,b}.cs")]
    [InlineData("a{b}c", "a{b}c")]
    public void GitDoesNotSupportLiteralSets(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.True(glob.IsMatch(path));
        Assert.False(glob.IsMatch("a"));
        Assert.False(glob.IsMatch("b"));
    }

    [Theory]
    [InlineData(GlobDialect.Posix)]
    [InlineData(GlobDialect.PosixPath)]
    [InlineData(GlobDialect.Git)]
    public void NamedCharacterClasses(GlobDialect dialect)
    {
        AssertMatch("[[:digit:]]", "5");
        AssertNoMatch("[[:digit:]]", "a");
        AssertMatch("[[:alpha:]]", "a");
        AssertNoMatch("[[:alpha:]]", "5");
        AssertMatch("[[:alnum:]]", "1");
        AssertNoMatch("[[:alnum:]]", "-");
        AssertMatch("[[:space:]]", " ");
        AssertNoMatch("[[:space:]]", "a");
        AssertMatch("[[:blank:]]", "\t");
        AssertNoMatch("[[:blank:]]", "a");
        AssertMatch("[[:cntrl:]]", "\u0001");
        AssertNoMatch("[[:cntrl:]]", "a");
        AssertMatch("[[:upper:]]", "A");
        AssertNoMatch("[[:upper:]]", "a");
        AssertMatch("[[:lower:]]", "a");
        AssertNoMatch("[[:lower:]]", "A");
        AssertMatch("[[:xdigit:]]", "F");
        AssertNoMatch("[[:xdigit:]]", "g");
        AssertMatch("[[:punct:]]", ".");
        AssertNoMatch("[[:punct:]]", "a");
        AssertMatch("[[:graph:]]", "a");
        AssertNoMatch("[[:graph:]]", " ");
        AssertMatch("[[:print:]]", " ");
        AssertNoMatch("[[:print:]]", "\u0001");

        // A negated class, and a class combined with ordinary characters, another class or a wildcard.
        AssertMatch("[![:digit:]]", "a");
        AssertNoMatch("[![:digit:]]", "5");
        AssertMatch("[[:digit:]abc]", "b");
        AssertMatch("[[:digit:]abc]", "5");
        AssertNoMatch("[[:digit:]abc]", "z");
        AssertMatch("[[:upper:][:digit:]]", "A");
        AssertMatch("[[:upper:][:digit:]]", "5");
        AssertNoMatch("[[:upper:][:digit:]]", "a");
        AssertMatch("x[[:digit:]]y", "x5y");
        AssertNoMatch("x[[:digit:]]y", "xay");
        AssertMatch("[[:alpha:]]*", "abc");

        void AssertMatch(string pattern, string path) => Assert.True(Glob.Parse(pattern, dialect).IsMatch(path));
        void AssertNoMatch(string pattern, string path) => Assert.False(Glob.Parse(pattern, dialect).IsMatch(path));
    }

    [Fact]
    public void NamedCharacterClassesAreNotSupportedByTheStandardDialect()
    {
        // The Standard dialect keeps reading '[[:digit:]' as an ordinary bracket expression holding the characters
        // '[', ':', 'd', 'i', 'g', 't' and ']', followed by a literal ']'.
        var glob = Glob.Parse("[[:digit:]]", GlobDialect.Standard);
        Assert.False(glob.IsMatch("5"));
        Assert.True(glob.IsMatch("g]"));
    }

    [Theory]
    // A Posix pattern matches a plain string: it can be empty, and a trailing '/' is part of it.
    [InlineData("*", "")]
    [InlineData("**", "")]
    [InlineData("*", "a")]
    [InlineData("foo/", "foo/")]
    [InlineData("*/", "foo/")]
    [InlineData("foo*", "foo/")]
    public void MatchPosixString(string pattern, string path)
    {
        Assert.True(Glob.Parse(pattern, GlobDialect.Posix).IsMatch(path));
    }

    [Theory]
    [InlineData("?", "")]
    [InlineData("a*", "")]
    [InlineData("*a", "")]
    [InlineData("foo/", "foo")]
    public void DoesNotMatchPosixString(string pattern, string path)
    {
        Assert.False(Glob.Parse(pattern, GlobDialect.Posix).IsMatch(path));
    }

    [Theory]
    [InlineData(GlobDialect.Standard)]
    [InlineData(GlobDialect.PosixPath)]
    public void AnEmptyPathIsNotMatchedByThePathSeparatorAwareDialects(GlobDialect dialect)
    {
        Assert.False(Glob.Parse("*", dialect, GlobOptions.MatchLeadingDot).IsMatch(""));
    }

    [Theory]
    // A gitignore entry ending with a '/' matches the directory itself, at any depth.
    [InlineData("bin/", "", "bin")]
    [InlineData("bin/", "src", "bin")]
    [InlineData("bin/", "src/a/b", "bin")]
    [InlineData("/bin/", "", "bin")]
    public void GitDirectoryPatternMatchesTheDirectoryItself(string pattern, string directory, string filename)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.True(glob.IsMatch(directory, filename, PathItemType.Directory));
        Assert.False(glob.IsMatch(directory, filename, PathItemType.File));
    }

    [Theory]
    // The wildcard has to consume the whole path before the trailing directory entry is given a chance to match
    // what is left, which is nothing.
    [InlineData("**/", "", "bin")]
    [InlineData("**/", "src/a", "bin")]
    [InlineData("*/", "", "bin")]
    [InlineData("**/b*/", "src", "bin")]
    public void GitDirectoryPatternWithAWildcardMatchesTheDirectoryItself(string pattern, string directory, string filename)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.True(glob.IsMatch(directory, filename, PathItemType.Directory));
    }

    [Theory]
    [InlineData("bin/", "bin", "a.txt")]
    [InlineData("bin/", "bin/nested", "a.txt")]
    [InlineData("bin/", "src/bin", "a.txt")]
    [InlineData("/bin/", "bin", "a.txt")]
    public void GitDirectoryPatternMatchesTheDirectoryContent(string pattern, string directory, string filename)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.True(glob.IsMatch(directory, filename, PathItemType.File));
        Assert.True(glob.IsMatch(directory, filename, PathItemType.Directory));
    }

    [Theory]
    [InlineData("bin/", "", "obj")]
    [InlineData("bin/", "", "binary")]
    [InlineData("bin/", "obj", "a.txt")]
    [InlineData("/bin/", "src", "bin")] // anchored to the root
    public void GitDirectoryPatternRejectsOtherPaths(string pattern, string directory, string filename)
    {
        var glob = Glob.Parse(pattern, GlobDialect.Git);
        Assert.False(glob.IsMatch(directory, filename, PathItemType.Directory));
        Assert.False(glob.IsMatch(directory, filename, PathItemType.File));
    }

    [Fact]
    public void GitNegatedDirectoryPatternMatchesTheDirectoryOnly()
    {
        // Excluding a directory excludes its content, but re-including one does not re-include its content.
        var glob = Glob.Parse("!bin/", GlobDialect.Git);

        Assert.True(glob.IsMatch("", "bin", PathItemType.Directory));
        Assert.True(glob.IsMatch("src", "bin", PathItemType.Directory));
        Assert.False(glob.IsMatch("bin", "keep.txt", PathItemType.File));
        Assert.False(glob.IsMatch("bin", "nested", PathItemType.Directory));
    }

    [Fact]
    public void EnumerateFileSystemEntries_GitDirectoryPattern()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("bin/a.txt");
        directory.CreateEmptyFile("src/bin/b.txt");
        directory.CreateEmptyFile("src/c.txt");

        AssertEnumerateFileSystemEntries(directory, Glob.Parse("bin/", GlobDialect.Git), ["bin", "bin/a.txt", "src/bin", "src/bin/b.txt"]);
    }

    [Theory]
    // A folder is only worth visiting when a pattern segment is left to match the file name below it.
    [InlineData("src/*.txt", "src2")]
    [InlineData("src/*.txt", "srcx/a")]
    [InlineData("src/*.txt", "src/archive.txt")]
    [InlineData("src/*.txt", "src/archive.txt/nested")]
    [InlineData("src/a/*.txt", "src/ab")]
    [InlineData("?/a.txt", "ab")]
    public void ShouldNotRecurseIntoAFolderThatCannotContainAMatch(string pattern, string folderPath)
    {
        Assert.False(Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot).IsPartialMatch(folderPath));
        Assert.False(Glob.Parse(pattern, GlobDialect.Standard, GlobOptions.MatchLeadingDot | GlobOptions.IgnoreCase).IsPartialMatch(folderPath));
    }

    [Fact]
    public void EnumerateFiles_DoesNotVisitFoldersThatCannotContainAMatch()
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("src/a.txt");
        directory.CreateEmptyFile("src2/b.txt");
        directory.CreateEmptyFile("src/archive.txt/c.txt");

        using var enumerator = new RecordingGlobFileSystemEnumerator(Glob.Parse("src/*.txt", GlobDialect.Standard), directory.FullPath);

        var files = new List<string>();
        while (enumerator.MoveNext())
        {
            files.Add(MakeRelative(enumerator.Current, directory));
        }

        Assert.Equal(["src/a.txt"], files.Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["src"], enumerator.RecursedDirectories.Select(path => MakeRelative(path, directory)).Order(StringComparer.Ordinal).ToList());
    }

    private static string MakeRelative(string path, TemporaryDirectory directory)
    {
        return FullPath.FromPath(path).MakePathRelativeTo(directory.FullPath).Replace('\\', '/');
    }

    private sealed class RecordingGlobFileSystemEnumerator : GlobFileSystemEnumerator<string>
    {
        public RecordingGlobFileSystemEnumerator(IGlobEvaluatable glob, string directory)
            : base(glob, directory, new EnumerationOptions { RecurseSubdirectories = true })
        {
        }

        public List<string> RecursedDirectories { get; } = [];

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry entry)
        {
            var result = base.ShouldRecurseIntoEntry(ref entry);
            if (result)
            {
                RecursedDirectories.Add(entry.ToFullPath());
            }

            return result;
        }

        protected override string TransformEntry(ref FileSystemEntry entry) => entry.ToFullPath();
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
