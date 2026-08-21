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
        foreach (var rune in text.EnumerateRunes())
        {
            if (options.IsAllowed(rune))
            {
                // Append the replacement atomically, so the maximum length can never split a
                // surrogate pair or a multi-character replacement.
                var replacement = options.Replace(rune);
                if (sb.Length + replacement.Length > maximumLength)
                    break;

                sb.Append(replacement);
            }
            else if (Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark))
            {
                // Combining marks attached to a disallowed character are dropped silently instead of
                // producing a separator. A slug never starts with a separator, and separators are never repeated.
                if (sb.Length == 0 || EndsWith(sb, separator))
                    continue;

                if (sb.Length + separator.Length > maximumLength)
                    break;

                sb.Append(separator);
            }
        }

        var slug = sb.ToString();
        if (!options.CanEndWithSeparator && separator.Length > 0 && slug.EndsWith(separator, StringComparison.Ordinal))
        {
            slug = slug[..^separator.Length];
        }

        return slug.Normalize(NormalizationForm.FormC);
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
