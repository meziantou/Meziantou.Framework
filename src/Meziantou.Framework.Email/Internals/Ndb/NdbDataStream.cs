namespace Meziantou.Framework.Email.Internals.Ndb;

/// <summary>A read-only stream over the data of a node, which reads its blocks one at a time.</summary>
internal sealed class NdbDataStream : Stream
{
    private readonly NdbReader _reader;
    private readonly ulong[] _leaves;
    private readonly long _length;
    private int _leafIndex;
    private byte[] _current = [];
    private int _offset;
    private long _position;

    public NdbDataStream(NdbReader reader, ulong bid)
    {
        _reader = reader;
        _leaves = [.. reader.EnumerateDataLeaves(bid)];
        _length = reader.GetDataSize(bid);
    }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        while (_offset >= _current.Length)
        {
            if (_leafIndex >= _leaves.Length)
            {
                return 0;
            }

            _current = _reader.ReadBlock(_leaves[_leafIndex++]);
            _offset = 0;
        }

        var count = Math.Min(buffer.Length, _current.Length - _offset);
        _current.AsSpan(_offset, count).CopyTo(buffer);
        _offset += count;
        _position += count;
        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
