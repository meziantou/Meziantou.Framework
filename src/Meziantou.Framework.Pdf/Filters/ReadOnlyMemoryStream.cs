namespace Meziantou.Framework.Pdf.Filters;

/// <summary>A read-only stream over memory, so that a decoder reads the data where it is, without a copy.</summary>
internal sealed class ReadOnlyMemoryStream(ReadOnlyMemory<byte> data) : Stream
{
    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => data.Length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, data.Length - _position);
        data.Span.Slice(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override int ReadByte() => _position < data.Length ? data.Span[_position++] : -1;

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
