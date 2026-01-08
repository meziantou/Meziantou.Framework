using System.Text;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Test VFS with deeply nested directory structure (Foo/Bar/Baz.txt).
/// Used to verify that placeholder paths are handled correctly for nested directories.
/// </summary>
internal sealed class NestedVirtualFileSystem : ProjectedFileSystemBase
{
    public NestedVirtualFileSystem(string rootFolder) : base(rootFolder) { }

    protected override IEnumerable<ProjectedFileSystemEntry> GetEntries(string path)
    {
        // Structure:
        // /
        // └── Foo/
        //     └── Bar/
        //         └── Baz.txt
        if (AreFileNamesEqual(path, ""))
            yield return ProjectedFileSystemEntry.Directory("Foo");
        else if (AreFileNamesEqual(path, "Foo"))
            yield return ProjectedFileSystemEntry.Directory("Bar");
        else if (AreFileNamesEqual(path, @"Foo\Bar"))
            yield return ProjectedFileSystemEntry.File("Baz.txt", 18);
    }

    // Override GetEntry to fix comparison for nested paths.
    // The base implementation compares entry.Name with the full path, which fails for nested paths.
    // It should compare with just the filename portion.
    protected override ProjectedFileSystemEntry? GetEntry(string path)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileName(path);
        return GetEntries(directory ?? "").FirstOrDefault(entry => AreFileNamesEqual(entry.Name, fileName));
    }

    protected override Stream? OpenRead(string path)
    {
        if (AreFileNamesEqual(path, @"Foo\Bar\Baz.txt"))
            return new MemoryStream(Encoding.UTF8.GetBytes("Hello from Foo Bar"));
        return null;
    }
}

