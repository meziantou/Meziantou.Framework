namespace Meziantou.Framework.Win32.ProjectedFileSystem;

internal sealed class AsyncSampleVirtualFileSystem : ProjectedFileSystemBase
{
    public AsyncSampleVirtualFileSystem(string rootFolder)
        : base(rootFolder)
    {
    }

    protected override async ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        await Task.Yield();
        return path switch
        {
            "" => [ProjectedFileSystemEntry.File("async-file.bin", 4)],
            _ => [],
        };
    }

    protected override async ValueTask<Stream?> OpenReadAsync(string path)
    {
        await Task.Yield();
        return AreFileNamesEqual(path, "async-file.bin")
            ? new MemoryStream([10, 20, 30, 40])
            : null;
    }
}
