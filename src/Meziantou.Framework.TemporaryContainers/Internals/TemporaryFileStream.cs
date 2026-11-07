namespace Meziantou.Framework.TemporaryContainers.Internals;

internal sealed class TemporaryFileStream : Stream
{
    private readonly string _path;
    private readonly string? _directory;
    private readonly FileStream _stream;
    private bool _disposed;

    /// <param name="path">The file to read, deleted when the stream is disposed.</param>
    /// <param name="directory">A temporary directory holding the file, deleted with it.</param>
    public TemporaryFileStream(string path, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        _path = path;
        _directory = directory;
        _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    /// <summary>Gets the path of the temporary file.</summary>
    public string FilePath => _path;

    public override bool CanRead => _stream.CanRead;

    public override bool CanSeek => _stream.CanSeek;

    public override bool CanWrite => false;

    public override long Length => _stream.Length;

    public override long Position
    {
        get => _stream.Position;
        set => _stream.Position = value;
    }

    public override void Flush() => _stream.Flush();

    public override int Read(byte[] buffer, int offset, int count) => _stream.Read(buffer, offset, count);

    public override int Read(Span<byte> buffer) => _stream.Read(buffer);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _stream.ReadAsync(buffer, cancellationToken);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => _stream.ReadAsync(buffer, offset, count, cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => _stream.Seek(offset, origin);

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (disposing)
                _stream.Dispose();
        }
        finally
        {
            DeleteFiles();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            await _stream.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            DeleteFiles();
        }

        await base.DisposeAsync().ConfigureAwait(false);
    }

    private void DeleteFiles()
    {
        File.Delete(_path);
        if (_directory is not null)
            PrivateTemporaryFile.DeleteDirectory(_directory);
    }
}
