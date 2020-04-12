using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.IO;
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

        [Pure]
        [SuppressMessage("Design", "MA0045:Do not use blocking call (make method async)", Justification = "No io operation")]
        public static IEnumerable<string> SplitLines(this string str)
        {
            if (str.Length == 0)
                return Array.Empty<string>();

            return SplitLinesImpl(str);

            static IEnumerable<string> SplitLinesImpl(string str)
            {
                using var reader = new StringReader(str);
                string? line;
                while ((line = reader.ReadLine()) != null)
                    yield return line;
            }
        }

#if NETCOREAPP3_1

        public delegate void LineAction(ReadOnlySpan<char> line, ReadOnlySpan<char> separator);
        public delegate bool CancellableLineAction(ReadOnlySpan<char> line, ReadOnlySpan<char> separator);

        [Pure]
        public static void SplitLines(this string str, LineAction action)
        {
            var span = str.AsSpan();
            Span<char> separator = stackalloc char[2];

            var start = 0;
            var i = 0;
            while (i < span.Length)
            {
                var ch = span[i];
                if (ch == '\r' || ch == '\n')
                {
                    var line = span[start..i];

                    separator[0] = ch;
                    var currentSeparator = separator.Slice(0, 1);

                    start = i + 1;
                    if (ch == '\r' && start < span.Length && span[start] == '\n')
                    {
                        separator[1] = '\n';
                        currentSeparator = separator;

                        i++;
                        start = i + 1;
                    }

                    action(line, currentSeparator);
                }

                i++;
            }

            if (i > start)
            {
                action(span[start..], ReadOnlySpan<char>.Empty);
            }
        }

        [Pure]
        public static void SplitLines(this string str, CancellableLineAction action)
        {
            var span = str.AsSpan();
            Span<char> separator = stackalloc char[2];

            var start = 0;
            var i = 0;
            while (i < span.Length)
            {
                var ch = span[i];
                if (ch == '\r' || ch == '\n')
                {
                    var line = span[start..i];

                    separator[0] = ch;
                    var currentSeparator = separator.Slice(0, 1);

                    start = i + 1;
                    if (ch == '\r' && start < span.Length && span[start] == '\n')
                    {
                        separator[1] = '\n';
                        currentSeparator = separator;

                        i++;
                        start = i + 1;
                    }

                    if (!action(line, currentSeparator))
                        return;
                }

                i++;
            }

            if (i > start)
            {
                action(span[start..], ReadOnlySpan<char>.Empty);
            }
        }
#elif NET461
#elif NETSTANDARD2_0
#else
#error Platform not supported
#endif
    }
}
