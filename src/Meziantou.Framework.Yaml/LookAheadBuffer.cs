namespace Meziantou.Framework.Yaml;

/// <summary>
/// Provides access to a stream and allows to peek at the next characters,
/// up to the buffer's capacity.
/// </summary>
/// <remarks>This class implements a circular buffer with a fixed capacity.</remarks>
public class LookAheadBuffer : ILookAheadBuffer
{
    private readonly TextReader _input;
    private readonly char[] _buffer;
    private int _firstIndex;
    private int _count;
    private bool _endOfInput;

    /// <summary>
    /// Initializes a new instance of the <see cref="LookAheadBuffer"/> class.
    /// </summary>
    /// <param name="input">The input.</param>
    /// <param name="capacity">The capacity.</param>
    public LookAheadBuffer(TextReader input, int capacity)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "The capacity must be positive.");
        }

        this._input = input;
        _buffer = new char[capacity];
    }

    /// <summary>Gets a value indicating whether the end of the input reader has been reached.</summary>
    public bool EndOfInput { get { return _endOfInput && _count == 0; } }

    /// <summary>Gets the index of the character for the specified offset.</summary>
    private int GetIndexForOffset(int offset)
    {
        int index = _firstIndex + offset;
        if (index >= _buffer.Length)
        {
            index -= _buffer.Length;
        }
        return index;
    }

    /// <summary>Gets the character at thhe specified offset.</summary>
    public char Peek(int offset)
    {
        if (offset < 0 || offset >= _buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "The offset must be betwwen zero and the capacity of the buffer.");
        }

        Cache(offset);

        if (offset < _count)
        {
            return _buffer[GetIndexForOffset(offset)];
        }
        else
        {
            return '\0';
        }
    }

    /// <summary>
    /// Reads characters until at least <paramref name="length"/> characters are in the buffer.
    /// </summary>
    /// <param name="length">Number of characters to cache.</param>
    public void Cache(int length)
    {
        while (length >= _count)
        {
            int nextChar = _input.Read();
            if (nextChar >= 0)
            {
                int lastIndex = GetIndexForOffset(_count);
                _buffer[lastIndex] = (char)nextChar;
                ++_count;
            }
            else
            {
                _endOfInput = true;
                return;
            }
        }
    }

    /// <summary>
    /// Skips the next <paramref name="length"/> characters. Those characters must have been
    /// obtained first by calling the <see cref="Peek"/> or <see cref="Cache"/> methods.
    /// </summary>
    public void Skip(int length)
    {
        if (length < 1 || length > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The length must be between 1 and the number of characters in the buffer. Use the Peek() and / or Cache() methods to fill the buffer.");
        }
        _firstIndex = GetIndexForOffset(length);
        _count -= length;
    }
}