namespace Meziantou.Framework.Geo.Tests;

/// <summary>
/// A read-only stream that cannot seek, generated as it is read: a prefix, a record repeated a number of times, then a
/// suffix. A large document does not need to be in memory, and the bytes read tell how far a reader went.
/// </summary>
internal sealed class RepeatedRecordStream(byte[] prefix, byte[] record, long count, byte[] suffix) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => prefix.Length + (record.Length * count) + suffix.Length;
    public override long Position { get => _position; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var written = 0;
        while (written < buffer.Length && _position < Length)
        {
            ReadOnlySpan<byte> source;
            long sourceOffset;
            if (_position < prefix.Length)
            {
                source = prefix;
                sourceOffset = _position;
            }
            else if (_position < prefix.Length + (record.Length * count))
            {
                source = record;
                sourceOffset = (_position - prefix.Length) % record.Length;
            }
            else
            {
                source = suffix;
                sourceOffset = _position - prefix.Length - (record.Length * count);
            }

            var length = (int)Math.Min(buffer.Length - written, source.Length - sourceOffset);
            source.Slice((int)sourceOffset, length).CopyTo(buffer[written..]);
            written += length;
            _position += length;
        }

        return written;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Task.FromResult(Read(buffer.AsSpan(offset, count)));

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
