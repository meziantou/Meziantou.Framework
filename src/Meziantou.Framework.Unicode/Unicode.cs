using System.Buffers;

namespace Meziantou.Framework;

/// <summary>Provides Unicode helper methods.</summary>
public static partial class Unicode
{
    private static string?[]? s_confusableReplacements;

    /// <summary>Replaces confusable Unicode characters using the Unicode confusables table.</summary>
    /// <param name="str">The text to normalize.</param>
    /// <returns>The text with confusable characters replaced.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string ReplaceConfusablesCharacters(string str)
    {
        ArgumentNullException.ThrowIfNull(str);

        return MapConfusableCharacters(str);
    }

    /// <summary>Replaces a confusable Unicode character using the Unicode confusables table.</summary>
    /// <param name="rune">The character to normalize.</param>
    /// <returns>The replacement text for the character.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string ReplaceConfusablesCharacters(Rune rune)
    {
        var mapping = UnicodeConfusablesData.FindMapping(rune.Value);
        if (mapping < 0)
            return rune.ToString();

        // The replacements are cached: a caller that maps characters one by one does not allocate a string for each.
        var replacements = s_confusableReplacements ??= new string?[UnicodeConfusablesData.Count];
        return replacements[mapping] ??= UnicodeConfusablesData.GetReplacement(mapping).ToString();
    }

    /// <summary>Replaces a confusable Unicode character using the Unicode confusables table.</summary>
    /// <param name="value">The character to normalize.</param>
    /// <returns>The replacement text for the character.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string ReplaceConfusablesCharacters(char value)
    {
        if (!Rune.TryCreate(value, out var rune))
            return value.ToString();

        return ReplaceConfusablesCharacters(rune);
    }

    /// <summary>Determines whether a Unicode character has a confusable replacement.</summary>
    /// <param name="rune">The Unicode scalar value to inspect.</param>
    /// <returns><see langword="true"/> when the character is confusable; otherwise <see langword="false"/>.</returns>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static bool IsConfusableCharacter(Rune rune)
    {
        return UnicodeConfusablesData.IsConfusable(rune.Value);
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
        var index = IndexOfFirstConfusable(value);
        if (index < 0)
            return value;

        // Some replacements are longer than their source, so leave a little room to grow.
        var sb = new StringBuilder(value.Length + 16);
        sb.Append(value, 0, index);
        while (index < value.Length)
        {
            if (!Rune.TryGetRuneAt(value, index, out var rune))
            {
                sb.Append(value[index]);
                index++;
                continue;
            }

            if (UnicodeConfusablesData.TryGetReplacement(rune.Value, out var replacement))
            {
                sb.Append(replacement);
                index += rune.Utf16SequenceLength;
                continue;
            }

            // Copy the run of characters that are not confusable at once: most of a text is not.
            var start = index;
            index += rune.Utf16SequenceLength;
            var next = IndexOfFirstConfusable(value.AsSpan(index));
            index = next < 0 ? value.Length : index + next;
            sb.Append(value, start, index - start);
        }

        return sb.ToString();
    }

    private static int IndexOfFirstConfusable(ReadOnlySpan<char> value)
    {
        var index = 0;
        while (index < value.Length)
        {
            // Most ASCII characters are not confusable: skip them with a vectorized search.
            if (value[index] < 0x80)
            {
                var next = value[index..].IndexOfAnyExcept(ConfusableSearch.AsciiNonConfusables);
                if (next < 0)
                    return -1;

                index += next;
            }

            // An unpaired surrogate is not confusable: it decodes as U+FFFD, which is not either, and is skipped.
            _ = Rune.DecodeFromUtf16(value[index..], out var rune, out var charsConsumed);
            if (UnicodeConfusablesData.IsConfusable(rune.Value))
                return index;

            index += charsConsumed;
        }

        return -1;
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

    /// <summary>Holds the search values of <see cref="IndexOfFirstConfusable(ReadOnlySpan{char})"/>, created on first use only.</summary>
    private static class ConfusableSearch
    {
        public static readonly SearchValues<char> AsciiNonConfusables = Create();

        private static SearchValues<char> Create()
        {
            Span<char> values = stackalloc char[128];
            var count = 0;
            for (var c = '\0'; c < 128; c++)
            {
                if (!UnicodeConfusablesData.IsConfusable(c))
                {
                    values[count++] = c;
                }
            }

            return SearchValues.Create(values[..count]);
        }
    }
}
