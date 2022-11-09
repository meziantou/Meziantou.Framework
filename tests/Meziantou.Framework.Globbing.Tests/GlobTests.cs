using FluentAssertions;
using Xunit;

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
        Glob.TryParse(pattern, GlobOptions.None, out var result).Should().BeFalse();
        result.Should().BeNull();

        new Func<object>(() => Glob.Parse(pattern, GlobOptions.None)).Should().ThrowExactly<ArgumentException>();
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
        var glob = Glob.Parse(pattern, GlobOptions.None);
        var globi = Glob.Parse(pattern, GlobOptions.IgnoreCase);
        glob.IsPartialMatch(folderPath).Should().BeTrue();
        globi.IsPartialMatch(folderPath).Should().BeTrue();
    }

    [Theory]
    [InlineData("test/*.txt", "titi")]
    [InlineData("test/**/a*.txt", "titi/a")]
    [InlineData("test/**/a*.txt", "titi/b/c/d")]
    public void ShouldNotRecurse(string pattern, string folderPath)
    {
        var glob = Glob.Parse(pattern, GlobOptions.None);
        var globi = Glob.Parse(pattern, GlobOptions.IgnoreCase);
        glob.IsPartialMatch(folderPath).Should().BeFalse();
        globi.IsPartialMatch(folderPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("a/b", "a/b")]
    [InlineData("a?c", "abc")]
    [InlineData("a?c", "adc")]
    [InlineData("*.txt", ".txt")]
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
    [InlineData("[!ab]", "c")]
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
    [InlineData("**/*", "a")]
    [InlineData("**/*", "a/b")]
    public void Match(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobOptions.None);
        var globi = Glob.Parse(pattern, GlobOptions.IgnoreCase);
        glob.IsMatch(path).Should().BeTrue();
        glob.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeTrue();

        globi.IsMatch(path).Should().BeTrue();
        globi.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeTrue();

        glob.IsPartialMatch(Path.GetDirectoryName(path)).Should().BeTrue();
        globi.IsPartialMatch(Path.GetDirectoryName(path)).Should().BeTrue();

#if NET472
#else
        if (OperatingSystem.IsWindows())
#endif
        {
            glob.IsMatch(path.Replace('/', '\\')).Should().BeTrue();
            glob.IsMatch(Path.GetDirectoryName(path).Replace('/', '\\'), Path.GetFileName(path)).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("a?c", "a?C")]
    [InlineData("a?c", "adC")]
    [InlineData("*.txt", ".Txt")]
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
    [InlineData("[!ab]", "C")]
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
    [InlineData("\\a", "A")]
    [InlineData("\\[ab\\]", "[Ab]")]
    [InlineData("{a\\,,b}", "A,")]
    [InlineData("{a\\,,b}", "B")]
    public void MatchIgnoreCase(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobOptions.IgnoreCase);
        glob.IsMatch(path).Should().BeTrue();
        glob.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeTrue();
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
    [InlineData("[!a-d]", "a")]
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
    public void DoesNotMatch(string pattern, string path)
    {
        var glob = Glob.Parse(pattern, GlobOptions.None);
        var globi = Glob.Parse(pattern, GlobOptions.IgnoreCase);

        glob.IsMatch(path).Should().BeFalse();
        glob.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeFalse();

        globi.IsMatch(path).Should().BeFalse();
        globi.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeFalse();
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
        var glob = Glob.Parse(pattern, GlobOptions.None);
        glob.IsMatch(path).Should().BeFalse();
        glob.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeFalse();
    }

    [Theory]
    [InlineData(GlobOptions.None)]
    [InlineData(GlobOptions.IgnoreCase)]
    public void EnumerateFolder1(GlobOptions options)
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("d1/d2/f1.txt");
        directory.CreateEmptyFile("d1/d2/f2.txt");
        directory.CreateEmptyFile("d1/f3.txt");
        directory.CreateEmptyFile("d1/f3.png");

        var glob = Glob.Parse("**/*.txt", options);

        TestEvaluate(directory, glob, new[] { "d1/d2/f1.txt", "d1/d2/f2.txt", "d1/f3.txt" });
    }

    [Theory]
    [InlineData(GlobOptions.None)]
    [InlineData(GlobOptions.IgnoreCase)]
    public void EnumerateFolder2(GlobOptions options)
    {
        using var directory = TemporaryDirectory.Create();
        directory.CreateEmptyFile("d1/d2/f1.txt");
        directory.CreateEmptyFile("d1/d2/f2.txt");
        directory.CreateEmptyFile("d1/f3.txt");

        var glob = Glob.Parse("d1/*.txt", options);
        TestEvaluate(directory, glob, new[] { "d1/f3.txt" });
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
            Glob.Parse("**/*.txt", options),
            Glob.Parse("!d1/*.txt", options));

        TestEvaluate(directory, glob, new[]
        {
            "d1/d2/f1.txt",
            "d1/d2/f2.txt",
            "d3/f4.txt",
        });
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
        var glob = Glob.Parse(pattern, GlobOptions.Git);
        var globi = Glob.Parse(pattern, GlobOptions.IgnoreCase | GlobOptions.Git);

        glob.IsMatch(path).Should().BeTrue();
        glob.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeTrue();

        globi.IsMatch(path).Should().BeTrue();
        globi.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeTrue();

        glob.IsPartialMatch(Path.GetDirectoryName(path)).Should().BeTrue();
        globi.IsPartialMatch(Path.GetDirectoryName(path)).Should().BeTrue();

#if NET472
#elif NETCOREAPP3_1
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
#else
        if (OperatingSystem.IsWindows())
#endif
        {
            glob.IsMatch(path.Replace('/', '\\')).Should().BeTrue();
            glob.IsMatch(Path.GetDirectoryName(path).Replace('/', '\\'), Path.GetFileName(path)).Should().BeTrue();
        }
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
        var glob = Glob.Parse(pattern, GlobOptions.Git);
        var globi = Glob.Parse(pattern, GlobOptions.IgnoreCase | GlobOptions.Git);

        glob.IsMatch(path).Should().BeFalse();
        glob.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeFalse();

        globi.IsMatch(path).Should().BeFalse();
        globi.IsMatch(Path.GetDirectoryName(path), Path.GetFileName(path)).Should().BeFalse();
    }

    private static void TestEvaluate(TemporaryDirectory directory, Glob glob, string[] expectedResult)
    {
        var items = glob.EnumerateFiles(directory.FullPath)
            .AsEnumerable()
            .Select(path => FullPath.FromPath(path).MakePathRelativeTo(directory.FullPath).Replace('\\', '/'))
            .OrderBy(x => x)
            .ToList();

        items.Should().Equal(expectedResult);
    }

    private static void TestEvaluate(TemporaryDirectory directory, GlobCollection glob, string[] expectedResult)
    {
        var items = glob.EnumerateFiles(directory.FullPath)
            .AsEnumerable()
            .Select(path => FullPath.FromPath(path).MakePathRelativeTo(directory.FullPath).Replace('\\', '/'))
            .OrderBy(x => x)
            .ToList();

        items.Should().Equal(expectedResult);
    }
}
