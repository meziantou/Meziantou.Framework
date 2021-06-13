using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;
using FluentAssertions;

namespace Meziantou.Framework.Tests
{
    public sealed class FullPathTests
    {
        [Fact]
        public void IsEmpty()
        {
            default(FullPath).IsEmpty.Should().BeTrue();
            FullPath.Empty.IsEmpty.Should().BeTrue();
            FullPath.FromPath("test").IsEmpty.Should().BeFalse();
        }

        [Fact]
        public void CombinePath()
        {
            var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
            var expected = FullPath.Combine("test", "a", "b");
            actual.Should().Be(expected);
        }

        [Theory]
        [InlineData("a", "a")]
        [InlineData("a b", "a b")]
        [InlineData("a/", "a")]
        [InlineData("a/../b", "b")]
        [InlineData(".", ".")]
        public void MakeRelativeTo(string childPath, string expected)
        {
            var rootPath = FullPath.FromPath("test");
            var path1 = FullPath.Combine("test", childPath);

            path1.MakePathRelativeTo(rootPath).Should().Be(expected);
        }

        [Theory]
        [InlineData("test", "test/a")]
        [InlineData("test", "test/a.txt")]
        [InlineData("test", "test/b/a.txt")]
        public void IsChildOf_True(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.Combine(root, path);

            childPath.IsChildOf(rootPath).Should().BeTrue();
        }

        [Theory]
        [InlineData("test/", "test")]
        [InlineData("test/", "test/")]
        [InlineData("test", "test")]
        [InlineData("test", "test/")]
        [InlineData("test", "abc")]
        [InlineData("test", "../test")]
        [InlineData("test", "test1/b/a.txt")]
        public void IsChildOf_False(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.FromPath(path);

            childPath.IsChildOf(rootPath).Should().BeFalse();
        }

        [Theory]
        [InlineData("test", "abc")]
        [InlineData("test", "../test")]
        [InlineData("test", "test1/b/a.txt")]
        public void Equals_False(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.FromPath(path);

            rootPath.Should().NotBe(childPath);
        }

        [Theory]
        [InlineData("test/", "test")]
        [InlineData("test/", "test/")]
        [InlineData("test", "test")]
        [InlineData("test", "test/")]
        [InlineData("test", "./test")]
        public void Equals_True(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.FromPath(path);

            rootPath.Should().Be(childPath);
        }

        [Fact]
        public void JsonSerialize_RoundTripEmpty()
        {
            var value = FullPath.Empty;
            JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(value)).Should().Be(value);
        }

