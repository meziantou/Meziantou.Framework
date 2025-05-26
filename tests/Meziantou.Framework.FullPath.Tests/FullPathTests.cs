using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using TestUtilities;
using Xunit;

namespace Meziantou.Framework.Tests;

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
    public void Properties()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";
        Assert.False(path.IsEmpty);
        Assert.Equal("b.txt", path.Name);
        Assert.Equal(".txt", path.Extension);
        Assert.Equal("b", path.NameWithoutExtension);
        Assert.Equal(FullPath.FromPath("test") / "a", path.Parent);
    }


    [Fact]
    public void CombinePath()
    {
        var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
        var expected = FullPath.Combine("test", "a", "b");
        Assert.Equal(expected, actual);
    }

#if NET9_0_OR_GREATER
    [Fact]
    public void CombinePath_ReadOnlySpan()
    {
        var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
        var expected = FullPath.Combine(FullPath.FromPath("test"), (ReadOnlySpan<string>)["a", "b"]);
        Assert.Equal(expected, actual);
    }
#endif

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
        var path = Environment.CurrentDirectory;
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
        Assert.True(File.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
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
        Assert.True(File.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
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
        Assert.True(Directory.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
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
        Assert.True(Directory.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
        Assert.Equal(path, target);
    }

    [Fact]
    public async Task ResolveSymlink_Recursive()
    {
        await using var temp = TemporaryDirectory.Create();
        var file = temp.CreateEmptyFile("a/b.txt");
        var symlink = temp.GetFullPath("c");
        var symlink2 = temp.GetFullPath("d");
        CreateSymlink(symlink, file, SymbolicLink.AllowUnpriviledgedCreate);
        CreateSymlink(symlink2, symlink, SymbolicLink.AllowUnpriviledgedCreate);
        Assert.True(symlink2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.Immediate, out var resolved1));
        Assert.Equal(symlink, resolved1);
        Assert.True(symlink2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out var resolved2));
        Assert.EndsWith(Path.Combine("a", "b.txt"), resolved2.Value.Value, StringComparison.Ordinal); // On GitHub Actions, path starts with a symlink, so resolved2 != file
    }

    [Fact]
    public async Task ResolveSymlink_ResolveAllSymbolicLinks()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateDirectory("a/b");
        var symlink = temp.GetFullPath("c");
        CreateSymlink(symlink, path, SymbolicLink.Directory | SymbolicLink.AllowUnpriviledgedCreate);
        var file = temp.CreateEmptyFile("c/d.txt");
        Assert.True(file.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out var resolved));

        Assert.EndsWith(Path.Combine("a", "b", "d.txt"), resolved.Value.Value, StringComparison.Ordinal); // On GitHub Actions, path starts with a symlink, so resolved2 != file
    }

    [Fact]
    public void ChangeExtension()
    {
        var actual = FullPath.FromPath("test.a.txt").ChangeExtension(".avi");
        var expected = FullPath.FromPath("test.a.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_NoExtension()
    {
        var actual = FullPath.FromPath("test").ChangeExtension(".avi");
        var expected = FullPath.FromPath("test.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_Empty()
    {
        var actual = FullPath.Empty.ChangeExtension(".avi");
        var expected = FullPath.Empty;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_Null()
    {
        var actual = FullPath.FromPath("test").ChangeExtension(null);
        var expected = FullPath.FromPath("test");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_NoDot()
    {
        var actual = FullPath.FromPath("test.txt").ChangeExtension("avi");
        var expected = FullPath.FromPath("test.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TryFindFirstAncestorOrSelf()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var fileName = Guid.NewGuid().ToString("N");
        var filePath = tempDir.CreateEmptyFile(fileName);

        Assert.False(tempDir.FullPath.TryFindFirstAncestorOrSelf(p => false, out _));

        Assert.True(tempDir.FullPath.TryFindFirstAncestorOrSelf(p => File.Exists(p / fileName), out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public async Task TryFindFirstAncestorOrSelf_Depth()
    {
        await using var tempDir = TemporaryDirectory.Create();
        var fileName = Guid.NewGuid().ToString("N");
        var filePath = tempDir.CreateEmptyFile(fileName);
        var subDir = tempDir.CreateDirectory("a/b/c/d/e");

        Assert.False(subDir.TryFindFirstAncestorOrSelf(p => false, out _));

        Assert.True(subDir.TryFindFirstAncestorOrSelf(p => File.Exists(p / fileName), out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    [RunIf(FactOperatingSystem.Windows)]
    public void ShellFolderTest()
    {
        var fullPath = FullPath.GetShellFolderPath(ShellFolder.Downloads);
        Assert.NotEmpty(fullPath.Value);
    }

    private static bool IsWindows()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    private static void CreateSymlink(string source, string target, SymbolicLink options)
    {
        if (IsWindows())
        {
            if (!CreateSymbolicLink(source, target, options))
            {
                var error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "Cannot create the symbolic link. You may need to enable Developer Mode or run the tests as admin.");
            }
        }
#if NETCOREAPP3_1_OR_GREATER
        else
        {
            Assert.Equal(0, Mono.Unix.Native.Syscall.symlink(target, source));
        }
#endif
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.I1)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, SymbolicLink dwFlags);

    private enum SymbolicLink
    {
        File = 0,
        Directory = 1,
        AllowUnpriviledgedCreate = 2,
    }
}
