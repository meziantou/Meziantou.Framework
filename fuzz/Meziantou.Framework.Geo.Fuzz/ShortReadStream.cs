namespace Meziantou.Framework.Geo.Fuzz;

/// <summary>
/// A read-only stream over a buffer whose reads return at most a number of bytes, as a network may, and that can refuse
/// to seek. The asynchronous reads complete synchronously.
/// </summary>
internal sealed class ShortReadStream(byte[] buffer, int maxReadLength, bool canSeek) : Stream
{
    private readonly MemoryStream _inner = new(buffer, writable: false);

    public override bool CanRead => true;

    public override bool CanSeek => canSeek;

    public override bool CanWrite => false;

    public override long Length => canSeek ? _inner.Length : throw new NotSupportedException();

    public override long Position
    {
        get => canSeek ? _inner.Position : throw new NotSupportedException();
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => _inner.Read(buffer[..Math.Min(buffer.Length, maxReadLength)]);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

    public override long Seek(long offset, SeekOrigin origin) => canSeek ? _inner.Seek(offset, origin) : throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
