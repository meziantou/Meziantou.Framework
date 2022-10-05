using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Tests;

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

    [Fact]
    public async Task ResolveSymlink_Recursive()
    {
        await using var temp = TemporaryDirectory.Create();
        var file = temp.CreateEmptyFile("a/b.txt");
        var symlink = temp.GetFullPath("c");
        var symlink2 = temp.GetFullPath("d");
        CreateSymlink(symlink, file, SymbolicLink.AllowUnpriviledgedCreate);
        CreateSymlink(symlink2, symlink, SymbolicLink.AllowUnpriviledgedCreate);

        symlink2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.Immediate, out var resolved1).Should().BeTrue();
        resolved1.Should().Be(symlink);

        symlink2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out var resolved2).Should().BeTrue();
        resolved2.Value.Value.Should().EndWith(Path.Combine("a", "b.txt")); // On GitHub Actions, path starts with a symlink, so resolved2 != file
    }

    [Fact]
    public async Task ResolveSymlink_ResolveAllSymbolicLinks()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateDirectory("a/b");
        var symlink = temp.GetFullPath("c");
        CreateSymlink(symlink, path, SymbolicLink.Directory | SymbolicLink.AllowUnpriviledgedCreate);
        var file = temp.CreateEmptyFile("c/d.txt");

        file.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out var resolved).Should().BeTrue();

        resolved.Value.Value.Should().EndWith(Path.Combine("a", "b", "d.txt")); // On GitHub Actions, path starts with a symlink, so resolved2 != file
    }

    [Fact]
    public void ChangeExtension()
    {
        var actual = FullPath.FromPath("test.a.txt").ChangeExtension(".avi");
        var expected = FullPath.Combine("test.a.avi");
        actual.Should().Be(expected);
    }

    [Fact]
    public void ChangeExtension_NoExtension()
    {
        var actual = FullPath.FromPath("test").ChangeExtension(".avi");
        var expected = FullPath.Combine("test.avi");
        actual.Should().Be(expected);
    }

    [Fact]
    public void ChangeExtension_Empty()
    {
        var actual = FullPath.Empty.ChangeExtension(".avi");
        var expected = FullPath.Empty;
        actual.Should().Be(expected);
    }

    [Fact]
    public void ChangeExtension_Null()
    {
        var actual = FullPath.FromPath("test").ChangeExtension(null);
        var expected = FullPath.Combine("test");
        actual.Should().Be(expected);
    }

    [Fact]
    public void ChangeExtension_NoDot()
    {
        var actual = FullPath.FromPath("test.txt").ChangeExtension("avi");
        var expected = FullPath.Combine("test.avi");
        actual.Should().Be(expected);
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
            Mono.Unix.Native.Syscall.symlink(target, source).Should().Be(0);
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
