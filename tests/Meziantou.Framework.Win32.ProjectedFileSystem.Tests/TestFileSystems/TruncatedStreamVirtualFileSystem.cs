namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Test VFS whose stream ends before supplying the length it declared for the entry.
/// Stands in for a provider whose backing store is truncated mid-read (a dropped connection,
/// a partial download), which must surface as a failed read rather than zero-filled content.
/// </summary>
internal sealed class TruncatedStreamVirtualFileSystem : ProjectedFileSystemBase
{
    private const int DeclaredSize = 10000;
    private const int ActualSize = 5000;

    public TruncatedStreamVirtualFileSystem(string rootFolder) : base(rootFolder) { }

    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (AreFileNamesEqual(path, ""))
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([ProjectedFileSystemEntry.File("truncated.bin", DeclaredSize)]);

        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([]);
    }

    protected override ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (AreFileNamesEqual(path, "truncated.bin"))
        {
            var data = new byte[ActualSize];
            Array.Fill(data, (byte)0xAB);
            return ValueTask.FromResult<Stream?>(new MemoryStream(data));
        }

        return ValueTask.FromResult<Stream?>(null);
    }
}
