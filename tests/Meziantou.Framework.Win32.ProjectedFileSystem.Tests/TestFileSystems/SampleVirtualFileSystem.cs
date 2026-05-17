namespace Meziantou.Framework.Win32.ProjectedFileSystem;

internal sealed class SampleVirtualFileSystem : ProjectedFileSystemBase
{
    public SampleVirtualFileSystem(string rootFolder)
        : base(rootFolder)
    {
    }

    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (AreFileNamesEqual(path, ""))
        {
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>(
            [
                ProjectedFileSystemEntry.Directory("folder"),
                ProjectedFileSystemEntry.File("a", 1),
                ProjectedFileSystemEntry.File("b", 2),
            ]);
        }

        if (AreFileNamesEqual(path, "folder"))
        {
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>(
            [
                ProjectedFileSystemEntry.File("c", 3),
            ]);
        }

        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([]);
    }

    [SuppressMessage("Style", "IDE0230:Use UTF-8 string literal", Justification = "")]
    protected override ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (AreFileNamesEqual(path, "a"))
        {
            return ValueTask.FromResult<Stream?>(new MemoryStream([1]));
        }

        if (AreFileNamesEqual(path, "b"))
        {
            return ValueTask.FromResult<Stream?>(new MemoryStream([1, 2]));
        }

        if (AreFileNamesEqual(path, "folder\\c"))
        {
            return ValueTask.FromResult<Stream?>(new MemoryStream([1, 2, 3]));
        }

        return ValueTask.FromResult<Stream?>(null);
    }
}
