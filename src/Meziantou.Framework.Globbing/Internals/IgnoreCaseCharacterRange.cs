using System.Buffers;

namespace Meziantou.Framework.Globbing.Internals;

/// <summary>
///     A character range matched with <see cref="StringComparison.OrdinalIgnoreCase"/>, the comparison used by literals and
///     sets: a character is in the range when it is ordinal case-insensitive equal to one of the characters of the range.
/// </summary>
/// <remarks>
///     The bounds are not normalized, as it would change the characters the range contains: '[@-B]' contains 'A', and
///     lowering its bounds would drop both '@' and 'A'. Ordinal case-insensitive comparison does not fold a non-ASCII
///     character to an ASCII one ('ſ' is not 's', the Kelvin sign is not 'k'), and it relates more than two characters
///     to each other ('Σ', 'σ' and 'ς'), so <see cref="char.ToLowerInvariant(char)"/> and <see cref="char.ToUpperInvariant(char)"/>
///     cannot be used on their own.
/// </remarks>
internal sealed class IgnoreCaseCharacterRange
{
    private readonly CharacterRange _range;

    // The case keys of the characters of the range that are not their own key. Two characters are equal when their keys
    // are equal, so a character outside of the range matches when its key is one of these, or is a character of the
    // range that is its own key.
    private readonly SearchValues<char>? _foldedKeys;

    public IgnoreCaseCharacterRange(CharacterRange range)
    {
        _range = range;

        HashSet<char>? keys = null;
        for (var c = (int)range.Min; c <= range.Max; c++)
        {
            var key = GetCaseKey((char)c);
            if (key != c)
            {
                keys ??= [];
                keys.Add(key);
            }
        }

        if (keys is not null)
        {
            _foldedKeys = SearchValues.Create([.. keys]);
        }
    }

    public CharacterRange Range => _range;

    public bool Contains(char c)
    {
        if (_range.IsInRange(c))
            return true;

        var key = GetCaseKey(c);
        if (_range.IsInRange(key) && GetCaseKey(key) == key)
            return true;

        return _foldedKeys is not null && _foldedKeys.Contains(key);
    }

    /// <summary>
    ///     Returns the character that <see cref="StringComparison.OrdinalIgnoreCase"/> compares <paramref name="c"/> as:
    ///     two characters are equal when they have the same key.
    /// </summary>
    internal static char GetCaseKey(char c)
    {
        if (char.IsAscii(c))
            return char.IsAsciiLetterLower(c) ? (char)(c - ('a' - 'A')) : c;

        // Ordinal case-insensitive comparison uppercases both characters, but does not use every mapping of
        // ToUpperInvariant, so the mapping is only kept when the comparison agrees with it.
        var upper = char.ToUpperInvariant(c);
        if (upper != c && !MemoryExtensions.Equals(new ReadOnlySpan<char>(in c), new ReadOnlySpan<char>(in upper), StringComparison.OrdinalIgnoreCase))
            return c;

        return upper;
    }
}
