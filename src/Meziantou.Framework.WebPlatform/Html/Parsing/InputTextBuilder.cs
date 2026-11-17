using System.Text;

namespace Meziantou.Framework.WebPlatform.Html.Parsing;

/// <summary>
/// Builds the text of a token (a character run, a name, an attribute value, a comment). While the text is a contiguous run
/// of the input, it is a slice of the input string, so the characters are not copied; they are copied to a buffer as soon
/// as a character that is not the next character of the slice is appended (a character reference, a normalized newline...).
/// The slice keeps a reference to the input string, so it remains valid when the input changes (<c>document.write()</c>).
/// </summary>
internal sealed class InputTextBuilder
{
    private string? _source;
    private int _start;
    private char[] _buffer = [];
    private int _length;

    public int Length => _length;

    public ReadOnlySpan<char> Span => _source is not null ? _source.AsSpan(_start, _length) : _buffer.AsSpan(0, _length);

    public void Clear()
    {
        _source = null;
        _length = 0;
    }

    /// <summary>Removes the characters after <paramref name="length"/> (the text is restored to a previous length).</summary>
    public void Truncate(int length)
    {
        _length = length;
    }

    /// <summary>Appends characters of the input.</summary>
    public void Append(string input, int start, int length)
    {
        if (_length == 0)
        {
            _source = input;
            _start = start;
            _length = length;
            return;
        }

        if (_source is not null && ReferenceEquals(_source, input) && _start + _length == start)
        {
            _length += length;
            return;
        }

        Append(input.AsSpan(start, length));
    }

    /// <summary>
    /// Appends a character consumed from the input at <paramref name="index"/>: it starts a slice when the text is empty
    /// and the character is unchanged (e.g. the first letter of a name, which the tokenizer reconsumes).
    /// </summary>
    public void AppendConsumed(char value, string input, int index)
    {
        if (_length == 0 && (uint)index < (uint)input.Length && input[index] == value)
        {
            _source = input;
            _start = index;
            _length = 1;
            return;
        }

        Append(value);
    }

    public void Append(char value)
    {
        if (_source is not null)
        {
            // The slice is extended when the character is the next character of the input
            var end = _start + _length;
            if (end < _source.Length && _source[end] == value)
            {
                _length++;
                return;
            }

            CopySourceToBuffer(1);
        }
        else if (_length == _buffer.Length)
        {
            Grow(1);
        }

        _buffer[_length++] = value;
    }

    public void Append(ReadOnlySpan<char> value)
    {
        if (_source is not null)
        {
            CopySourceToBuffer(value.Length);
        }
        else if (_length + value.Length > _buffer.Length)
        {
            Grow(value.Length);
        }

        value.CopyTo(_buffer.AsSpan(_length));
        _length += value.Length;
    }

    public void Append(StringBuilder value)
    {
        foreach (var chunk in value.GetChunks())
        {
            Append(chunk.Span);
        }
    }

    public bool Equals(string value) => Span.SequenceEqual(value);

    public override string ToString() => _length == 0 ? "" : Span.ToString();

    private void CopySourceToBuffer(int additionalCapacity)
    {
        var source = _source.AsSpan(_start, _length);
        _source = null;
        if (_length + additionalCapacity > _buffer.Length)
        {
            _buffer = new char[GetNewCapacity(additionalCapacity)];
        }

        source.CopyTo(_buffer);
    }

    private void Grow(int additionalCapacity)
    {
        Array.Resize(ref _buffer, GetNewCapacity(additionalCapacity));
    }

    private int GetNewCapacity(int additionalCapacity) => Math.Max(Math.Max(_buffer.Length * 2, 16), _length + additionalCapacity);
}
