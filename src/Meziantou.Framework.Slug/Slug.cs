using System.Globalization;
using System.Text;

namespace Meziantou.Framework;

public static class Slug
{
    [return: NotNullIfNotNull(parameterName: "text")]
    public static string? Create(string? text)
    {
        return Create(text, options: null);
    }

    [return: NotNullIfNotNull(parameterName: "text")]
    public static string? Create(string? text, SlugOptions? options)
    {
        if (text == null)
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
            else if (unicodeCategory != UnicodeCategory.NonSpacingMark && options.Separator != null && !EndsWith(sb, options.Separator))
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

        if (!options.CanEndWithSeparator && options.Separator != null && text.EndsWith(options.Separator, StringComparison.Ordinal))
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
