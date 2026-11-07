namespace Meziantou.Framework;

/// <summary>Enumerates the extended grapheme clusters of a text. See <see cref="Unicode.EnumerateGraphemeClusters(ReadOnlySpan{char})"/>.</summary>
public ref struct UnicodeGraphemeClusterEnumerator
{
    private readonly ReadOnlySpan<char> _text;
    private int _start;
    private int _end;

    internal UnicodeGraphemeClusterEnumerator(ReadOnlySpan<char> text)
    {
        _text = text;
    }

    /// <summary>Gets the range of the current cluster in the text.</summary>
    public readonly Range Current => _start.._end;

    /// <summary>Returns this instance, so that the enumerator can be used by a <see langword="foreach"/> statement.</summary>
    /// <returns>This instance.</returns>
    public readonly UnicodeGraphemeClusterEnumerator GetEnumerator() => this;

    /// <summary>Moves to the next cluster.</summary>
    /// <returns><see langword="true"/> when there is a cluster; <see langword="false"/> at the end of the text.</returns>
    public bool MoveNext()
    {
        if (_end >= _text.Length)
            return false;

        _start = _end;
        _end += Unicode.GetGraphemeClusterLength(_text[_start..]);
        return true;
    }
}
