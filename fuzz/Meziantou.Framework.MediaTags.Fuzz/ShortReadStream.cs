namespace Meziantou.Framework.MediaTags.Fuzz;

/// <summary>A seekable read-only stream over a buffer that returns at most a few bytes per read, as a network or a pipe may.</summary>
internal sealed class ShortReadStream(byte[] buffer, int maxBytesPerRead) : Stream
{
    private readonly MemoryStream _inner = new(buffer, writable: false);

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, maxBytesPerRead));

    public override int Read(Span<byte> buffer) => _inner.Read(buffer[..Math.Min(buffer.Length, maxBytesPerRead)]);

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
