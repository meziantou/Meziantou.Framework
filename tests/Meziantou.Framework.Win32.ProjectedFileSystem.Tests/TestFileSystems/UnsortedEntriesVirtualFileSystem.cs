namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Test VFS that returns directory entries in unsorted order.
/// Used to verify that DirectoryEnumerationSession properly sorts entries
/// before returning them to ProjFS for merge with on-disk placeholders.
/// </summary>
internal sealed class UnsortedEntriesVirtualFileSystem : ProjectedFileSystemBase
{
    public UnsortedEntriesVirtualFileSystem(string rootFolder) : base(rootFolder) { }

    protected override IEnumerable<ProjectedFileSystemEntry> GetEntries(string path)
    {
        if (AreFileNamesEqual(path, ""))
        {
            // Return entries in unsorted order (z, a, m, b, y)
            yield return ProjectedFileSystemEntry.File("zebra.txt", 1);
            yield return ProjectedFileSystemEntry.File("apple.txt", 1);
            yield return ProjectedFileSystemEntry.File("mango.txt", 1);
            yield return ProjectedFileSystemEntry.Directory("banana");
            yield return ProjectedFileSystemEntry.File("yellow.txt", 1);
        }
    }

    protected override Stream? OpenRead(string path) => new MemoryStream([0]);
}

