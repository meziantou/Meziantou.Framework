namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Test VFS that returns directory entries in unsorted order.
/// Used to verify that DirectoryEnumerationSession properly sorts entries
/// before returning them to ProjFS for merge with on-disk placeholders.
/// </summary>
internal sealed class UnsortedEntriesVirtualFileSystem : ProjectedFileSystemBase
{
    public UnsortedEntriesVirtualFileSystem(string rootFolder) : base(rootFolder) { }

    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (AreFileNamesEqual(path, ""))
        {
            // Return entries in unsorted order (z, a, m, b, y)
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>(
            [
                ProjectedFileSystemEntry.File("zebra.txt", 1),
                ProjectedFileSystemEntry.File("apple.txt", 1),
                ProjectedFileSystemEntry.File("mango.txt", 1),
                ProjectedFileSystemEntry.Directory("banana"),
                ProjectedFileSystemEntry.File("yellow.txt", 1),
            ]);
        }

        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([]);
    }

    protected override ValueTask<Stream?> OpenReadAsync(string path) => ValueTask.FromResult<Stream?>(new MemoryStream([0]));
}
