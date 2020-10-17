using System;
using System.Linq;
using Xunit;

namespace Meziantou.Framework.Globbing.Tests
{
    public class GlobTests
    {
        [Theory]
        [InlineData("")] // Empty is not valid
        [InlineData("../*.txt")] // Cannot start with '..'
        [InlineData("**/../test")] // Cannot have '..' after a starting '**'
        [InlineData("a\\")] // Cannot ends with the escape character '\'
        [InlineData("{a")] // Missing '}'
        [InlineData("[a")] // Missing ']'
        public void ParseInvalid(string pattern)
        {
            Assert.False(Glob.TryParse(pattern, GlobOptions.None, out var result));
            Assert.Null(result);

            Assert.Throws<ArgumentException>(() => Glob.Parse(pattern, GlobOptions.None));
        }

        [Theory]
        [InlineData("test/*.txt", "test")]
        [InlineData("**/a.txt", "test/a")]
        [InlineData("**/*.txt", "test/a")]
        [InlineData("test/**/a*.txt", "test/a")]
        [InlineData("test/**/a*.txt", "test/a/b/c/d")]
        [InlineData("!test/**/a*.txt", "test/a/b/c/d")]
        public void ShouldRecurse(string pattern, string folderPath)
        {
            var glob = Glob.Parse(pattern, GlobOptions.None);
            var globi = Glob.Parse(pattern, GlobOptions.CaseInsensitive);
            Assert.True(glob.ShouldTraverseFolder(folderPath));
            Assert.True(globi.ShouldTraverseFolder(folderPath));
        }

        [Theory]
        [InlineData("test/*.txt", "titi")]
        [InlineData("test/**/a*.txt", "titi/a")]
        [InlineData("test/**/a*.txt", "titi/b/c/d")]
        public void ShouldNotRecurse(string pattern, string folderPath)
        {
            var glob = Glob.Parse(pattern, GlobOptions.None);
            var globi = Glob.Parse(pattern, GlobOptions.CaseInsensitive);
            Assert.False(glob.ShouldTraverseFolder(folderPath));
            Assert.False(globi.ShouldTraverseFolder(folderPath));
        }

        [Theory]
        [InlineData("a?c", "abc")]
        [InlineData("a?c", "adc")]
        [InlineData("*.txt", ".txt")]
        [InlineData("*.txt", "test.txt")]
        [InlineData(".*", ".gitignore")]
        [InlineData("!*.txt", "a.txt")]
        [InlineData("*/test.txt", "a/test.txt")]
        [InlineData("a/*.txt", "a/test.txt")]
        [InlineData("**/test.txt", "test.txt")]
        [InlineData("**/test.txt", "a/test.txt")]
        [InlineData("**/test.txt", "a/b/test.txt")]
        [InlineData("test/**", "test/a.txt")]
        [InlineData("test/**", "test/a/b/c.txt")]
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
        [InlineData("[--a]", "-")]
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
        public void Match(string pattern, string path)
        {
            var glob = Glob.Parse(pattern, GlobOptions.None);
            var globi = Glob.Parse(pattern, GlobOptions.CaseInsensitive);
            Assert.True(glob.IsMatch(path));
            Assert.True(globi.IsMatch(path));
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
        [InlineData("test/**", "test/a.tXt")]
        [InlineData("test/**", "test/a/B/c.txt")]
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
            var glob = Glob.Parse(pattern, GlobOptions.CaseInsensitive);
            Assert.True(glob.IsMatch(path));
        }

        [Theory]
        [InlineData("a.txt", "a")]
        [InlineData("a.txt", "test.png")]
        [InlineData("a.txt", "test/a.txt")]
        [InlineData("**/*.txt", "test.png")]
        [InlineData("**/*.txt", "a/test.png")]
        [InlineData("**/*.txt", "a/b/test.png")]
        [InlineData("test/*.txt", "test/test.png")]
        [InlineData("test/*.txt", "foo/bar.txt")]
        [InlineData("test/[ab].txt", "test/c.txt")]
        [InlineData("[!a-d]", "a")]
        [InlineData("[!a-d]", "d")]
        public void DoesNotMatch(string pattern, string folderPath)
        {
            var glob = Glob.Parse(pattern, GlobOptions.None);
            var globi = Glob.Parse(pattern, GlobOptions.CaseInsensitive);
            Assert.False(glob.IsMatch(folderPath));
            Assert.False(globi.IsMatch(folderPath));
        }

        [Theory]
        [InlineData(GlobOptions.None)]
        [InlineData(GlobOptions.CaseInsensitive)]
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
        [InlineData(GlobOptions.CaseInsensitive)]
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
        [InlineData(GlobOptions.CaseInsensitive)]
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

        private static void TestEvaluate(TemporaryDirectory directory, Glob glob, string[] expectedResult)
        {
            var items = glob.EnumerateFiles(directory.FullPath)
                .AsEnumerable()
                .Select(path => FullPath.FromPath(path).MakePathRelativeTo(directory.FullPath).Replace('\\', '/'))
                .Sort()
                .ToList();

            Assert.Equal(expectedResult, items);
        }

        private static void TestEvaluate(TemporaryDirectory directory, GlobCollection glob, string[] expectedResult)
        {
            var items = glob.EnumerateFiles(directory.FullPath)
                .AsEnumerable()
                .Select(path => FullPath.FromPath(path).MakePathRelativeTo(directory.FullPath).Replace('\\', '/'))
                .Sort()
                .ToList();

            Assert.Equal(expectedResult, items);
        }
    }
}
