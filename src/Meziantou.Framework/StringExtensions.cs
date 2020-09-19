using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Runtime.InteropServices;
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
        internal static bool Contains(this string str, char value, StringComparison stringComparison)
        {
#if NET5_0 || NETCOREAPP3_1
            return str.Contains(value, stringComparison);
#elif NET461 || NETSTANDARD2_0
            return str.IndexOf(value.ToString(), stringComparison) >= 0;
#else
#error Platform not supported
#endif
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

            return str[^1] == c;
        }

        [Pure]
        public static bool StartsWith(this string? str, char c)
        {
            if (string.IsNullOrEmpty(str))
                return false;

            return str[0] == c;
        }

        [Pure]
        public static LineSplitEnumerator SplitLines(this string str) => new LineSplitEnumerator(str.AsSpan());

        [StructLayout(LayoutKind.Auto)]
        public ref struct LineSplitEnumerator
        {
            private ReadOnlySpan<char> _str;

            public LineSplitEnumerator(ReadOnlySpan<char> str)
            {
                _str = str;
                Current = default;
            }

            public LineSplitEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                if (_str.Length == 0)
                    return false;

                var span = _str;
                var index = span.IndexOfAny('\r', '\n');
                if (index == -1)
                {
                    _str = ReadOnlySpan<char>.Empty;
                    Current = new LineSplitEntry(span, ReadOnlySpan<char>.Empty);
                    return true;
                }

                if (index < span.Length - 1 && span[index] == '\r')
                {
                    var next = span[index + 1];
                    if (next == '\n')
                    {
                        Current = new LineSplitEntry(span.Slice(0, index), span.Slice(index, 2));
                        _str = span[(index + 2)..];
                        return true;
                    }
                }

                Current = new LineSplitEntry(span.Slice(0, index), span.Slice(index, 1));
                _str = span[(index + 1)..];
                return true;
            }

            public LineSplitEntry Current { get; private set; }
        }

        [StructLayout(LayoutKind.Auto)]
        public readonly ref struct LineSplitEntry
        {
            public LineSplitEntry(ReadOnlySpan<char> line, ReadOnlySpan<char> separator)
            {
                Line = line;
                Separator = separator;
            }

            public ReadOnlySpan<char> Line { get; }
            public ReadOnlySpan<char> Separator { get; }

            public void Deconstruct(out ReadOnlySpan<char> line, out ReadOnlySpan<char> separator)
            {
                line = Line;
                separator = Separator;
            }

            public static implicit operator ReadOnlySpan<char>(LineSplitEntry entry) => entry.Line;
        }
    }
}
