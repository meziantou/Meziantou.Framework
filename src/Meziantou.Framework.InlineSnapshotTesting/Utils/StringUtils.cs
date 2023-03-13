using System.Runtime.InteropServices;
using System.Text;

namespace Meziantou.Framework.InlineSnapshotTesting.Utils;
internal static class StringUtils
{
#if NETSTANDARD2_0
    public static bool Contains(this string str, char value, StringComparison stringComparison)
    {
        if(stringComparison != StringComparison.Ordinal)
            throw new ArgumentOutOfRangeException(nameof(stringComparison));

        return str.IndexOf(value) != -1;
    }

    [SuppressMessage("Usage", "MA0074:Avoid implicit culture-sensitive methods", Justification = "Does not apply")]
    public static string Replace(this string str, string oldValue, string newValue, StringComparison stringComparison)
    {
        if(stringComparison != StringComparison.Ordinal)
            throw new ArgumentOutOfRangeException(nameof(stringComparison));

        return str.Replace(oldValue, newValue);
    }
#endif

    public static SpanLineEnumerator EnumerateLines(string value) => new(value.AsSpan());
    public static SpanLineEnumerator EnumerateLines(ReadOnlySpan<char> value) => new(value);

    public static string ReplaceLineEndings(string value, string replacementText)
    {
        if (replacementText is null)
            throw new ArgumentNullException(nameof(replacementText));

        // Early-exit: do we need to do anything at all?
        // If not, return this string as-is.
        var idxOfFirstNewlineChar = IndexOfNewlineChar(value.AsSpan(), out var stride);
        if (idxOfFirstNewlineChar < 0)
            return value;

        // While writing to the builder, we don't bother memcpying the first
        // or the last segment into the builder. We'll use the builder only
        // for the intermediate segments, then we'll sandwich everything together
        // with one final string.Concat call.
        var firstSegment = value.AsSpan(0, idxOfFirstNewlineChar);
        var remaining = value.AsSpan(idxOfFirstNewlineChar + stride);

        var builder = new StringBuilder();
        while (true)
        {
            var idx = IndexOfNewlineChar(remaining, out stride);
            if (idx < 0)
                break; builder.Append(replacementText);
            builder.Append(remaining.Slice(0, idx));
            remaining = remaining.Slice(idx + stride);
        }

#if NET6_0_OR_GREATER
        var retVal = string.Concat(firstSegment, builder.ToString(), replacementText, remaining);
#else
        var retVal = string.Concat(firstSegment.ToString(), builder.ToString(), replacementText, remaining.ToString());
#endif
        return retVal;
    }

    // https://github.com/dotnet/runtime/pull/53115/files#diff-7e02dbe3fd1a8d2b0c52c18e7af8d0dd2e0a5505df37ff01cc2edeebc3224fe7R1248
    private static int IndexOfNewlineChar(ReadOnlySpan<char> text, out int stride)
    {
        const string Needles = "\r\n\f\u0085\u2028\u2029";

        stride = default;
        var idx = text.IndexOfAny(Needles.AsSpan());
        if ((uint)idx < (uint)text.Length)
        {
            stride = 1; // needle found

            // Did we match CR? If so, and if it's followed by LF, then we need
            // to consume both chars as a single newline function match.

            if (text[idx] == '\r')
            {
                var nextCharIdx = idx + 1;
                if ((uint)nextCharIdx < (uint)text.Length && text[nextCharIdx] == '\n')
                    stride = 2;
            }
        }

        return idx;
    }

    [StructLayout(LayoutKind.Auto)]
    public ref struct SpanLineEnumerator
    {
        private ReadOnlySpan<char> _remaining;
        private ReadOnlySpan<char> _current;
        private bool _isEnumeratorActive;

        internal SpanLineEnumerator(ReadOnlySpan<char> buffer)
        {
            _remaining = buffer;
            _current = default;
            _isEnumeratorActive = true;
        }

        /// <summary>
        /// Gets the line at the current position of the enumerator.
        /// </summary>
        public readonly ReadOnlySpan<char> Current => _current;

        /// <summary>
        /// Returns this instance as an enumerator.
        /// </summary>
        public readonly SpanLineEnumerator GetEnumerator() => this;

        /// <summary>
        /// Advances the enumerator to the next line of the span.
        /// </summary>
        /// <returns>
        /// True if the enumerator successfully advanced to the next line; false if
        /// the enumerator has advanced past the end of the span.
        /// </returns>
        public bool MoveNext()
        {
            if (!_isEnumeratorActive)
                return false; // EOF previously reached or enumerator was never initialized

            var idx = IndexOfNewlineChar(_remaining, out var stride);
            if (idx >= 0)
            {
                _current = _remaining.Slice(0, idx);
                _remaining = _remaining.Slice(idx + stride);
            }
            else
            {
                // We've reached EOF, but we still need to return 'true' for this final
                // iteration so that the caller can query the Current property once more.

                _current = _remaining;
                _remaining = default;
                _isEnumeratorActive = false;
            }

            return true;
        }
    }
}
