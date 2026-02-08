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

    protected override IEnumerable<ProjectedFileSystemEntry> GetEntries(string path)
    {
        if (AreFileNamesEqual(path, ""))
            yield return ProjectedFileSystemEntry.File("nonseekabledatafile.bin", FileSize);
    }

    protected override Stream? OpenRead(string path)
    {
        if (AreFileNamesEqual(path, "nonseekabledatafile.bin"))
        {
            // Generate predictable content: byte at position N = N % 256
            var data = new byte[FileSize];
            for (var i = 0; i < FileSize; i++)
                data[i] = (byte)(i % 256);
            return new NonSeekableStream(new MemoryStream(data));
        }
        return null;
    }

    /// <summary>
    /// Wraps a stream to make it non-seekable, simulating network or pipe streams.
    /// </summary>
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
