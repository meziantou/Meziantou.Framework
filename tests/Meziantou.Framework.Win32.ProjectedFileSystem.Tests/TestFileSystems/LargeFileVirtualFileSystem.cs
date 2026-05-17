namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Test VFS with a large file (larger than default BufferSize of 4KB).
/// Used to verify that file data is read correctly at arbitrary offsets.
/// </summary>
internal sealed class LargeFileVirtualFileSystem : ProjectedFileSystemBase
{
    // File size larger than default BufferSize (4KB) to trigger multi-chunk reads
    private const int FileSize = 10000;

    public LargeFileVirtualFileSystem(string rootFolder) : base(rootFolder) { }

    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (AreFileNamesEqual(path, ""))
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([ProjectedFileSystemEntry.File("largefile.bin", FileSize)]);

        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([]);
    }

    protected override ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (AreFileNamesEqual(path, "largefile.bin"))
        {
            // Generate predictable content: byte at position N = N % 256
            // This allows verification that reads at offset X return data starting at X
            var data = new byte[FileSize];
            for (var i = 0; i < FileSize; i++)
                data[i] = (byte)(i % 256);
            return ValueTask.FromResult<Stream?>(new MemoryStream(data));
        }

        return ValueTask.FromResult<Stream?>(null);
    }
}
