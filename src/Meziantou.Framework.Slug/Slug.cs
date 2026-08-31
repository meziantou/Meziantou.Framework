namespace Meziantou.Framework;

/// <summary>
/// Provides methods for generating URL-friendly slugs from text.
/// <example>
/// <code>
/// var slug = Slug.Create("This is a test!"); // "This-is-a-test"
/// var lowerSlug = Slug.Create("Hello World", new SlugOptions { CasingTransformation = CasingTransformation.ToLowerCase }); // "hello-world"
/// </code>
/// </example>
/// </summary>
public static class Slug
{
    /// <summary>The number of UTF-16 characters needed to encode any single rune.</summary>
    private const int MaxUtf16CharsPerRune = 2;

    /// <summary>Extra buffer room for the character that turns out not to fit and is removed again.</summary>
    private const int TruncationHeadroom = 8;

    // A Hangul syllable decomposes into a leading jamo, a vowel jamo and an optional trailing jamo. Those are
    // letters rather than marks, so the mark test alone does not recognize them as part of the same character.
    private const int HangulLeadingJamoFirst = 0x1100;
    private const int HangulLeadingJamoLast = 0x1112;
    private const int HangulVowelJamoFirst = 0x1161;
    private const int HangulVowelJamoLast = 0x1175;
    private const int HangulTrailingJamoFirst = 0x11A8;
    private const int HangulTrailingJamoLast = 0x11C2;

    /// <summary>Creates a slug from the specified text using default options.</summary>
    /// <param name="text">The text to convert to a slug.</param>
    /// <returns>A slug generated from the input text, or <see langword="null"/> if <paramref name="text"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(parameterName: nameof(text))]
    public static string? Create(string? text)
    {
        return Create(text, options: null);
    }

