using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework
{
    public static class StringExtensions
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
        public static bool EqualsIgnoreCase(this string? str1, string? str2)
        {
            return string.Equals(str1, str2, StringComparison.OrdinalIgnoreCase);
        }

        [Pure]
        public static bool Contains(this string? str, string? value, StringComparison stringComparison)
        {
            if (str == null)
                return value == null;

            if (value == null)
                return false;

            return str.IndexOf(value, stringComparison) >= 0;
        }

        [Pure]
        public static bool ContainsIgnoreCase(this string? str, string? value)
        {
            return Contains(str, value, StringComparison.OrdinalIgnoreCase);
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
        [return: NotNullIfNotNull(parameterName: "str")]
        public static string? Replace(this string? str, string oldValue, string newValue, StringComparison comparison)
        {
            if (str == null)
                return null;

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
