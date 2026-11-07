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
    // The characters that are not their own case key, sorted by key. The table does not depend on the range, so creating
    // a range does not walk its characters, which would be slow for a wide range such as '[a-�]'.
    private static readonly (char[] Keys, char[] Characters) FoldedCharacters = CreateFoldedCharacters();

    private readonly CharacterRange _range;

    public IgnoreCaseCharacterRange(CharacterRange range)
    {
        _range = range;
    }

    public CharacterRange Range => _range;

    public bool Contains(char c)
    {
        if (_range.IsInRange(c))
            return true;

        // Two characters are equal when their keys are equal: 'c' matches when the range holds its key, if the key is its
        // own key, or one of the characters whose key it is
        var key = GetCaseKey(c);
        if (_range.IsInRange(key) && GetCaseKey(key) == key)
            return true;

        var (keys, characters) = FoldedCharacters;
        var index = keys.AsSpan().BinarySearch(key);
        if (index < 0)
            return false;

        while (index > 0 && keys[index - 1] == key)
        {
            index--;
        }

        for (; index < keys.Length && keys[index] == key; index++)
        {
            if (_range.IsInRange(characters[index]))
                return true;
        }

        return false;
    }

    private static (char[] Keys, char[] Characters) CreateFoldedCharacters()
    {
        var entries = new List<(char Key, char Character)>();
        for (var c = (int)char.MinValue; c <= char.MaxValue; c++)
        {
            var key = GetCaseKey((char)c);
            if (key != c)
            {
                entries.Add((key, (char)c));
            }
        }

        entries.Sort();
        return ([.. entries.Select(entry => entry.Key)], [.. entries.Select(entry => entry.Character)]);
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
