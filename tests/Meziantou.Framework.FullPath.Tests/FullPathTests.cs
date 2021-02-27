using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Meziantou.Framework.Tests
{
    public sealed class FullPathTests
    {
        [Fact]
        public void IsEmpty()
        {
            Assert.True(default(FullPath).IsEmpty);
            Assert.True(FullPath.Empty.IsEmpty);
            Assert.False(FullPath.FromPath("test").IsEmpty);
        }

        [Fact]
        public void CombinePath()
        {
            var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
            var expected = FullPath.Combine("test", "a", "b");
            Assert.Equal(expected, actual);
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

            Assert.Equal(expected, path1.MakePathRelativeTo(rootPath));
        }

        [Theory]
        [InlineData("test", "test/a")]
        [InlineData("test", "test/a.txt")]
        [InlineData("test", "test/b/a.txt")]
        public void IsChildOf_True(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.Combine(root, path);

            Assert.True(childPath.IsChildOf(rootPath));
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

            Assert.False(childPath.IsChildOf(rootPath));
        }

        [Theory]
        [InlineData("test", "abc")]
        [InlineData("test", "../test")]
        [InlineData("test", "test1/b/a.txt")]
        public void Equals_False(string root, string path)
        {
            var rootPath = FullPath.FromPath(root);
            var childPath = FullPath.FromPath(path);

            Assert.NotEqual(childPath, rootPath);
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

            Assert.Equal(childPath, rootPath);
        }

        [Fact]
        public void JsonSerialize_RoundTripEmpty()
        {
            var value = FullPath.Empty;
            Assert.Equal(value, JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(value)));
        }

        [Fact]
        public void JsonSerialize_RoundTripNonEmpty()
        {
            var value = FullPath.FromPath(@"c:\test");
            Assert.Equal(value, JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(value)));
        }

        [Fact]
        public void JsonSerialize_Empty()
        {
            Assert.Equal("\"\"", JsonSerializer.Serialize(FullPath.Empty));
        }

        [Fact]
        public void JsonSerialize_NonEmpty()
        {
            var path = System.Environment.CurrentDirectory;
            Assert.Equal(JsonSerializer.Serialize(path), JsonSerializer.Serialize(FullPath.FromPath(path)));
            Assert.Equal(path, JsonSerializer.Deserialize<FullPath>(JsonSerializer.Serialize(FullPath.FromPath(path))).Value);
        }

        [Fact]
        public void JsonDeserialize_Null()
        {
            Assert.Equal(FullPath.Empty, JsonSerializer.Deserialize<FullPath>(@"null"));
        }

        [Fact]
        public void JsonDeserialize_Empty()
        {
            Assert.Equal(FullPath.Empty, JsonSerializer.Deserialize<FullPath>(@""""""));
        }

        [Fact]
        public void JsonDeserialize_NonEmpty()
        {
            Assert.Equal(FullPath.FromPath(@"c:\test"), JsonSerializer.Deserialize<FullPath>(@"""c:\\test"""));
        }

        [Fact]
        public async Task ResolveSymlink_FileAbsolutePath()
        {
            await using var temp = TemporaryDirectory.Create();
            var path = temp.CreateEmptyFile("a.txt");
            Assert.False(path.IsSymbolicLink());
            Assert.False(path.TryGetSymbolicLinkTarget(out _));

            // Create symlink
            var symlink = temp.GetFullPath("b.txt");
            CreateSymlink(symlink, path, SymbolicLink.File | SymbolicLink.AllowUnpriviledgedCreate);

            Assert.True(File.Exists(symlink), "File does not exist");
            Assert.True(symlink.IsSymbolicLink(), "IsSymbolicLink should be true");
            Assert.True(symlink.TryGetSymbolicLinkTarget(out var target), "TryGetSymbolicLinkTarget should be true");
            Assert.Equal(path, target);
        }

        [Fact]
        public async Task ResolveSymlink_FileRelativePath()
        {
            await using var temp = TemporaryDirectory.Create();
            var path = temp.CreateEmptyFile("a.txt");
            Assert.False(path.IsSymbolicLink());
            Assert.False(path.TryGetSymbolicLinkTarget(out _));

            // Create symlink
            var symlink = temp.GetFullPath("b.txt");
            CreateSymlink(symlink, "a.txt", SymbolicLink.File | SymbolicLink.AllowUnpriviledgedCreate);

            Assert.True(File.Exists(symlink), "File does not exist");
            Assert.True(symlink.IsSymbolicLink(), "IsSymbolicLink should be true");
            Assert.True(symlink.TryGetSymbolicLinkTarget(out var target), "TryGetSymbolicLinkTarget should be true");
            Assert.Equal(path, target);
        }

        [Fact]
        public async Task ResolveSymlink_DirectoryAbsolutePath()
        {
            await using var temp = TemporaryDirectory.Create();
            var path = temp.CreateDirectory("a");
            Assert.False(path.IsSymbolicLink());
            Assert.False(path.TryGetSymbolicLinkTarget(out _));

            // Create symlink
            var symlink = temp.GetFullPath("b");
            CreateSymlink(symlink, path, SymbolicLink.Directory | SymbolicLink.AllowUnpriviledgedCreate);

            Assert.True(Directory.Exists(symlink), "Directory does not exist");
            Assert.True(symlink.IsSymbolicLink(), "IsSymbolicLink should be true");
            Assert.True(symlink.TryGetSymbolicLinkTarget(out var target), "TryGetSymbolicLinkTarget should be true");
            Assert.Equal(path, target);
        }

        [Fact]
        public async Task ResolveSymlink_DirectoryRelativePath()
        {
            await using var temp = TemporaryDirectory.Create();
            var path = temp.CreateDirectory("a");
            Assert.False(path.IsSymbolicLink());
            Assert.False(path.TryGetSymbolicLinkTarget(out _));

            // Create symlink
            var symlink = temp.GetFullPath("b");
            CreateSymlink(symlink, "a", SymbolicLink.Directory | SymbolicLink.AllowUnpriviledgedCreate);

            Assert.True(Directory.Exists(symlink), "Directory does not exist");
            Assert.True(symlink.IsSymbolicLink(), "IsSymbolicLink should be true");
            Assert.True(symlink.TryGetSymbolicLinkTarget(out var target), "TryGetSymbolicLinkTarget should be true");
            Assert.Equal(path, target);
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
#if NETCOREAPP3_1 || NET5_0
            else
            {
                Assert.Equal(0, Mono.Unix.Native.Syscall.symlink(target, source));
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
