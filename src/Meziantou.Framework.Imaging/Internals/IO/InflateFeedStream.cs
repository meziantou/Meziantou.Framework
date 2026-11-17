namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The source of a streaming <see cref="System.IO.Compression.DeflateStream"/> fed by a push-model decoder:
/// the decoder copies one bounded piece of compressed data at a time with <see cref="Push(ReadOnlySpan{byte})"/>, then
/// pulls decompressed bytes until the inflater starves (this stream then reads 0 bytes). The BCL inflater resumes after a
/// starved read, so the compressed datastream is never buffered as a whole.
/// </summary>
internal sealed class InflateFeedStream : Stream
{
    private readonly byte[] _buffer;
    private readonly byte[] _prefix;
    private int _offset;
    private int _length;
    private int _prefixOffset;
    private int _prefixLength;

    /// <param name="buffer">The storage of one pushed piece (its capacity bounds each push).</param>
    /// <param name="prefixCapacity">The maximum length of the prefix of a piece, stored apart from <paramref name="buffer"/>.</param>
    public InflateFeedStream(byte[] buffer, int prefixCapacity = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _buffer = buffer;
        _prefix = prefixCapacity == 0 ? [] : new byte[prefixCapacity];
    }

    /// <summary>Gets the number of pushed bytes not read by the inflater yet.</summary>
    public int Available => _prefixLength - _prefixOffset + _length - _offset;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>Copies the next piece of compressed data; the previous piece must have been read completely.</summary>
    /// <returns>The number of bytes copied (at most the buffer capacity).</returns>
    public int Push(ReadOnlySpan<byte> data) => Push([], data);

    /// <summary>
    /// Copies a prefix (at most the prefix capacity), then the next piece of compressed data; the previous piece must have
    /// been read completely.
    /// </summary>
    /// <returns>The number of bytes of <paramref name="data"/> copied (at most the buffer capacity).</returns>
    public int Push(ReadOnlySpan<byte> prefix, ReadOnlySpan<byte> data)
    {
        if (Available != 0)
            throw new InvalidOperationException("The previous compressed piece was not consumed by the inflater.");

        // The prefix is stored apart, so that it never takes room from the piece: every push reads data
        prefix.CopyTo(_prefix);
        _prefixOffset = 0;
        _prefixLength = prefix.Length;

        var count = Math.Min(data.Length, _buffer.Length);
        data[..count].CopyTo(_buffer);
        _offset = 0;
        _length = count;
        return count;
    }

    /// <summary>Removes the last pushed bytes that were not read yet, so that the inflater cannot read them.</summary>
    /// <param name="destination">Receives the bytes removed, in order: at most its length.</param>
    /// <returns>The number of bytes removed.</returns>
    public int Withhold(Span<byte> destination)
    {
        var count = Math.Min(destination.Length, Available);
        var fromBuffer = Math.Min(count, _length - _offset);
        var fromPrefix = count - fromBuffer;
        _prefix.AsSpan(_prefixLength - fromPrefix, fromPrefix).CopyTo(destination);
        _buffer.AsSpan(_length - fromBuffer, fromBuffer).CopyTo(destination[fromPrefix..]);
        _prefixLength -= fromPrefix;
        _length -= fromBuffer;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var fromPrefix = Math.Min(buffer.Length, _prefixLength - _prefixOffset);
        _prefix.AsSpan(_prefixOffset, fromPrefix).CopyTo(buffer);
        _prefixOffset += fromPrefix;

        var fromBuffer = Math.Min(buffer.Length - fromPrefix, _length - _offset);
        _buffer.AsSpan(_offset, fromBuffer).CopyTo(buffer[fromPrefix..]);
        _offset += fromBuffer;
        return fromPrefix + fromBuffer;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
