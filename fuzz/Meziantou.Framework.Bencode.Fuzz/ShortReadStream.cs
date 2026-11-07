namespace Meziantou.Framework.Bencode.Fuzz;

/// <summary>A read-only stream over a buffer whose reads return the number of bytes a <see cref="ChunkSizer"/> gives, as a network may.</summary>
internal sealed class ShortReadStream(byte[] buffer, ChunkSizer sizer) : Stream
{
    private readonly MemoryStream _inner = new(buffer, writable: false);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer) => _inner.Read(buffer[..Math.Min(buffer.Length, sizer.Next())]);

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer.AsSpan(offset, count)));

    // Completes synchronously, so the decoders run without the thread pool
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
