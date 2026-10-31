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
    /// The skeleton is <c>bidiSkeleton(LTR, X)</c>: the text as displayed in a left-to-right paragraph, reduced by
    /// <c>internalSkeleton</c>. See <see cref="GetConfusableSkeleton(string, UnicodeBidiDirection)"/>. It is a comparison key and nothing more:
    /// it is not displayable text, and it must not be shown to users or stored in place of the
    /// original. Two strings are confusable when their skeletons are equal, which is what
    /// <see cref="AreConfusable(string, string)"/> tests.
    /// </remarks>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string GetConfusableSkeleton(string value)
    {
        return GetConfusableSkeleton(value, UnicodeBidiDirection.LeftToRight);
    }

    /// <summary>Computes the bidirectional skeleton of a string for a paragraph direction, as defined by Unicode Technical Standard #39.</summary>
    /// <param name="value">The text to reduce. Must be well-formed UTF-16.</param>
    /// <param name="direction">
    /// The direction of the paragraph the text is displayed in: <see cref="UnicodeBidiDirection.LeftToRight"/> (LTR),
    /// <see cref="UnicodeBidiDirection.RightToLeft"/> (RTL), or <see cref="UnicodeBidiDirection.Auto"/> for the
    /// direction of the first strong character (FS).
    /// </param>
    /// <returns>The skeleton of <paramref name="value"/>, <c>bidiSkeleton(direction, X)</c>.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> contains an unpaired surrogate.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a defined value.</exception>
    /// <remarks>
    /// <para>
    /// The text is first put in display order with <see cref="UnicodeBidi"/>: rules up to L2 of the bidirectional
    /// algorithm, then L3 (the combining marks of a right-to-left character follow it) and L4 (the characters
    /// displayed with a mirrored glyph are replaced by their <c>Bidi_Mirroring_Glyph</c>). Each paragraph of the text
    /// is a single line. The result is then reduced by <c>internalSkeleton</c>:
    /// <c>toNFD(toConfusable(removeDefaultIgnorable(toNFD(X))))</c>.
    /// </para>
    /// <para>
    /// The skeleton is a comparison key and nothing more: it is not displayable text, and it must not be shown to
    /// users or stored in place of the original.
    /// </para>
    /// </remarks>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static string GetConfusableSkeleton(string value, UnicodeBidiDirection direction)
    {
        ArgumentNullException.ThrowIfNull(value);
        ThrowIfInvalidDirection(direction);

        if (value.Length == 0)
            return value;

        ThrowIfNotWellFormed(value, nameof(value));

        var reordered = ReorderForSkeleton(value, direction);

        // The normalization is the one of this library, not the one of the platform: the skeleton does not depend on
        // the version of ICU, and it is available in globalization-invariant mode.
        // Both normalization passes matter. Without the first, a decomposed string is not folded the same way as its
        // composed form; without the second, the mapped result is not in a canonical form. The confusables table is
        // closed under its own mapping, so a single mapping pass between them is sufficient.
        var decomposed = UnicodeNormalization.Normalize(reordered, NormalizationForm.FormD);
        var visible = RemoveDefaultIgnorables(decomposed);
        var mapped = MapConfusableCharacters(visible);
        return UnicodeNormalization.Normalize(mapped, NormalizationForm.FormD);
    }

    /// <summary>Determines whether two strings are visually confusable, as defined by Unicode Technical Standard #39.</summary>
    /// <param name="a">The first string. Must be well-formed UTF-16.</param>
    /// <param name="b">The second string. Must be well-formed UTF-16.</param>
    /// <returns><see langword="true"/> when both strings have the same skeleton; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">The strings are different and one of them contains an unpaired surrogate.</exception>
    /// <remarks>
    /// The strings are compared as displayed in a left-to-right paragraph (<see cref="GetConfusableSkeleton(string)"/>).
    /// This compares whole strings character by character after reduction. It does not consider
    /// script mixing, so two strings drawn from different scripts that are not individually
    /// confusable are still reported as not confusable.
    /// </remarks>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static bool AreConfusable(string a, string b)
    {
        return AreConfusable(a, b, UnicodeBidiDirection.LeftToRight);
    }

    /// <summary>Determines whether two strings are visually confusable when displayed in a paragraph of a given direction, as defined by Unicode Technical Standard #39.</summary>
    /// <param name="a">The first string. Must be well-formed UTF-16.</param>
    /// <param name="b">The second string. Must be well-formed UTF-16.</param>
    /// <param name="direction">The direction of the paragraph: see <see cref="GetConfusableSkeleton(string, UnicodeBidiDirection)"/>.</param>
    /// <returns><see langword="true"/> when both strings have the same skeleton for <paramref name="direction"/>; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException">The strings are different and one of them contains an unpaired surrogate.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a defined value.</exception>
    /// <seealso href="https://unicode.org/reports/tr39/" />
    public static bool AreConfusable(string a, string b, UnicodeBidiDirection direction)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        ThrowIfInvalidDirection(direction);

        if (string.Equals(a, b, StringComparison.Ordinal))
            return true;

        return string.Equals(GetConfusableSkeleton(a, direction), GetConfusableSkeleton(b, direction), StringComparison.Ordinal);
    }

    private static void ThrowIfInvalidDirection(UnicodeBidiDirection direction)
    {
        if (direction is not (UnicodeBidiDirection.Auto or UnicodeBidiDirection.LeftToRight or UnicodeBidiDirection.RightToLeft))
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "The value is not defined");
    }

    /// <summary>The first steps of <c>bidiSkeleton</c>: the text in display order, with rules L3 and L4 applied.</summary>
    private static string ReorderForSkeleton(string value, UnicodeBidiDirection direction)
    {
        // Fast path: every level is 0, so the display order is the order of the text and nothing is mirrored.
        if (direction is not UnicodeBidiDirection.RightToLeft && !UnicodeBidi.MayRequireBidi(value))
            return value;

        var result = new StringBuilder(value.Length);
        var start = 0;
        for (var index = 0; index < value.Length;)
        {
            var codePoint = DecodeAt(value, index, out var length);
            index += length;
            if (UnicodeProperties.GetBidiClass(codePoint) is UnicodeBidirectionalCategory.ParagraphSeparator)
            {
                AppendParagraphInDisplayOrder(result, value.AsSpan(start, index - start), direction);
                start = index;
            }
        }

        if (start < value.Length)
        {
            AppendParagraphInDisplayOrder(result, value.AsSpan(start), direction);
        }

        return result.ToString();
    }

    private static void AppendParagraphInDisplayOrder(StringBuilder result, ReadOnlySpan<char> paragraph, UnicodeBidiDirection direction)
    {
        if (direction is not UnicodeBidiDirection.RightToLeft && !UnicodeBidi.MayRequireBidi(paragraph))
        {
            result.Append(paragraph);
            return;
        }

        var levels = ArrayPool<byte>.Shared.Rent(paragraph.Length);
        var order = ArrayPool<int>.Shared.Rent(paragraph.Length);
        try
        {
            var paragraphLevel = UnicodeBidi.ResolveLevels(paragraph, direction, levels);

            // The paragraph is a single line.
            UnicodeBidi.ResetLineLevels(paragraph, paragraphLevel, levels);
            UnicodeBidi.GetVisualOrder(paragraph, levels, order);

            // Keep the index of the first code unit of each code point: a pair is in order, so its second code unit
            // follows its first one.
            var count = 0;
            for (var i = 0; i < paragraph.Length; i++)
            {
                var index = order[i];
                order[count++] = index;
                if (char.IsHighSurrogate(paragraph[index]) && index + 1 < paragraph.Length && char.IsLowSurrogate(paragraph[index + 1]))
                {
                    i++;
                }
            }

            for (var i = 0; i < count;)
            {
                var level = levels[order[i]];
                var end = i + 1;

                // L3: the combining marks of a right-to-left character are displayed before it. Put the character
                // first, followed by its marks in the order of the text.
                if ((level & 1) != 0 && IsCombiningMark(DecodeAt(paragraph, order[i], out _)))
                {
                    var baseIndex = i + 1;
                    while (baseIndex < count && levels[order[baseIndex]] == level && IsCombiningMark(DecodeAt(paragraph, order[baseIndex], out _)))
                    {
                        baseIndex++;
                    }

                    if (baseIndex < count && levels[order[baseIndex]] == level)
                    {
                        order.AsSpan(i, baseIndex - i + 1).Reverse();
                        end = baseIndex + 1;
                    }
                    else
                    {
                        end = baseIndex;
                    }
                }

                for (; i < end; i++)
                {
                    var codePoint = DecodeAt(paragraph, order[i], out _);

                    // L4: a right-to-left character is displayed with its mirrored glyph.
                    if ((levels[order[i]] & 1) != 0 && UnicodeProperties.TryGetBidiMirroringGlyph(codePoint, out var mirrored))
                    {
                        codePoint = mirrored;
                    }

                    AppendCodePoint(result, codePoint);
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(order);
            ArrayPool<byte>.Shared.Return(levels);
        }
    }

    private static bool IsCombiningMark(int codePoint)
    {
        return UnicodeProperties.GetGeneralCategory(codePoint) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }

    private static void AppendCodePoint(StringBuilder result, int codePoint)
    {
        Span<char> buffer = stackalloc char[2];
        var length = new Rune(codePoint).EncodeToUtf16(buffer);
        result.Append(buffer[..length]);
    }

    private static string RemoveDefaultIgnorables(string value)
    {
        var index = IndexOfFirstDefaultIgnorable(value);
        if (index < 0)
            return value;

        var sb = new StringBuilder(value.Length);
        sb.Append(value, 0, index);
        while (index < value.Length)
        {
            var codePoint = DecodeAt(value, index, out var length);
            if (!UnicodeProperties.IsDefaultIgnorable(codePoint))
            {
                sb.Append(value, index, length);
            }

            index += length;
        }

        return sb.ToString();
    }

    private static int IndexOfFirstDefaultIgnorable(ReadOnlySpan<char> value)
    {
        for (var index = 0; index < value.Length;)
        {
            // The first default-ignorable code point is U+00AD: skip the characters before it with a vectorized search.
            if (value[index] < 0x00AD)
            {
                var next = value[index..].IndexOfAnyExceptInRange('\0', '\u00AC');
                if (next < 0)
                    return -1;

                index += next;
            }

            var codePoint = DecodeAt(value, index, out var length);
            if (UnicodeProperties.IsDefaultIgnorable(codePoint))
                return index;

            index += length;
        }

        return -1;
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
