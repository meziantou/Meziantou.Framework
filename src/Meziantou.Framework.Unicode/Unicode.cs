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

    /// <summary>Computes the skeleton of a string, as defined by Unicode Technical Standard #39.</summary>
    /// <param name="value">The text to reduce. Must be well-formed UTF-16.</param>
    /// <returns>The skeleton of <paramref name="value"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> contains an unpaired surrogate.</exception>
    /// <remarks>
    /// The skeleton is <c>toNFD(toConfusable(toNFD(X)))</c>. It is a comparison key and nothing more:
    /// it is not displayable text, and it must not be shown to users or stored in place of the
    /// original. Two strings are confusable when their skeletons are equal, which is what
    /// <see cref="AreConfusable(string, string)"/> tests.
    /// <para>
    /// Both normalization passes matter. Without the first, a decomposed string is not folded the
    /// same way as its composed form; without the second, the mapped result is not in a canonical
    /// form. The confusables table is closed under its own mapping, so a single mapping pass between
    /// them is sufficient.
    /// </para>
    /// </remarks>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string GetConfusableSkeleton(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
            return value;

        ThrowIfNotWellFormed(value, nameof(value));

        // The normalization is the one of this library, not the one of the platform: the skeleton does not depend on
        // the version of ICU, and it is available in globalization-invariant mode.
        var decomposed = UnicodeNormalization.Normalize(value, NormalizationForm.FormD);
        var mapped = MapConfusableCharacters(decomposed);
        return UnicodeNormalization.Normalize(mapped, NormalizationForm.FormD);
    }

    /// <summary>Determines whether two strings are visually confusable, as defined by Unicode Technical Standard #39.</summary>
    /// <param name="a">The first string. Must be well-formed UTF-16.</param>
    /// <param name="b">The second string. Must be well-formed UTF-16.</param>
    /// <returns><see langword="true"/> when both strings have the same skeleton; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">The strings are different and one of them contains an unpaired surrogate.</exception>
    /// <remarks>
    /// This compares whole strings character by character after reduction. It does not consider
    /// script mixing, so two strings drawn from different scripts that are not individually
    /// confusable are still reported as not confusable.
    /// </remarks>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static bool AreConfusable(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        return string.Equals(GetConfusableSkeleton(a), GetConfusableSkeleton(b), StringComparison.Ordinal);
    }

    private static string MapConfusableCharacters(string value)
    {
        StringBuilder? sb = null;
        var index = 0;
        while (index < value.Length)
        {
            if (!Rune.TryGetRuneAt(value, index, out var rune))
            {
                sb?.Append(value[index]);
                index++;
                continue;
            }

            if (UnicodeConfusablesData.TryGetReplacement(rune, out var replacement))
            {
                sb ??= new StringBuilder(value.Length + 16).Append(value, 0, index);
                sb.Append(replacement);
            }
            else
            {
                sb?.Append(value, index, rune.Utf16SequenceLength);
            }

            index += rune.Utf16SequenceLength;
        }

        return sb?.ToString() ?? value;
    }

    private static void ThrowIfNotWellFormed(string value, string parameterName)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var c = value[index];
            if (!char.IsSurrogate(c))
                continue;

            if (char.IsHighSurrogate(c) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                continue;
            }

            throw new ArgumentException("The string contains an unpaired surrogate", parameterName);
        }
    }
}
