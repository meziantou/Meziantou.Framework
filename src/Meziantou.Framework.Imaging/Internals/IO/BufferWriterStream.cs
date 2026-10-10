using System.Buffers;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A write-only <see cref="Stream"/> over an <see cref="IBufferWriter{T}"/>, used to run a BCL compressor straight into
/// the encoder output buffer instead of into an intermediate array (the Deflate strips of the TIFF encoder).
/// </summary>
/// <remarks>The stream never performs I/O and never flushes anything to a destination: the writer owns the bytes.</remarks>
internal sealed class BufferWriterStream(IBufferWriter<byte> writer) : Stream
{
    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return;

        buffer.CopyTo(writer.GetSpan(buffer.Length));
        writer.Advance(buffer.Length);
    }

    public override void WriteByte(byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
