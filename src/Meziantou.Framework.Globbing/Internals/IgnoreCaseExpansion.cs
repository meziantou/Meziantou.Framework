namespace Meziantou.Framework.Globbing.Internals;

internal static class IgnoreCaseExpansion
{
    /// <summary>
    ///     Expands <paramref name="characters"/> with every character that <see cref="StringComparison.OrdinalIgnoreCase"/>
    ///     considers equal to one of them.
    /// </summary>
    /// <remarks>
    ///     The result feeds prefilters that reject a path before the segment is matched, so it must not miss any
    ///     character. Ordinal case-insensitive comparison relates more than two characters to each other (it treats
    ///     'Σ', 'σ' and 'ς' as equal, for instance), and <see cref="char.ToLowerInvariant(char)"/> and
    ///     <see cref="char.ToUpperInvariant(char)"/> do not enumerate all of them. The expansion is exact for ASCII
    ///     characters, as no other character is ordinal case-insensitive equal to an ASCII one, so anything else
    ///     returns <see langword="false"/> and the caller must drop the prefilter instead of using an incomplete set.
    /// </remarks>
    public static bool TryExpand(ReadOnlySpan<char> characters, bool ignoreCase, [NotNullWhen(true)] out char[]? result)
    {
        if (!ignoreCase)
        {
            result = characters.ToArray();
            return true;
        }

        foreach (var character in characters)
        {
            if (!char.IsAscii(character))
            {
                result = null;
                return false;
            }
        }

        var expanded = new HashSet<char>(characters.Length * 2);
        foreach (var character in characters)
        {
            expanded.Add(char.ToLowerInvariant(character));
            expanded.Add(char.ToUpperInvariant(character));
        }

        result = [.. expanded];
        return true;
    }
}
