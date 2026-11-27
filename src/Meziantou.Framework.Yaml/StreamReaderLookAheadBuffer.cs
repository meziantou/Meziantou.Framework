namespace Meziantou.Framework.Yaml;

/// <summary>Reads the characters of a <see cref="StreamReader"/> by blocks, so that peeking at a character is an array access.</summary>
/// <remarks>
/// <see cref="LookAheadBuffer"/> calls <see cref="TextReader.Read()"/> for each character, which costs more than parsing it.
/// Reading a block from a <see cref="StreamReader"/> does not wait for more input than reading a character does: it returns
/// the characters decoded from one read of the stream when the stream has no more bytes available.
/// </remarks>
internal sealed class StreamReaderLookAheadBuffer : ILookAheadBuffer
{
    private const int BufferSize = 1024;

    private readonly StreamReader _input;
    private char[] _buffer = new char[BufferSize];
    private int _position;
    private int _length;
    private bool _endOfInput;

    public StreamReaderLookAheadBuffer(StreamReader input)
    {
        _input = input;
    }

    public bool EndOfInput => _endOfInput && _position >= _length;

    /// <summary>Gets the characters that are read from the input and not consumed yet.</summary>
    public ReadOnlySpan<char> Remaining => _buffer.AsSpan(_position, _length - _position);

    public char Peek(int offset)
    {
        var index = _position + offset;
        if ((uint)index < (uint)_length)
            return _buffer[index];

        Cache(offset);
        index = _position + offset;
        return index < _length ? _buffer[index] : '\0';
    }

    public void Skip(int length)
    {
        if ((uint)length > (uint)(_length - _position))
            throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 0 and the number of characters in the buffer. Use the Peek() and / or Cache() methods to fill the buffer.");

        _position += length;
    }

    /// <summary>Reads characters until at least <paramref name="length"/> + 1 characters are in the buffer, or the end of the input.</summary>
    public void Cache(int length)
    {
        if (_position + length < _length || _endOfInput)
            return;

        var count = _length - _position;
        if (length >= _buffer.Length)
        {
            var newBuffer = new char[Math.Max(length + 1, _buffer.Length * 2)];
            _buffer.AsSpan(_position, count).CopyTo(newBuffer);
            _buffer = newBuffer;
        }
        else if (_position > 0)
        {
            _buffer.AsSpan(_position, count).CopyTo(_buffer);
        }

        _position = 0;
        _length = count;
        while (_length <= length)
        {
            var read = _input.Read(_buffer.AsSpan(_length));
            if (read <= 0)
            {
                _endOfInput = true;
                return;
            }

            _length += read;
        }
    }
}
