using System.Text.Json;
using Meziantou.Xunit;

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
    public void FromPath_EmptyString_ReturnsEmpty()
    {
        Assert.True(FullPath.FromPath("").IsEmpty);
        Assert.Equal(FullPath.Empty, FullPath.FromPath(""));
        Assert.Equal(FullPath.Empty, FullPath.FromPath(string.Empty));
    }

    [Fact]
    public void FromPath_EmptyString_IsNotTheCurrentDirectory()
    {
        Assert.NotEqual(FullPath.CurrentDirectory(), FullPath.FromPath(""));
        Assert.Equal(FullPath.CurrentDirectory(), FullPath.FromPath("."));
    }

    [Fact]
    public void FromPath_RoundTripsTheStringRepresentation()
    {
        Assert.Equal(FullPath.Empty, FullPath.FromPath(FullPath.Empty.Value));

        var path = FullPath.FromPath("test");
        Assert.Equal(path, FullPath.FromPath(path.Value));
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
    public void AddOperator()
    {
        var actual = FullPath.FromPath("test") + "a" + "b.txt";
        var expected = FullPath.FromPath("testab.txt");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeName()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";
        var newPath = path.WithName("c.txt");
        Assert.Equal(FullPath.FromPath("test") / "a" / "c.txt", newPath);
    }

    [Fact]
    public void ChangeNameWithoutExtension()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";
        var newPath = path.WithNameWithoutExtension("c");
        Assert.Equal(FullPath.FromPath("test") / "a" / "c.txt", newPath);
    }

    [Fact]
    public void ChangeName_NormalizesTheResultingPath()
    {
        var root = FullPath.FromPath("test");
        var path = root / "a" / "b.txt";

        var newPath = path.WithName("../c.txt");

        Assert.Equal(root / "c.txt", newPath);
        Assert.Equal(FullPath.FromPath(newPath.RawValue), newPath);
    }

    [Fact]
    public void ChangeName_EscapingTheRootIsNotReportedAsAChild()
    {
        var root = FullPath.FromPath("test");
        var path = root / "a" / "b.txt";

        var newPath = path.WithName("../../../../etc/passwd");

        Assert.False(newPath.IsChildOf(root));
        Assert.Equal(FullPath.FromPath(newPath.RawValue), newPath);
    }

    [Fact]
    public void ChangeName_RootDirectory()
    {
        var root = GetRootDirectory();

        var newPath = root.WithName("temp");

        Assert.Equal(root / "temp", newPath);
    }

    [Fact]
    public void ChangeName_EmbeddedNullCharacterIsRejected()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";

        Assert.Throws<ArgumentException>(() => path.WithName("a\0b"));
    }

    [Fact]
    public void ChangeNameWithoutExtension_NormalizesTheResultingPath()
    {
        var root = FullPath.FromPath("test");
        var path = root / "a" / "b.txt";

        var newPath = path.WithNameWithoutExtension("../c");

        Assert.Equal(root / "c.txt", newPath);
    }

    [Fact]
    public void ChangeNameWithoutExtension_RootDirectory()
    {
        var root = GetRootDirectory();

        var newPath = root.WithNameWithoutExtension("temp");

        Assert.Equal(root / "temp", newPath);
    }

    [Fact]
    public void ChangeExtension_NormalizesTheResultingPath()
    {
        var root = FullPath.FromPath("test");
        var path = root / "a" / "b.txt";

        var newPath = path.WithExtension("/../../c");

        Assert.Equal(FullPath.FromPath(newPath.RawValue), newPath);
        Assert.False(newPath.IsChildOf(root / "a"));
    }

    [Fact]
    public void ChangeExtension_DotFileKeepsNoTrailingSeparator()
    {
        var parent = FullPath.FromPath("test") / "a";
        var path = parent / ".gitignore";

        var newPath = path.WithExtension(null);

        Assert.Equal(parent, newPath);
        Assert.Equal(parent.RawValue, newPath.RawValue);
    }

    [Fact]
    public void ChangeMultipleExtensions_DotFileKeepsNoTrailingSeparator()
    {
        var parent = FullPath.FromPath("test") / "a";
        var path = parent / ".gitignore";

        var newPath = path.WithExtension(null, replaceAllTrailingExtensions: true);

        Assert.Equal(parent, newPath);
        Assert.Equal(parent.RawValue, newPath.RawValue);
    }

    [Fact]
    public void ChangeMultipleExtensions()
    {
        var path = FullPath.FromPath("test") / "a" / "b.tar.gz";
        var newPath = path.WithExtension(".zip", replaceAllTrailingExtensions: true);
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.zip", newPath);
    }

    [Fact]
    public void ChangeExtensionWithMultipleDots()
    {
        var path = FullPath.FromPath("test") / "a" / "b.tar.gz";
        var newPath = path.WithExtension(".zip", extensionCount: 2);
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.zip", newPath);
    }

    [Fact]
    public void ChangeMultipleExtensions_WithCount()
    {
        var path = FullPath.FromPath("test") / "a" / "b.tar.gz";
        var newPath = path.WithExtension(".zip", extensionCount: 1);
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.tar.zip", newPath);
    }

    [Fact]
    public void ChangeMultipleExtensions_NoExtension()
    {
        var path = FullPath.FromPath("test") / "a" / "b";
        var newPath = path.WithExtension(".zip", replaceAllTrailingExtensions: true);
        Assert.Equal(FullPath.FromPath("test") / "a" / "b.zip", newPath);
    }

    [Fact]
    public void ChangeMultipleExtensions_InvalidCount()
    {
        var path = FullPath.FromPath("test") / "a" / "b.tar.gz";
        Assert.Throws<ArgumentOutOfRangeException>(() => path.WithExtension(".zip", extensionCount: 0));
    }

    [Fact]
    public void CombinePath()
    {
        var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
        var expected = FullPath.Combine("test", "a", "b");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CombinePath_ReadOnlySpan()
    {
        var actual = FullPath.FromPath("test") / "a" / ".." / "a" / "." / "b";
        var expected = FullPath.Combine(FullPath.FromPath("test"), (ReadOnlySpan<string>)["a", "b"]);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Combine_AllOverloadsAgree()
    {
        var root = FullPath.FromPath("test");
        var expected = root / "a" / "b" / "c";
        string[] parts = ["a", "b", "c"];
        string[] rootedParts = ["test", "a", "b", "c"];

        Assert.Equal(expected, FullPath.Combine("test", "a", "b", "c"));
        Assert.Equal(expected, FullPath.Combine(rootedParts));
        Assert.Equal(expected, FullPath.Combine((ReadOnlySpan<string>)rootedParts));
        Assert.Equal(expected, FullPath.Combine(root, "a", "b", "c"));
        Assert.Equal(expected, FullPath.Combine(root, parts));
        Assert.Equal(expected, FullPath.Combine(root, (ReadOnlySpan<string>)parts));
    }

    [Fact]
    public void Combine_ShorterOverloadsAgree()
    {
        var root = FullPath.FromPath("test");

        Assert.Equal(root / "a", FullPath.Combine("test", "a"));
        Assert.Equal(root / "a", FullPath.Combine(root, "a"));
        Assert.Equal(root / "a" / "b", FullPath.Combine("test", "a", "b"));
        Assert.Equal(root / "a" / "b", FullPath.Combine(root, "a", "b"));
    }

    [Fact]
    public void Combine_EmptyRootResolvesAgainstTheCurrentDirectory()
    {
        // Documents current behaviour: every FullPath-rooted overload falls back to FromPath, which resolves a
        // relative path against Environment.CurrentDirectory rather than propagating Empty or throwing.
        var cwd = FullPath.CurrentDirectory();
        string[] parts = ["a", "b"];

        Assert.Equal(cwd / "a", FullPath.Combine(FullPath.Empty, "a"));
        Assert.Equal(cwd / "a" / "b", FullPath.Combine(FullPath.Empty, "a", "b"));
        Assert.Equal(cwd / "a" / "b" / "c", FullPath.Combine(FullPath.Empty, "a", "b", "c"));
        Assert.Equal(cwd / "a" / "b", FullPath.Combine(FullPath.Empty, parts));
        Assert.Equal(cwd / "a" / "b", FullPath.Combine(FullPath.Empty, (ReadOnlySpan<string>)parts));
        Assert.Equal(cwd / "a", FullPath.Empty / "a");
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("a b", "a b")]
    [InlineData("a/", "a")]
    [InlineData("a/../b", "b")]
    [InlineData(".", ".")]
    [InlineData("..", "..")]
    [InlineData("../..", "../..")]
    [InlineData("../sibling", "../sibling")]
    public void MakeRelativeTo(string childPath, string expected)
    {
        var rootPath = FullPath.FromPath("test");
        var path1 = FullPath.Combine("test", childPath);
        Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), path1.MakePathRelativeTo(rootPath));
    }

    [Fact]
    public void MakeRelativeTo_RootDirectory()
    {
        var rootPath = GetRootDirectory();
        var path = rootPath / "a";

        Assert.Equal("a", path.MakePathRelativeTo(rootPath));
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

    [Fact]
    public void IsChildOf_RootDirectory()
    {
        var rootPath = GetRootDirectory();

        Assert.True((rootPath / "a").IsChildOf(rootPath));
        Assert.True((rootPath / "a" / "b.txt").IsChildOf(rootPath));
        Assert.False(rootPath.IsChildOf(rootPath));
    }

    [Fact]
    public void IsChildOf_UsesTheSameCaseSensitivityAsTheDefaultComparer()
    {
        var rootPath = FullPath.FromPath("test");
        var childPath = FullPath.FromPath("TEST") / "a.txt";

        Assert.Equal(childPath.Parent == rootPath, childPath.IsChildOf(rootPath));
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
    public void FullPathComparer_CaseSensitive()
    {
        var lower = FullPath.FromPath("test") / "a.txt";
        var upper = FullPath.FromPath("test") / "A.TXT";

        Assert.True(FullPathComparer.CaseSensitive.IsCaseSensitive);
        Assert.False(FullPathComparer.CaseSensitive.Equals(lower, upper));
        Assert.NotEqual(0, FullPathComparer.CaseSensitive.Compare(lower, upper));
    }

    [Fact]
    public void FullPathComparer_CaseInsensitive()
    {
        var lower = FullPath.FromPath("test") / "a.txt";
        var upper = FullPath.FromPath("test") / "A.TXT";

        Assert.False(FullPathComparer.CaseInsensitive.IsCaseSensitive);
        Assert.True(FullPathComparer.CaseInsensitive.Equals(lower, upper));
        Assert.Equal(0, FullPathComparer.CaseInsensitive.Compare(lower, upper));
        Assert.Equal(FullPathComparer.CaseInsensitive.GetHashCode(lower), FullPathComparer.CaseInsensitive.GetHashCode(upper));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows | TestOperatingSystems.MacOS)]
    public void FullPathComparer_DefaultIgnoresCaseOnWindowsAndMacOS()
    {
        // Compared through the comparer rather than Assert.Equal: FullPath converts implicitly to string, so
        // Assert.Equal would bind to the string overload and compare ordinally instead of using FullPath equality.
        Assert.False(FullPathComparer.Default.IsCaseSensitive);
        Assert.True(FullPathComparer.Default.Equals(FullPath.FromPath("test") / "a.txt", FullPath.FromPath("test") / "A.TXT"));
        Assert.True((FullPath.FromPath("test") / "a.txt") == (FullPath.FromPath("test") / "A.TXT"));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux)]
    public void FullPathComparer_DefaultIsCaseSensitiveOnLinux()
    {
        Assert.True(FullPathComparer.Default.IsCaseSensitive);
        Assert.False(FullPathComparer.Default.Equals(FullPath.FromPath("test") / "a.txt", FullPath.FromPath("test") / "A.TXT"));
        Assert.False((FullPath.FromPath("test") / "a.txt") == (FullPath.FromPath("test") / "A.TXT"));
    }

    [Fact]
    public void FullPathComparer_Compare()
    {
        var a = FullPath.FromPath("test") / "a.txt";
        var b = FullPath.FromPath("test") / "b.txt";

        Assert.True(FullPathComparer.CaseSensitive.Compare(a, b) < 0);
        Assert.True(FullPathComparer.CaseSensitive.Compare(b, a) > 0);
        Assert.Equal(0, FullPathComparer.CaseSensitive.Compare(a, a));
    }

    [Fact]
    public void FullPathComparer_Empty()
    {
        Assert.Equal(0, FullPathComparer.Default.GetHashCode(FullPath.Empty));
        Assert.Equal(0, FullPathComparer.CaseSensitive.GetHashCode(FullPath.Empty));
        Assert.Equal(0, FullPathComparer.CaseInsensitive.GetHashCode(FullPath.Empty));
        Assert.True(FullPathComparer.Default.Equals(FullPath.Empty, FullPath.Empty));
        Assert.False(FullPathComparer.Default.Equals(FullPath.Empty, FullPath.FromPath("test")));
    }

    [Fact]
    public void EqualsAndCompareTo_IgnoreCase()
    {
        var lower = FullPath.FromPath("test") / "a.txt";
        var upper = FullPath.FromPath("test") / "A.TXT";

        Assert.True(lower.Equals(upper, ignoreCase: true));
        Assert.False(lower.Equals(upper, ignoreCase: false));
        Assert.Equal(lower.GetHashCode(ignoreCase: true), upper.GetHashCode(ignoreCase: true));
        Assert.Equal(0, lower.CompareTo(upper, ignoreCase: true));
        Assert.NotEqual(0, lower.CompareTo(upper, ignoreCase: false));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void FromPath_ExtendedPrefixIsRemovedOnWindows()
    {
        Assert.Equal(@"C:\temp\a.txt", FullPath.FromPath(@"\\?\C:\temp\a.txt").RawValue);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void FromPath_ExtendedUncPrefixIsConvertedBackToAUncPath()
    {
        Assert.Equal(@"\\server\share\folder\file.txt", FullPath.FromPath(@"\\?\UNC\server\share\folder\file.txt").RawValue);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void FromPath_ExtendedPrefixIsAFileNameOnUnix()
    {
        // '\' is a regular file name character on Unix, so this is a relative file name, not a device path
        var expected = FullPath.CurrentDirectory() / @"\\?\a";

        Assert.Equal(expected, FullPath.FromPath(@"\\?\a"));
        Assert.Equal(@"\\?\a", FullPath.FromPath(@"\\?\a").Name);
    }

    [Fact]
    public void JsonDeserialize_InvalidPathThrowsJsonException()
    {
        var exception = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<FullPath>("\"a\\u0000b\""));

        Assert.IsAssignableTo<ArgumentException>(exception.InnerException);
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
    public void IComparable_CompareTo()
    {
        IComparable path = FullPath.FromPath("test") / "a";

        Assert.Equal(0, path.CompareTo(FullPath.FromPath("test") / "a"));
        Assert.True(path.CompareTo(FullPath.FromPath("test") / "b") < 0);
        Assert.True(path.CompareTo(null) > 0);
        Assert.Throws<ArgumentException>(() => path.CompareTo("test"));
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
        CreateSymlink(symlink, path, isDirectory: false);
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
        CreateSymlink(symlink, "a.txt", isDirectory: false);
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
        CreateSymlink(symlink, path, isDirectory: true);
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
        CreateSymlink(symlink, "a", isDirectory: true);
        Assert.True(Directory.Exists(symlink));
        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
        Assert.Equal(path, target);
    }

    [Fact]
    public async Task ResolveSymlink_NonAsciiPath()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateEmptyFile("日本語.txt");

        var symlink = temp.GetFullPath("リンク.txt");
        CreateSymlink(symlink, "日本語.txt", isDirectory: false);

        Assert.True(symlink.IsSymbolicLink());
        Assert.True(symlink.TryGetSymbolicLinkTarget(out var target));
        Assert.Equal(path, target);

        Assert.True(path.TryGetCanonicalPath(out var expected));
        Assert.True(symlink.TryGetCanonicalPath(out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ResolveSymlink_Recursive()
    {
        await using var temp = TemporaryDirectory.Create();
        var file = temp.CreateEmptyFile("a/b.txt");
        var symlink = temp.GetFullPath("c");
        var symlink2 = temp.GetFullPath("d");
        CreateSymlink(symlink, file, isDirectory: false);
        CreateSymlink(symlink2, symlink, isDirectory: false);
        Assert.True(symlink2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.Immediate, out var resolved1));
        Assert.Equal(symlink, resolved1);
        Assert.True(symlink2.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out var resolved2));
        Assert.EndsWith(Path.Combine("a", "b.txt"), resolved2.Value.Value); // On GitHub Actions, path starts with a symlink, so resolved2 != file
    }

    [Fact]
    public async Task ResolveSymlink_Cycle()
    {
        await using var temp = TemporaryDirectory.Create();
        var a = temp.GetFullPath("a");
        var b = temp.GetFullPath("b");
        CreateSymlink(a, "b", isDirectory: false);
        CreateSymlink(b, "a", isDirectory: false);

        Assert.True(a.IsSymbolicLink());
        Assert.True(a.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.Immediate, out var immediate));
        Assert.Equal(b, immediate);

        Assert.Throws<IOException>(() => { a.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.FinalTarget, out _); });
        Assert.Throws<IOException>(() => { a.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out _); });
    }

    [Fact]
    public async Task ResolveSymlink_CycleThroughADirectoryLink()
    {
        // The links reference each other through a directory component, so the walk alternates between resolving a
        // link and consuming a component that is not one
        await using var temp = TemporaryDirectory.Create();
        var a = temp.GetFullPath("a");
        var b = temp.GetFullPath("b");
        CreateSymlink(a, Path.Combine("b", "c"), isDirectory: true);
        CreateSymlink(b, "a", isDirectory: true);

        Assert.Throws<IOException>(() => { a.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out _); });
    }

    [Fact]
    public async Task ResolveSymlink_ResolveAllSymbolicLinks()
    {
        await using var temp = TemporaryDirectory.Create();
        var path = temp.CreateDirectory("a/b");
        var symlink = temp.GetFullPath("c");
        CreateSymlink(symlink, path, isDirectory: true);
        var file = temp.CreateEmptyFile("c/d.txt");
        Assert.True(file.TryGetSymbolicLinkTarget(SymbolicLinkResolutionMode.AllSymbolicLinks, out var resolved));

        Assert.EndsWith(Path.Combine("a", "b", "d.txt"), resolved.Value.Value); // On GitHub Actions, path starts with a symlink, so resolved2 != file
    }

    [Fact]
    public async Task TryGetCanonicalPath_File()
    {
        await using var temp = TemporaryDirectory.Create();
        var file = temp.CreateEmptyFile("a.txt");

        Assert.True(file.TryGetCanonicalPath(out var canonicalPath));
        Assert.True(File.Exists(canonicalPath));
    }

    [Fact]
    public async Task TryGetCanonicalPath_SymbolicLink()
    {
        await using var temp = TemporaryDirectory.Create();
        var target = temp.CreateEmptyFile("a.txt");
        var symlink = temp.GetFullPath("b.txt");
        CreateSymlink(symlink, "a.txt", isDirectory: false);

        Assert.True(target.TryGetCanonicalPath(out var expected));
        Assert.True(symlink.TryGetCanonicalPath(out var actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task TryGetCanonicalPath_MissingPath()
    {
        await using var temp = TemporaryDirectory.Create();
        var missingPath = temp.GetFullPath("missing.txt");

        Assert.False(missingPath.TryGetCanonicalPath(out _));
    }

    [Fact]
    public void ChangeExtension()
    {
        var actual = FullPath.FromPath("test.a.txt").WithExtension(".avi");
        var expected = FullPath.FromPath("test.a.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_NoExtension()
    {
        var actual = FullPath.FromPath("test").WithExtension(".avi");
        var expected = FullPath.FromPath("test.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_Empty()
    {
        var actual = FullPath.Empty.WithExtension(".avi");
        var expected = FullPath.Empty;
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_Null()
    {
        var actual = FullPath.FromPath("test").WithExtension(null);
        var expected = FullPath.FromPath("test");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ChangeExtension_NoDot()
    {
        var actual = FullPath.FromPath("test.txt").WithExtension("avi");
        var expected = FullPath.FromPath("test.avi");
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CreateTempFile_Default()
    {
        var path = FullPath.CreateTempFile(prefix: null);
        try
        {
            Assert.Equal(".tmp", path.Extension);
            Assert.True(File.Exists(path.Value));
        }
        finally
        {
            File.Delete(path.Value);
        }
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void CreateTempFile_IsOnlyAccessibleByTheCurrentUser()
    {
        var path = FullPath.CreateTempFile(prefix: null);
        try
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path.Value));
        }
        finally
        {
            File.Delete(path.Value);
        }
    }

    [Fact]
    public void CreateTempFile_WithFolderPrefixSuffix()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var folder = tempDirectory.FullPath / Guid.NewGuid().ToString("N");
        Assert.False(Directory.Exists(folder.Value));

        var path = FullPath.CreateTempFile(folder, "prefix-", ".txt");

        try
        {
            Assert.Equal(".txt", path.Extension);
            Assert.Equal(folder, path.Parent);
            Assert.StartsWith("prefix-", path.Name);
            Assert.True(File.Exists(path.Value));
            Assert.True(Directory.Exists(folder.Value));
        }
        finally
        {
            File.Delete(path.Value);
        }
    }

    [Fact]
    public void CreateTempFile_WithNullSuffix()
    {
        var path = FullPath.CreateTempFile(prefix: "prefix-", suffix: null);
        try
        {
            Assert.Equal(string.Empty, path.Extension);
            Assert.StartsWith("prefix-", path.Name);
            Assert.True(File.Exists(path.Value));
        }
        finally
        {
            File.Delete(path.Value);
        }
    }

    [Fact]
    public void CreateTempFile_ThrowsAfterMaxAttempts()
    {
        var folder = FullPath.GetTempPath();
        var invalidSuffix = $"{Path.DirectorySeparatorChar}invalid";
        var exception = Assert.Throws<IOException>(() => FullPath.CreateTempFile(folder, prefix: null, suffix: invalidSuffix));
        Assert.Contains("10 attempts", exception.Message);
    }

    [Fact]
    public void GetFolderPath()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(FullPath.FromPath(expected), FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    [Fact]
    public void GetFolderPath_WithSpecialFolderOption()
    {
        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        Assert.Equal(FullPath.FromPath(expected), FullPath.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify));
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
    public void TryFindGitRepositoryRoot()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateDirectory(".git");
        var subDir = tempDir.CreateDirectory("src/app");

        Assert.True(subDir.TryFindGitRepositoryRoot(out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void TryFindGitRepositoryRoot_Worktree()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateTextFile(".git", "gitdir: C:/main-repo/.git/worktrees/sample-worktree");
        var subDir = tempDir.CreateDirectory("src/app");

        Assert.True(subDir.TryFindGitRepositoryRoot(out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void TryFindGitRepositoryRoot_Worktree_FromFilePath()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateTextFile(".git", "gitdir: C:/main-repo/.git/worktrees/sample-worktree");
        var filePath = tempDir.CreateEmptyFile("src/app/readme.txt");

        Assert.True(filePath.TryFindGitRepositoryRoot(out var result));
        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void TryFindGitRepositoryRoot_NotFound()
    {
        using var tempDir = TemporaryDirectory.Create();
        var subDir = tempDir.CreateDirectory("src/app");

        Assert.False(subDir.TryFindGitRepositoryRoot(out _));
    }

    [Fact]
    public void FindRequiredGitRepositoryRoot()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateDirectory(".git");
        var subDir = tempDir.CreateDirectory("src/app");

        var result = subDir.FindRequiredGitRepositoryRoot();

        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void FindRequiredGitRepositoryRoot_Worktree()
    {
        using var tempDir = TemporaryDirectory.Create();
        tempDir.CreateTextFile(".git", "gitdir: C:/main-repo/.git/worktrees/sample-worktree");
        var subDir = tempDir.CreateDirectory("src/app");

        var result = subDir.FindRequiredGitRepositoryRoot();

        Assert.Equal(tempDir.FullPath, result);
    }

    [Fact]
    public void FindRequiredGitRepositoryRoot_NotFound()
    {
        using var tempDir = TemporaryDirectory.Create();
        var subDir = tempDir.CreateDirectory("src/app");

        Assert.Throws<InvalidOperationException>(() => subDir.FindRequiredGitRepositoryRoot());
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void KnownFolderTest()
    {
        var fullPath = FullPath.GetKnownFolderPath(KnownFolder.Downloads);
        Assert.NotEmpty(fullPath.Value);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Linux | TestOperatingSystems.MacOS)]
    public void ToWindowsExtendedPath_ThrowsOnNonWindows()
    {
        var path = FullPath.FromPath("test") / "a" / "b.txt";

        Assert.Throws<PlatformNotSupportedException>(path.ToWindowsExtendedPath);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_RegularPath()
    {
        var path = FullPath.FromPath(@"C:\temp\test.txt");
        var extended = path.ToWindowsExtendedPath();
        Assert.Equal(@"\\?\C:\temp\test.txt", extended);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_UNCPath()
    {
        var path = FullPath.FromPath(@"\\server\share\folder\file.txt");
        var extended = path.ToWindowsExtendedPath();
        Assert.Equal(@"\\?\UNC\server\share\folder\file.txt", extended);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_AlreadyExtended()
    {
        var path = FullPath.FromPath(@"C:\temp\test.txt");
        var extended = path.ToWindowsExtendedPath();

        // FromPath removes the device prefix, so the round-trip has to give back the original path. Asserting only
        // that ToWindowsExtendedPath is idempotent would compare two identical expressions and could never fail.
        Assert.Equal(path, FullPath.FromPath(extended));
        Assert.Equal(extended, FullPath.FromPath(extended).ToWindowsExtendedPath());
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_UNCPath_RoundTrips()
    {
        var path = FullPath.FromPath(@"\\server\share\folder\file.txt");
        var extended = path.ToWindowsExtendedPath();

        Assert.Equal(@"\\?\UNC\server\share\folder\file.txt", extended);
        Assert.Equal(path, FullPath.FromPath(extended));
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_Empty()
    {
        var extended = FullPath.Empty.ToWindowsExtendedPath();
        Assert.Equal("", extended);
    }

    [Fact]
    [RunIf(TestOperatingSystems.Windows)]
    public void ToWindowsExtendedPath_LongPath()
    {
        var longSegment = new string('a', 250);
        var path = FullPath.FromPath($@"C:\{longSegment}\test.txt");
        var extended = path.ToWindowsExtendedPath();
        Assert.StartsWith(@"\\?\", extended);
        Assert.Contains(longSegment, extended);
    }

    private static FullPath GetRootDirectory()
    {
        return FullPath.FromPath(Path.GetPathRoot(FullPath.CurrentDirectory().Value)!);
    }

    private static void CreateSymlink(FullPath source, string target, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.CreateSymbolicLink(source, target);
        }
        else
        {
            File.CreateSymbolicLink(source, target);
        }
    }
}
