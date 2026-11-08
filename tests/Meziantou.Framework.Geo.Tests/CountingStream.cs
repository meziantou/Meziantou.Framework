namespace Meziantou.Framework.Geo.Tests;

/// <summary>A stream over data that counts the bytes read, and can return few bytes per read and refuse to seek.</summary>
internal sealed class CountingStream(byte[] data, bool canSeek = true, int maxReadLength = int.MaxValue) : Stream
{
    private readonly MemoryStream _inner = new(data, writable: false);

    public long BytesRead { get; private set; }

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

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer[..Math.Min(buffer.Length, maxReadLength)]);
        BytesRead += read;
        return read;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override long Seek(long offset, SeekOrigin origin) => canSeek ? _inner.Seek(offset, origin) : throw new NotSupportedException();

    public override void Flush() => throw new NotSupportedException();
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