        [Fact]
        public void JsonSerialize_RoundTripNonEmpty()
        {
            var value = FullPath.FromPath(@"c:\test");
            JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(value)).Should().Be(value);
        }

        [Fact]
        public void JsonSerialize_Empty()
        {
            JsonSerializer.Serialize(FullPath.Empty).Should().Be("\"\"");
        }

        [Fact]
        public void JsonSerialize_NonEmpty()
        {
            var path = System.Environment.CurrentDirectory;
            JsonSerializer.Serialize(FullPath.FromPath(path)).Should().Be(JsonSerializer.Serialize(path));
            JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(FullPath.FromPath(path))).Value.Should().Be(path);
        }

        [Fact]
        public void JsonDeserialize_Null()
        {
            JsonSerializer.Deserialize<FullPath>(@"null").Should().Be(FullPath.Empty);
        }

        [Fact]
        public void JsonDeserialize_Empty()
        {
            JsonSerializer.Deserialize<FullPath>(@"""""").Should().Be(FullPath.Empty);
        }

        [Fact]
        public void JsonDeserialize_NonEmpty()
        {
            JsonSerializer.Deserialize<FullPath>(@"""c:\\test""").Should().Be(FullPath.FromPath(@"c:\test"));
        }

        [Fact]
        public async Task ResolveSymlink_FileAbsolutePath()
        {
            await using var temp = TemporaryDirectory.Create();
            var path = temp.CreateEmptyFile("a.txt");
            path.IsSymbolicLink().Should().BeFalse();
            path.TryGetSymbolicLinkTarget(out _).Should().BeFalse();

            // Create symlink
            var symlink = temp.GetFullPath("b.txt");
            CreateSymlink(symlink, path, SymbolicLink.File | SymbolicLink.AllowUnpriviledgedCreate);

            File.Exists(symlink).Should().BeTrue("File does not exist");
            symlink.IsSymbolicLink().Should().BeTrue("IsSymbolicLink should be true");
            symlink.TryGetSymbolicLinkTarget(out var target).Should().BeTrue("TryGetSymbolicLinkTarget should be true");
            target.Should().Be(path);
        }

        [Fact]
        public async Task ResolveSymlink_FileRelativePath()
        {
            await using var temp = TemporaryDirectory.Create();
            var path = temp.CreateEmptyFile("a.txt");
            path.IsSymbolicLink().Should().BeFalse();
            path.TryGetSymbolicLinkTarget(out _).Should().BeFalse();

            // Create symlink
            var symlink = temp.GetFullPath("b.txt");
            CreateSymlink(symlink, "a.txt", SymbolicLink.File | SymbolicLink.AllowUnpriviledgedCreate);

            File.Exists(symlink).Should().BeTrue("File does not exist");
            symlink.IsSymbolicLink().Should().BeTrue("IsSymbolicLink should be true");
            symlink.TryGetSymbolicLinkTarget(out var target).Should().BeTrue("TryGetSymbolicLinkTarget should be true");
            target.Should().Be(path);
        }

        [Fact]
        public async Task ResolveSymlink_DirectoryAbsolutePath()
        {
            await using var temp = TemporaryDirectory.Create();
            var path = temp.CreateDirectory("a");
            path.IsSymbolicLink().Should().BeFalse();
            path.TryGetSymbolicLinkTarget(out _).Should().BeFalse();

            // Create symlink
            var symlink = temp.GetFullPath("b");
            CreateSymlink(symlink, path, SymbolicLink.Directory | SymbolicLink.AllowUnpriviledgedCreate);

            Directory.Exists(symlink).Should().BeTrue("Directory should exist");
            symlink.IsSymbolicLink().Should().BeTrue("IsSymbolicLink should be true");
            symlink.TryGetSymbolicLinkTarget(out var target).Should().BeTrue("TryGetSymbolicLinkTarget should be true");
            target.Should().Be(path);
        }

        [Fact]
        public async Task ResolveSymlink_DirectoryRelativePath()
        {
            await using var temp = TemporaryDirectory.Create();
            var path = temp.CreateDirectory("a");
            path.IsSymbolicLink().Should().BeFalse();
            path.TryGetSymbolicLinkTarget(out _).Should().BeFalse();

            // Create symlink
            var symlink = temp.GetFullPath("b");
            CreateSymlink(symlink, "a", SymbolicLink.Directory | SymbolicLink.AllowUnpriviledgedCreate);

            Directory.Exists(symlink).Should().BeTrue("Directory does not exist");
            symlink.IsSymbolicLink().Should().BeTrue("IsSymbolicLink should be true");
            symlink.TryGetSymbolicLinkTarget(out var target).Should().BeTrue("TryGetSymbolicLinkTarget should be true");
            target.Should().Be(path);
        }

        private static bool IsWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        private static void CreateSymlink(string source, string target, SymbolicLink options)
        {
            if (IsWindows())
            {
                CreateSymbolicLink(source, target, options);
            }
#if NETCOREAPP3_1 || NET5_0 || NET6_0
            else
            {
                Mono.Unix.Native.Syscall.symlink(target, source).Should().Be(0);
            }
#endif
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

        private enum SymbolicLink
        {
            File = 0,
            Directory = 1,
            AllowUnpriviledgedCreate = 2,
        }
    }
}
