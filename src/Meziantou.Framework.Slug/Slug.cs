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

        var sb = new StringBuilder(options.MaximumLength > 0 ? Math.Min(text.Length, options.MaximumLength) : text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(rune.Value);
            if (options.IsAllowed(rune))
            {
                sb.Append(options.Replace(rune));
            }
            else if (unicodeCategory != UnicodeCategory.NonSpacingMark && options.Separator is not null && !EndsWith(sb, options.Separator))
            {
                sb.Append(options.Separator);
            }

            if (options.MaximumLength > 0 && sb.Length >= options.MaximumLength)
                break;
        }

        text = sb.ToString();
        if (options.MaximumLength > 0 && text.Length > options.MaximumLength)
        {
            text = text[..options.MaximumLength];
        }

        if (!options.CanEndWithSeparator && options.Separator is not null && text.EndsWith(options.Separator, StringComparison.Ordinal))
        {
            text = text[..^options.Separator.Length];
        }

        return text.Normalize(NormalizationForm.FormC);
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
