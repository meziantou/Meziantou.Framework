namespace Meziantou.Framework.Win32.ProjectedFileSystem;

/// <summary>
/// Test VFS that returns a non-seekable stream for file content.
/// Used to verify that GetFileDataCallback correctly handles streams
/// that do not support seeking by manually advancing past the byte offset.
/// </summary>
internal sealed class NonSeekableStreamVirtualFileSystem : ProjectedFileSystemBase
{
    // File size larger than default BufferSize (4KB) to trigger multi-chunk reads
    private const int FileSize = 10000;

    public NonSeekableStreamVirtualFileSystem(string rootFolder) : base(rootFolder) { }

    protected override ValueTask<IEnumerable<ProjectedFileSystemEntry>> GetEntriesAsync(string path)
    {
        if (AreFileNamesEqual(path, ""))
            return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([ProjectedFileSystemEntry.File("nonseekabledatafile.bin", FileSize)]);

        return ValueTask.FromResult<IEnumerable<ProjectedFileSystemEntry>>([]);
    }

    protected override ValueTask<Stream?> OpenReadAsync(string path)
    {
        if (AreFileNamesEqual(path, "nonseekabledatafile.bin"))
        {
            // Generate predictable content: byte at position N = N % 256
            var data = new byte[FileSize];
            for (var i = 0; i < FileSize; i++)
                data[i] = (byte)(i % 256);
            // CA2000 can't track ownership transfer through ValueTask.FromResult.
#pragma warning disable CA2000 // Dispose objects before losing scope
            return ValueTask.FromResult<Stream?>(new RestrictedStream(new MemoryStream(data), new RestrictedStreamOptions
            {
                AllowSynchronousCalls = true,
                AllowAsynchronousCalls = true,
                AllowReading = true,
                AllowWriting = true,
                AllowSeeking = false,
            }));
#pragma warning restore CA2000 // Dispose objects before losing scope
        }

        return ValueTask.FromResult<Stream?>(null);
    }
}
