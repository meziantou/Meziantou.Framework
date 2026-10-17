namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The source of a streaming <see cref="System.IO.Compression.ZLibStream"/> fed by a push-model decoder:
/// the decoder copies one bounded piece of compressed data at a time with
/// <see cref="Push"/>, then pulls decompressed bytes until the inflater starves (this stream then reads 0 bytes). The BCL
/// inflater resumes after a starved read, so the compressed datastream is never buffered as a whole.
/// </summary>
internal sealed class InflateFeedStream : Stream
{
    private readonly byte[] _buffer;
    private int _offset;
    private int _length;

    /// <param name="buffer">The storage of one pushed piece (its capacity bounds each push).</param>
    public InflateFeedStream(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
    }

    /// <summary>Gets the number of pushed bytes not read by the inflater yet.</summary>
    public int Available => _length - _offset;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>Copies the next piece of compressed data; the previous piece must have been read completely.</summary>
    /// <returns>The number of bytes copied (at most the buffer capacity).</returns>
    public int Push(ReadOnlySpan<byte> data)
    {
        if (Available != 0)
            throw new InvalidOperationException("The previous compressed piece was not consumed by the inflater.");

        var count = Math.Min(data.Length, _buffer.Length);
        data[..count].CopyTo(_buffer);
        _offset = 0;
        _length = count;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, Available);
        _buffer.AsSpan(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
