namespace Meziantou.Framework.Csv.Fuzz;

/// <summary>A read-only stream over bytes whose reads return the number of bytes a <see cref="ChunkSizer"/> gives, and complete synchronously.</summary>
internal sealed class ChunkedStream(byte[] data, ChunkSizer sizer) : Stream
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
        var count = (int)Math.Min(Math.Min(buffer.Length, data.Length - _position), (long)sizer.Next());
        data.AsSpan(_position, count).CopyTo(buffer);
        _position += count;
        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
