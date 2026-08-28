namespace Meziantou.Framework;

/// <summary>Provides Unicode helper methods.</summary>
public static partial class Unicode
{
    /// <summary>Replaces confusable Unicode characters using the Unicode confusables table.</summary>
    /// <param name="str">The text to normalize.</param>
    /// <returns>The text with confusable characters replaced.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string ReplaceConfusablesCharacters(string str)
    {
        ArgumentNullException.ThrowIfNull(str);

        var start = IndexOfFirstConfusable(str);
        if (start < 0)
            return str;

        // Some replacements are longer than their source, so leave a little room to grow.
        var sb = new StringBuilder(str.Length + 16);
        sb.Append(str, 0, start);

        var index = start;
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
            }
            else
            {
                // Append from the source string rather than the Rune: there is no
                // StringBuilder.Append(Rune) overload before .NET 11, so appending the Rune
                // would box it and allocate a string for every character on net10.0.
                sb.Append(str, index, rune.Utf16SequenceLength);
            }

            index += rune.Utf16SequenceLength;
        }

        return sb.ToString();
    }

    private static int IndexOfFirstConfusable(string str)
    {
        var index = 0;
        while (index < str.Length)
        {
            if (!Rune.TryGetRuneAt(str, index, out var rune))
            {
                index++;
                continue;
            }

            if (UnicodeConfusablesData.TryGetReplacement(rune, out _))
                return index;

            index += rune.Utf16SequenceLength;
        }

        return -1;
    }

    /// <summary>Replaces a confusable Unicode character using the Unicode confusables table.</summary>
    /// <param name="rune">The character to normalize.</param>
    /// <returns>The replacement text for the character.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string ReplaceConfusablesCharacters(Rune rune)
    {
        if (UnicodeConfusablesData.TryGetReplacement(rune, out var replacement))
            return replacement ?? rune.ToString();

        return rune.ToString();
    }

    /// <summary>Replaces a confusable Unicode character using the Unicode confusables table.</summary>
    /// <param name="value">The character to normalize.</param>
    /// <returns>The replacement text for the character.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string ReplaceConfusablesCharacters(char value)
    {
        if (!Rune.TryCreate(value, out var rune))
            return value.ToString();

        if (UnicodeConfusablesData.TryGetReplacement(rune, out var replacement))
            return replacement ?? rune.ToString();

        return rune.ToString();
    }

    /// <summary>Determines whether a Unicode character has a confusable replacement.</summary>
    /// <param name="rune">The Unicode scalar value to inspect.</param>
    /// <returns><see langword="true"/> when the character is confusable; otherwise <see langword="false"/>.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
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
