using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Compression.Internals.Ppmd;

/// <summary>Reads the bytes of a stream through a buffer.</summary>
internal sealed class PpmdStreamByteSource : PpmdByteSource
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The decoder stream owns the stream")]
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[16384];
    private int _position;
    private int _length;
    private bool _endOfStream;

    public PpmdStreamByteSource(Stream stream)
    {
        _stream = stream;
    }

    public override int ReadByte()
    {
        if (_position < _length)
            return _buffer[_position++];

        return ReadByteSlow();
    }

    private int ReadByteSlow()
    {
        if (_endOfStream)
            return -1;

        _length = _stream.Read(_buffer, 0, _buffer.Length);
        if (_length <= 0)
        {
            _length = 0;
            _endOfStream = true;
            return -1;
        }

        _position = 1;
        return _buffer[0];
    }
}
