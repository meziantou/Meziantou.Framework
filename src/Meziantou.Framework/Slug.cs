using System;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework
{
    public static class Slug
    {
        public static string Create(string text)
        {
            return Create(text, options: null);
        }

        public static string Create(string text, SlugOptions options)
        {
            if (text == null)
                return null;

            if (options == null)
            {
                options = new SlugOptions();
            }

            if (options.EarlyTruncate && options.MaximumLength <= 0 && text.Length <= options.MaximumLength)
            {
                text = text.Substring(0, options.MaximumLength);
            }

            text = text.Normalize(NormalizationForm.FormD);

            var sb = new StringBuilder(options.MaximumLength > 0 ? Math.Min(text.Length, options.MaximumLength) : text.Length);
            for (var index = 0; index < text.Length; index++)
            {
                var ch = text[index];
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(ch);
                if (options.IsAllowed(ch))
                {
                    switch (unicodeCategory)
                    {
                        case UnicodeCategory.UppercaseLetter:
                            if (options.ToLower)
                            {
                                ch = options.Culture != null ? char.ToLower(ch, options.Culture) : char.ToLowerInvariant(ch);
                            }
                            sb.Append(options.Replace(ch));
                            break;

                        case UnicodeCategory.LowercaseLetter:
                            if (options.ToUpper)
                            {
                                ch = options.Culture != null ? char.ToUpper(ch, options.Culture) : char.ToUpperInvariant(ch);
                            }
                            sb.Append(options.Replace(ch));
                            break;

                        default:
                            sb.Append(options.Replace(ch));
                            break;
                    }
                }
                else if (unicodeCategory != UnicodeCategory.NonSpacingMark && options.Separator != null && !sb.EndsWith(options.Separator))
                {
                    sb.Append(options.Separator);
                }

                if (options.MaximumLength > 0 && sb.Length >= options.MaximumLength)
                    break;
            }

            text = sb.ToString();
            if (options.MaximumLength > 0 && text.Length > options.MaximumLength)
            {
                text = text.Substring(0, options.MaximumLength);
            }

            if (!options.CanEndWithSeparator && options.Separator != null && text.EndsWith(options.Separator, StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - options.Separator.Length);
            }

            return text.Normalize(NormalizationForm.FormC);
        }
    }
}
