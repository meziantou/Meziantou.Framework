using System.Threading;

namespace Meziantou.Framework.Win32.ProjectedFileSystem;

internal sealed class AsyncSampleVirtualFileSystem : ProjectedFileSystemBase
{
    private int _getEntriesAsyncCalls;
    private int _getEntriesAsyncContinuations;
    private int _getEntryAsyncCalls;
    private int _getEntryAsyncContinuations;
    private int _openReadAsyncCalls;
    private int _openReadAsyncContinuations;

    public AsyncSampleVirtualFileSystem(string rootFolder)
        : base(rootFolder)
    {
    }

    public int GetEntriesAsyncCalls => _getEntriesAsyncCalls;
    public int GetEntriesAsyncContinuations => _getEntriesAsyncContinuations;
    public int GetEntryAsyncCalls => _getEntryAsyncCalls;
    public int GetEntryAsyncContinuations => _getEntryAsyncContinuations;
    public int OpenReadAsyncCalls => _openReadAsyncCalls;
    public int OpenReadAsyncContinuations => _openReadAsyncContinuations;

    protected override async ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        Interlocked.Increment(ref _getEntriesAsyncCalls);
        await Task.Yield();
        Interlocked.Increment(ref _getEntriesAsyncContinuations);

        if (AreFileNamesEqual(path, ""))
        {
            return [ProjectedFileSystemEntry.Directory("async-dir"), ProjectedFileSystemEntry.File("async-file.bin", 4)];
        }

        if (AreFileNamesEqual(path, "async-dir"))
        {
            return [ProjectedFileSystemEntry.File("nested-async.txt", 3)];
        }

        return [];
    }

    protected override async ValueTask<ProjectedFileSystemEntry?> GetEntryAsync(string path)
    {
        Interlocked.Increment(ref _getEntryAsyncCalls);
        await Task.Yield();
        Interlocked.Increment(ref _getEntryAsyncContinuations);

        if (AreFileNamesEqual(path, "async-file.bin"))
        {
            return ProjectedFileSystemEntry.File("async-file.bin", 4);
        }

        if (AreFileNamesEqual(path, "async-dir"))
        {
            return ProjectedFileSystemEntry.Directory("async-dir");
        }

        if (AreFileNamesEqual(path, @"async-dir\nested-async.txt"))
        {
            return ProjectedFileSystemEntry.File("nested-async.txt", 3);
        }

        return null;
    }

    protected override async ValueTask<Stream?> OpenReadAsync(string path)
    {
        Interlocked.Increment(ref _openReadAsyncCalls);
        await Task.Yield();
        Interlocked.Increment(ref _openReadAsyncContinuations);

        if (AreFileNamesEqual(path, "async-file.bin"))
        {
            return new MemoryStream([10, 20, 30, 40]);
        }

        if (AreFileNamesEqual(path, @"async-dir\nested-async.txt"))
        {
            return new MemoryStream([50, 60, 70]);
        }

        return null;
    }
}
