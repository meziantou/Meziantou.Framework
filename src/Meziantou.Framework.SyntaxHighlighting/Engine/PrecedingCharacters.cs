namespace Meziantou.Framework.SyntaxHighlighting.Engine;

/// <summary>
/// Characters that a begin match also covers right before where <see cref="Mode.Begin"/> matches, as if the pattern
/// started with a greedy loop of at most <see cref="MaxLength"/> of them: highlight.js's <c>[ ]*(?=X)</c> and <c>.?X</c>.
/// </summary>
/// <remarks>
/// <para>
/// The regex engine finds nothing to search for in such a pattern, so it runs it at every position of the input. The
/// tokenizer searches for <c>X</c> instead, then extends the match back over the characters before it (see
/// <see cref="Mode.BeginPreceding"/>), but never before the position the scan starts from.
/// </para>
/// <para>
/// That is the leftmost match of the whole pattern, with the same length, as long as <c>X</c> cannot match again after
/// accepted characters that follow one of its matches: when <c>X</c> matches at <c>k</c>, it does not match at any
/// <c>j</c> in <c>(k, k + MaxLength]</c> such that the characters of <c>[k, j)</c> are all accepted. A pattern that cannot
/// start with an accepted character (<c>TODO:</c> after spaces), or a literal that does not overlap itself shifted
/// by one character (<c>css`</c> after <c>.?</c>), has that property.
/// </para>
/// </remarks>
internal sealed class PrecedingCharacters
{
    private readonly Func<char, bool> _accepts;

    private PrecedingCharacters(Func<char, bool> accepts, int maxLength)
    {
        _accepts = accepts;
        MaxLength = maxLength;
    }

    /// <summary><c>[ ]*</c></summary>
    public static PrecedingCharacters Spaces { get; } = new(c => c is ' ', int.MaxValue);

    /// <summary><c>.?</c> (without <see cref="System.Text.RegularExpressions.RegexOptions.Singleline"/>)</summary>
    public static PrecedingCharacters OptionalCharacter { get; } = new(c => c is not '\n', 1);

    public int MaxLength { get; }

    public bool Accepts(char c) => _accepts(c);
}
