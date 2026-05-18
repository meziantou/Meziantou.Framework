namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Test VFS with deeply nested directory structure (Foo/Bar/Baz.txt).
/// Used to verify that placeholder paths are handled correctly for nested directories.
/// </summary>
internal sealed class NestedVirtualFileSystem : ProjectedFileSystemBase
{
    public NestedVirtualFileSystem(string rootFolder) : base(rootFolder) { }

    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        // Structure:
        // /
        // └── Foo/
        //     └── Bar/
        //         └── Baz.txt
        if (AreFileNamesEqual(path, ""))
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([ProjectedFileSystemEntry.Directory("Foo")]);

        if (AreFileNamesEqual(path, "Foo"))
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([ProjectedFileSystemEntry.Directory("Bar")]);

        if (AreFileNamesEqual(path, @"Foo\Bar"))
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([ProjectedFileSystemEntry.File("Baz.txt", 18)]);

        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([]);
    }

    protected override ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (AreFileNamesEqual(path, @"Foo\Bar\Baz.txt"))
            return ValueTask.FromResult<Stream?>(new MemoryStream(Encoding.UTF8.GetBytes("Hello from Foo Bar")));

        return ValueTask.FromResult<Stream?>(null);
    }
}
