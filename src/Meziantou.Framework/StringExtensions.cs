using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework
{

#if PUBLIC_STRING_EXTENSIONS
    public
#else
    internal
#endif
    static partial class StringExtensions
    {
        [Pure]
        public static string? Nullify(this string? str, bool trim)
        {
            if (str == null)
                return null;

            if (trim)
            {
                str = str.Trim();
            }

            if (string.IsNullOrEmpty(str))
                return null;

            return str;
        }

        [Pure]
        public static bool EqualsOrdinal(this string? str1, string? str2)
        {
            return string.Equals(str1, str2, StringComparison.Ordinal);
        }

        [Pure]
        public static bool EqualsIgnoreCase(this string? str1, string? str2)
        {
            return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
        }

        [Pure]
        public static bool ContainsIgnoreCase(this string str, string value)
        {
#if NETSTANDARD2_0
            return str.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
#elif NET5_0
            return str.Contains(value, StringComparison.OrdinalIgnoreCase);
#else
#error Platform not supported
#endif
        }

        [Pure]
        [return: NotNullIfNotNull(parameterName: "str")]
        public static string? RemoveDiacritics(this string? str)
        {
            if (str == null)
                return null;

            var normalizedString = str.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }

        [Pure]
        public static string Replace(this string str, string oldValue, string newValue, StringComparison comparison)
        {
            var sb = new StringBuilder();

            var previousIndex = 0;
            var index = str.IndexOf(oldValue, comparison);
            while (index != -1)
            {
                sb.Append(str, previousIndex, index - previousIndex);
                sb.Append(newValue);
                index += oldValue.Length;

                previousIndex = index;
                index = str.IndexOf(oldValue, index, comparison);
            }

            sb.Append(str, previousIndex, str.Length - previousIndex);
            return sb.ToString();
        }

        [Pure]
        [SuppressMessage("Style", "IDE0056:Use index operator", Justification = ".NET Standard 2.0 compatibility")]
        public static bool EndsWith(this string? str, char c)
        {
            if (string.IsNullOrEmpty(str))
                return false;

            return str[str.Length - 1] == c;
        }

        [Pure]
        public static bool StartsWith(this string? str, char c)
        {
            if (string.IsNullOrEmpty(str))
                return false;

            return str[0] == c;
        }
    }
}
