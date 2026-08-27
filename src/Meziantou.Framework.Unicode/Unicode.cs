namespace Meziantou.Framework;

/// <summary>Provides Unicode helper methods.</summary>
public static partial class Unicode
{
    /// <summary>Replaces characters that look like another character with their canonical form.</summary>
    /// <param name="str">The text to normalize.</param>
    /// <returns>The normalized text. Characters that are already ASCII are returned unchanged.</returns>
    /// <remarks>
    /// The mapping is derived from the Unicode confusables table, excluding sources that are already
    /// ASCII, so ordinary text such as <c>"Item 1 of 10"</c> is returned unchanged.
    /// <para>
    /// The result is displayable text, not a comparison key. It is deliberately not the skeleton
    /// defined by UTS #39: the input is not normalized and the mapping is applied in a single pass,
    /// so two strings that a reader would consider confusable can still produce different results.
    /// </para>
    /// </remarks>
    public static string ReplaceConfusablesCharacters(string str)
    {
        ArgumentNullException.ThrowIfNull(str);

        if (str.Length == 0)
            return str;

        var sb = new StringBuilder(str.Length);
        var index = 0;
        var hasReplacement = false;

        while (index < str.Length)
        {
            if (!Rune.TryGetRuneAt(str, index, out var rune))
            {
                sb.Append(str[index]);
                index++;
                continue;
            }

            if (UnicodeConfusablesData.TryGetReplacement(rune, out var replacement))
            {
                sb.Append(replacement);
                hasReplacement = true;
            }
            else
            {
                sb.Append(rune);
            }

            index += rune.Utf16SequenceLength;
        }

        if (!hasReplacement)
            return str;

        return sb.ToString();
    }

    /// <summary>Replaces a character that looks like another character with its canonical form.</summary>
    /// <param name="rune">The character to normalize.</param>
    /// <returns>The replacement text, or the character itself when it has no replacement.</returns>
    /// <remarks>ASCII characters are never replaced.</remarks>
    public static string ReplaceConfusablesCharacters(Rune rune)
    {
        if (UnicodeConfusablesData.TryGetReplacement(rune, out var replacement))
            return replacement ?? rune.ToString();

        return rune.ToString();
    }

    /// <summary>Replaces a character that looks like another character with its canonical form.</summary>
    /// <param name="value">The character to normalize.</param>
    /// <returns>The replacement text, or the character itself when it has no replacement.</returns>
    /// <remarks>
    /// ASCII characters are never replaced. A <see cref="char"/> cannot represent a code point above
    /// U+FFFF, so use the <see cref="Rune"/> or <see cref="string"/> overload to normalize those.
    /// </remarks>
    public static string ReplaceConfusablesCharacters(char value)
    {
        if (!Rune.TryCreate(value, out var rune))
            return value.ToString();

        if (UnicodeConfusablesData.TryGetReplacement(rune, out var replacement))
            return replacement ?? rune.ToString();

        return rune.ToString();
    }

    /// <summary>Determines whether a Unicode character has a replacement in the confusables table.</summary>
    /// <param name="rune">The Unicode scalar value to inspect.</param>
    /// <returns><see langword="true"/> when the character has a replacement; otherwise <see langword="false"/>.</returns>
    /// <remarks>Always returns <see langword="false"/> for ASCII characters, which are never replaced.</remarks>
    public static bool IsConfusableCharacter(Rune rune)
    {
        return UnicodeConfusablesData.TryGetReplacement(rune, out _);
    }

    public static IReadOnlyCollection<UnicodeCharacterInfo> AllCharacters => UnicodeCharacterInfos.AllCharacters;

    /// <summary>Gets information about a Unicode character.</summary>
    /// <param name="rune">The Unicode scalar value to inspect.</param>
    /// <returns>The character information, or <see langword="null"/> when not found.</returns>
    public static UnicodeCharacterInfo? GetCharacterInfo(Rune rune)
    {
        if (!UnicodeCharacterInfos.TryGetInfo(rune, out var info))
            return null;

        return info;
    }

    /// <summary>Gets information about a Unicode character.</summary>
    /// <param name="value">The Unicode scalar value to inspect.</param>
    /// <returns>The character information, or <see langword="null"/> when not found.</returns>
    public static UnicodeCharacterInfo? GetCharacterInfo(char value)
    {
        if (!Rune.TryCreate(value, out var rune))
            return null;

        return GetCharacterInfo(rune);
    }

    /// <summary>Tries to get information about a Unicode character.</summary>
    /// <param name="rune">The Unicode scalar value to inspect.</param>
    /// <param name="info">The character information when found.</param>
    /// <returns><see langword="true"/> when the character exists in the Unicode data; otherwise <see langword="false"/>.</returns>
    public static bool TryGetCharacterInfo(Rune rune, out UnicodeCharacterInfo info)
    {
        return UnicodeCharacterInfos.TryGetInfo(rune, out info);
    }

    /// <summary>Tries to get information about a Unicode character.</summary>
    /// <param name="value">The Unicode scalar value to inspect.</param>
    /// <param name="info">The character information when found.</param>
    /// <returns><see langword="true"/> when the character exists in the Unicode data; otherwise <see langword="false"/>.</returns>
    public static bool TryGetCharacterInfo(char value, out UnicodeCharacterInfo info)
    {
        if (!Rune.TryCreate(value, out var rune))
        {
            info = default;
            return false;
        }

        return TryGetCharacterInfo(rune, out info);
    }
}
