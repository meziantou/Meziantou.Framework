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

        var sb = new StringBuilder(Math.Min(text.Length, maximumLength));
        var budget = maximumLength;
        int trailingSeparatorStart;
        string slug;
        while (true)
        {
            var completed = AppendSlug(sb, text, options, separator, budget, out trailingSeparatorStart);
            slug = sb.ToString().Normalize(NormalizationForm.FormC);
            if (completed)
                break;

            // The slug is built decomposed but returned composed, and composing it merges each combining
            // mark back into the character it follows. The buffer can therefore be full while the composed
            // slug is still under the limit. Grant back exactly the characters composition recovered and
            // rebuild: the limit stays an upper bound on the composed slug, but it is actually filled.
            var recovered = sb.Length - slug.Length;
            if (recovered == 0 || maximumLength > int.MaxValue - recovered)
                break;

            var extendedBudget = maximumLength + recovered;
            if (extendedBudget <= budget)
                break;

            budget = extendedBudget;
        }

        // Only a separator AppendSlug emitted is trimmed. A character that came from the input is content, even
        // when it matches the separator, so CanEndWithSeparator never deletes something the caller typed.
        if (!options.CanEndWithSeparator && separator.Length > 0 && trailingSeparatorStart >= 0)
        {
            sb.Length = trailingSeparatorStart;
            slug = sb.ToString().Normalize(NormalizationForm.FormC);
        }

        return slug;
    }

    /// <summary>Rebuilds the slug into <paramref name="sb"/>, using at most <paramref name="budget"/> characters.</summary>
    /// <param name="trailingSeparatorStart">
    /// The offset of the separator this method emitted at the end of <paramref name="sb"/>, or -1 when the buffer does
    /// not end with one. A separator character that came from the input is content and never reported here.
    /// </param>
    /// <returns><see langword="true"/> if the whole text was consumed; otherwise, <see langword="false"/>.</returns>
    private static bool AppendSlug(StringBuilder sb, string text, SlugOptions options, string separator, int budget, out int trailingSeparatorStart)
    {
        sb.Clear();

        // Offset of the most recent separator this method emitted. It is not cleared when more characters are
        // appended, so that dropping a character below still leaves it describing the buffer correctly.
        var lastSeparatorStart = -1;
        var usesDefaultReplace = options.UsesDefaultReplace;
        Span<char> transformed = stackalloc char[MaxUtf16CharsPerRune];
        var characterStart = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var isCombiningMark = Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
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

                // A combining mark belongs to the character before it, so only a rune that is not one
                // starts a new character.
                if (!isCombiningMark)
                    characterStart = sb.Length;

                if (sb.Length + replacement.Length > budget)
                {
                    // Keeping a base character while dropping the mark that follows it would silently
                    // strip the accent off the last character of the slug. Drop the character instead.
                    if (isCombiningMark)
                        sb.Length = characterStart;

                    trailingSeparatorStart = TrailingSeparatorStart(sb, lastSeparatorStart, separator);
                    return false;
                }

                sb.Append(replacement);
            }
            else if (!isCombiningMark)
            {
                // Combining marks attached to a disallowed character are dropped silently instead of
                // producing a separator. A slug never starts with a separator, and separators are never repeated.
                if (sb.Length == 0 || EndsWithEmittedSeparator(sb, lastSeparatorStart, separator))
                    continue;

                if (sb.Length + separator.Length > budget)
                {
                    trailingSeparatorStart = TrailingSeparatorStart(sb, lastSeparatorStart, separator);
                    return false;
                }

                lastSeparatorStart = sb.Length;
                sb.Append(separator);
                characterStart = sb.Length;
            }
        }

        trailingSeparatorStart = TrailingSeparatorStart(sb, lastSeparatorStart, separator);
        return true;
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
