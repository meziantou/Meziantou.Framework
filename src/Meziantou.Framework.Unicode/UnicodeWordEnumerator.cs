namespace Meziantou.Framework;

/// <summary>Enumerates the segments between the word boundaries of a text. See <see cref="Unicode.EnumerateWordBoundaries(ReadOnlySpan{char})"/>.</summary>
public ref struct UnicodeWordEnumerator
{
    private readonly ReadOnlySpan<char> _text;
    private int _start;
    private int _end;

    internal UnicodeWordEnumerator(ReadOnlySpan<char> text)
    {
        _text = text;
    }

    /// <summary>Gets the range of the current segment in the text.</summary>
    public readonly Range Current => _start.._end;

    /// <summary>Returns this instance, so that the enumerator can be used by a <see langword="foreach"/> statement.</summary>
    /// <returns>This instance.</returns>
    public readonly UnicodeWordEnumerator GetEnumerator() => this;

    /// <summary>Moves to the next segment.</summary>
    /// <returns><see langword="true"/> when there is a segment; <see langword="false"/> at the end of the text.</returns>
    public bool MoveNext()
    {
        if (_end >= _text.Length)
            return false;

        _start = _end;
        _end += Unicode.GetWordSegmentLength(_text[_start..]);
        return true;
    }
}
