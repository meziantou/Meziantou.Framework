namespace Meziantou.Framework;

/// <summary>Enumerates the sentences of a text. See <see cref="Unicode.EnumerateSentenceBoundaries(ReadOnlySpan{char})"/>.</summary>
public ref struct UnicodeSentenceEnumerator
{
    private readonly ReadOnlySpan<char> _text;
    private int _start;
    private int _end;

    internal UnicodeSentenceEnumerator(ReadOnlySpan<char> text)
    {
        _text = text;
    }

    /// <summary>Gets the range of the current sentence in the text.</summary>
    public readonly Range Current => _start.._end;

    /// <summary>Returns this instance, so that the enumerator can be used by a <see langword="foreach"/> statement.</summary>
    /// <returns>This instance.</returns>
    public readonly UnicodeSentenceEnumerator GetEnumerator() => this;

    /// <summary>Moves to the next sentence.</summary>
    /// <returns><see langword="true"/> when there is a sentence; <see langword="false"/> at the end of the text.</returns>
    public bool MoveNext()
    {
        if (_end >= _text.Length)
            return false;

        _start = _end;
        _end += Unicode.GetSentenceLength(_text[_start..]);
        return true;
    }
}
