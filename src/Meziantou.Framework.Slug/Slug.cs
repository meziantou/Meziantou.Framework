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
    /// <remarks>Text that is not well-formed Unicode is accepted: each ill-formed character is treated as <see cref="Rune.ReplacementChar"/>. In invariant globalization mode, where the normalizer accepts ill-formed text, such characters are left as they are.</remarks>
    [return: NotNullIfNotNull(parameterName: nameof(text))]
    public static string? Create(string? text)
    {
        return Create(text, options: null);
    }

    /// <summary>Creates a slug from the specified text using the specified options.</summary>
    /// <param name="text">The text to convert to a slug.</param>
    /// <param name="options">The options to use for slug generation, or <see langword="null"/> to use default options.</param>
    /// <returns>A slug generated from the input text, or <see langword="null"/> if <paramref name="text"/> is <see langword="null"/>.</returns>
    /// <remarks>Text that is not well-formed Unicode is accepted: each ill-formed character is treated as <see cref="Rune.ReplacementChar"/>. In invariant globalization mode, where the normalizer accepts ill-formed text, such characters are left as they are.</remarks>
    [return: NotNullIfNotNull(parameterName: nameof(text))]
    public static string? Create(string? text, SlugOptions? options)
    {
        if (text is null)
            return null;

        options ??= SlugOptions.Default;
        text = Normalize(text, NormalizationForm.FormD);

        var separator = options.Separator;
        var maximumLength = options.MaximumLength > 0 ? options.MaximumLength : int.MaxValue;

        var sb = new StringBuilder(Math.Min(text.Length, maximumLength));
        var budget = maximumLength;
        string slug;
        while (true)
        {
            var completed = AppendSlug(sb, text, options, separator, budget);
            slug = Normalize(sb.ToString(), NormalizationForm.FormC);
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

        // Trimmed on the decomposed buffer, so a separator that is itself decomposed is still recognized.
        if (!options.CanEndWithSeparator && separator.Length > 0 && EndsWith(sb, separator))
        {
            sb.Length -= separator.Length;
            slug = Normalize(sb.ToString(), NormalizationForm.FormC);
        }

        return slug;
    }

    /// <summary>Rebuilds the slug into <paramref name="sb"/>, using at most <paramref name="budget"/> characters.</summary>
    /// <returns><see langword="true"/> if the whole text was consumed; otherwise, <see langword="false"/>.</returns>
    private static bool AppendSlug(StringBuilder sb, string text, SlugOptions options, string separator, int budget)
    {
        sb.Clear();
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

                    return false;
                }

                sb.Append(replacement);
            }
            else if (!isCombiningMark)
            {
                // Combining marks attached to a disallowed character are dropped silently instead of
                // producing a separator. A slug never starts with a separator, and separators are never repeated.
                if (sb.Length == 0 || EndsWith(sb, separator))
                    continue;

                if (sb.Length + separator.Length > budget)
                    return false;

                sb.Append(separator);
                characterStart = sb.Length;
            }
        }

        return true;
    }

    /// <summary>Normalizes <paramref name="text"/>, substituting anything the normalizer rejects.</summary>
    private static string Normalize(string text, NormalizationForm normalizationForm)
    {
        try
        {
            return text.Normalize(normalizationForm);
        }
        catch (ArgumentException)
        {
            // string.Normalize rejects text that is not well-formed UTF-16 - an unpaired surrogate, which a
            // caller produces by cutting a string in the middle of a surrogate pair - and the code points the
            // normalizer treats as ill-formed. Substituting them costs nothing on the well-formed path, which
            // never reaches this handler, and lets arbitrary user text through instead of throwing.
            return ReplaceIllFormedCharacters(text).Normalize(normalizationForm);
        }
    }

    /// <summary>Replaces every character the normalizer rejects with <see cref="Rune.ReplacementChar"/>.</summary>
    private static string ReplaceIllFormedCharacters(string text)
    {
        var sb = new StringBuilder(text.Length);
        Span<char> encoded = stackalloc char[MaxUtf16CharsPerRune];

        // EnumerateRunes already yields the replacement character for an unpaired surrogate, so only the
        // noncharacters have to be substituted here.
        foreach (var rune in text.EnumerateRunes())
        {
            var replacement = IsNoncharacter(rune) ? Rune.ReplacementChar : rune;
            sb.Append(encoded[..replacement.EncodeToUtf16(encoded)]);
        }

        return sb.ToString();
    }

    /// <summary>Determines whether the rune is one of the Unicode noncharacters, which never appear in well-formed text.</summary>
    private static bool IsNoncharacter(Rune rune)
    {
        return (rune.Value & 0xFFFE) == 0xFFFE || rune.Value is >= 0xFDD0 and <= 0xFDEF;
    }

    private static bool EndsWith(StringBuilder stringBuilder, string suffix)
    {
        if (stringBuilder.Length < suffix.Length)
            return false;

        for (var index = 0; index < suffix.Length; index++)
        {
            if (stringBuilder[stringBuilder.Length - 1 - index] != suffix[suffix.Length - 1 - index])
                return false;
        }

        return true;
    }
}