    /// <summary>Creates a slug from the specified text using the specified options.</summary>
    /// <param name="text">The text to convert to a slug.</param>
    /// <param name="options">The options to use for slug generation, or <see langword="null"/> to use default options.</param>
    /// <returns>A slug generated from the input text, or <see langword="null"/> if <paramref name="text"/> is <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(parameterName: nameof(text))]
    public static string? Create(string? text, SlugOptions? options)
    {
        if (text is null)
            return null;

        options ??= SlugOptions.Default;
        text = text.Normalize(NormalizationForm.FormD);

        var separator = options.Separator;
        var maximumLength = options.MaximumLength > 0 ? options.MaximumLength : int.MaxValue;

        // AppendSlug writes a character before it can measure it, so the buffer briefly holds one character more
        // than the limit allows. Sizing for that keeps the common case in a single StringBuilder chunk.
        var capacity = text.Length <= maximumLength ? text.Length : Math.Min(text.Length, maximumLength + TruncationHeadroom);
        var sb = new StringBuilder(capacity);
        AppendSlug(sb, text, options, separator, maximumLength, out var trailingSeparatorStart);

        // Only a separator AppendSlug emitted is trimmed. A character that came from the input is content, even
        // when it matches the separator, so CanEndWithSeparator never deletes something the caller typed.
        if (!options.CanEndWithSeparator && separator.Length > 0 && trailingSeparatorStart >= 0)
            sb.Length = trailingSeparatorStart;

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Builds the slug into <paramref name="sb"/>, keeping the composed result within <paramref name="maximumLength"/>.</summary>
    /// <param name="trailingSeparatorStart">
    /// The offset of the separator this method emitted at the end of <paramref name="sb"/>, or -1 when the buffer does
    /// not end with one. A separator character that came from the input is content and never reported here.
    /// </param>
    /// <remarks>
    /// The buffer is decomposed while the returned slug is composed, and composing merges each combining mark back into
    /// the character it follows, so the two have different lengths. The limit applies to the composed slug, so the budget
    /// is spent in composed characters: each character is measured once it is complete, and dropped whole when it does
    /// not fit. That keeps the limit an upper bound on the result while still filling it, in a single pass over the text.
    /// </remarks>
    private static void AppendSlug(StringBuilder sb, string text, SlugOptions options, string separator, int maximumLength, out int trailingSeparatorStart)
    {
        sb.Clear();

        // Offset of the most recent separator this method emitted. It is not cleared when more characters are
        // appended, so that dropping a character below still leaves it describing the buffer correctly.
        var lastSeparatorStart = -1;
        var usesDefaultReplace = options.UsesDefaultReplace;
        Span<char> transformed = stackalloc char[MaxUtf16CharsPerRune];

        // Length of the composed slug built so far, and the character still being accumulated: it is only measured
        // once the next character starts, because a combining mark can still shorten it.
        var composedLength = 0;
        var characterStart = -1;
        var characterRuneCount = 0;
        var separatorLength = separator.Length == 0 ? 0 : separator.Normalize(NormalizationForm.FormC).Length;
        var previousRuneWasHangulJamo = false;

        foreach (var rune in text.EnumerateRunes())
        {
            var isCombiningMark = Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

            // Everything that composes back into the character before it has to travel with it, so that the
            // maximum length only ever cuts between characters.
            var continuesCharacter = isCombiningMark || (previousRuneWasHangulJamo && IsHangulSyllableContinuation(rune));
            var appended = false;

            if (options.IsAllowed(rune))
            {
                // Append the replacement atomically, so the maximum length can never split a
                // surrogate pair or a multi-character replacement.
                scoped ReadOnlySpan<char> replacement;
                if (usesDefaultReplace)
                {
                    // Replace allocates a string for every rune of the input. Its default implementation
                    // is just a casing transformation, so write the result straight into the buffer instead.
                    replacement = transformed[..options.Transform(rune).EncodeToUtf16(transformed)];
                }
                else
                {
                    replacement = options.Replace(rune);
                }

                if (!continuesCharacter)
                {
                    if (!TryEndCharacter(sb, characterStart, characterRuneCount, maximumLength, ref composedLength))
                        break;

                    characterStart = sb.Length;
                    characterRuneCount = 0;
                }
                else if (characterStart < 0 || characterStart == sb.Length)
                {
                    // Nothing was written for the current character, so this continuation has no base to attach to
                    // and would leave a floating accent or a bare jamo at the start of the slug or right after a
                    // separator. Drop it, the way a disallowed character's marks are dropped below.
                    continue;
                }

                sb.Append(replacement);
                characterRuneCount++;
                appended = true;
            }
            else if (!isCombiningMark)
            {
                // A disallowed combining mark is dropped rather than emitting a separator, because it is part of
                // the character before it and not a word boundary.
                if (!TryEndCharacter(sb, characterStart, characterRuneCount, maximumLength, ref composedLength))
                    break;

                characterStart = -1;
                characterRuneCount = 0;

                // A slug never starts with a separator, and separators are never repeated.
                if (sb.Length == 0 || EndsWithEmittedSeparator(sb, lastSeparatorStart, separator))
                    continue;

                if (separatorLength > maximumLength - composedLength)
                    break;

                lastSeparatorStart = sb.Length;
                sb.Append(separator);
                composedLength += separatorLength;
            }

            previousRuneWasHangulJamo = appended && IsHangulJamo(rune);
        }

        TryEndCharacter(sb, characterStart, characterRuneCount, maximumLength, ref composedLength);
        trailingSeparatorStart = TrailingSeparatorStart(sb, lastSeparatorStart, separator);
    }

    /// <summary>
    /// Charges the character accumulated since <paramref name="characterStart"/> to <paramref name="composedLength"/>,
    /// or removes it from <paramref name="sb"/> when it no longer fits.
    /// </summary>
    /// <returns><see langword="true"/> if the character fit; otherwise, <see langword="false"/>.</returns>
    private static bool TryEndCharacter(StringBuilder sb, int characterStart, int characterRuneCount, int maximumLength, ref int composedLength)
    {
        if (characterStart < 0 || characterStart == sb.Length)
            return true;

        // A character made of a single rune has nothing to compose with, so its composed length is the length it
        // already occupies. Only the ones carrying combining marks or jamo are worth normalizing to measure.
        var length = sb.Length - characterStart;
        if (characterRuneCount > 1)
        {
            length = sb.ToString(characterStart, length).Normalize(NormalizationForm.FormC).Length;
        }

        if (length > maximumLength - composedLength)
        {
            sb.Length = characterStart;
            return false;
        }

        composedLength += length;
        return true;
    }

    /// <summary>Determines whether the rune is one of the Hangul jamo a syllable decomposes into.</summary>
    private static bool IsHangulJamo(Rune rune)
    {
        return rune.Value is (>= HangulLeadingJamoFirst and <= HangulLeadingJamoLast)
            or (>= HangulVowelJamoFirst and <= HangulVowelJamoLast)
            or (>= HangulTrailingJamoFirst and <= HangulTrailingJamoLast);
    }

    /// <summary>Determines whether the rune continues the Hangul syllable started by the jamo before it.</summary>
    private static bool IsHangulSyllableContinuation(Rune rune)
    {
        return rune.Value is (>= HangulVowelJamoFirst and <= HangulVowelJamoLast)
            or (>= HangulTrailingJamoFirst and <= HangulTrailingJamoLast);
    }

    /// <summary>Determines whether <paramref name="sb"/> ends with the separator emitted at <paramref name="lastSeparatorStart"/>.</summary>
    private static bool EndsWithEmittedSeparator(StringBuilder sb, int lastSeparatorStart, string separator)
    {
        return lastSeparatorStart >= 0 && lastSeparatorStart + separator.Length == sb.Length;
    }

    /// <summary>Returns the offset of the emitted separator at the end of <paramref name="sb"/>, or -1 when there is none.</summary>
    private static int TrailingSeparatorStart(StringBuilder sb, int lastSeparatorStart, string separator)
    {
        return EndsWithEmittedSeparator(sb, lastSeparatorStart, separator) ? lastSeparatorStart : -1;
    }

}
