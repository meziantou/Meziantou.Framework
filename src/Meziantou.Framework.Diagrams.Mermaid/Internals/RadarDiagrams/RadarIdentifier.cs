namespace Meziantou.Framework.Diagrams.Mermaid.Internals.RadarDiagrams;

/// <summary>The identifiers of the axes and of the curves of the radar charts: the <c>ID</c> terminal of the Langium grammars of Mermaid.</summary>
internal static class RadarIdentifier
{
    /// <summary>Returns the length of the identifier at a position: ASCII letters, digits, underscores, and dashes that are not at its end.</summary>
    public static int GetLength(string text, int start)
    {
        if (start >= text.Length || !IsWordCharacter(text[start]))
            return 0;

        var end = start + 1;
        for (var i = start + 1; i < text.Length && (IsWordCharacter(text[i]) || text[i] == '-'); i++)
        {
            if (IsWordCharacter(text[i]))
            {
                end = i + 1;
            }
        }

        return end - start;
    }

    /// <summary>
    /// Indicates whether a text reads back as an identifier. A digit starts a number. The keywords, and the words that
    /// start with the terminals Mermaid reads before the identifiers (<c>true</c>, <c>false</c>, <c>circle</c>,
    /// <c>polygon</c>, <c>title</c>), are not identifiers.
    /// </summary>
    public static bool IsValid(string text) => text.Length > 0 && GetLength(text, 0) == text.Length && IsValidAt(text, 0, text.Length);

    public static bool IsValidAt(string text, int start, int length)
    {
        if (length is 0 || char.IsAsciiDigit(text[start]))
            return false;

        var word = text.AsSpan(start, length);
        if (word is "axis" or "curve" or "showLegend" or "ticks" or "max" or "min" or "graticule" or "radar-beta" or "accTitle" or "accDescr")
            return false;

        return !word.StartsWith("true", StringComparison.Ordinal)
            && !word.StartsWith("false", StringComparison.Ordinal)
            && !word.StartsWith("circle", StringComparison.Ordinal)
            && !word.StartsWith("polygon", StringComparison.Ordinal)
            && !word.StartsWith("title", StringComparison.Ordinal);
    }

    private static bool IsWordCharacter(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
