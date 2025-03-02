using System.Runtime.InteropServices;

namespace Meziantou.Framework.HumanReadable.Utils;
internal static class StringUtils
{
    public static bool IsMultiLines(ReadOnlySpan<char> value) => IndexOfNewlineChar(value, out _) >= 0;

    public static SpanLineEnumerator EnumerateLines(string value) => new(value.AsSpan());
    public static SpanLineEnumerator EnumerateLines(ReadOnlySpan<char> value) => new(value);

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
        private SpanLine _current;
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
        public readonly SpanLine Current => _current;

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
                _current = new SpanLine(_remaining.Slice(0, idx), _remaining.Slice(idx, stride));
                _remaining = _remaining.Slice(idx + stride);
            }
            else
            {
                // We've reached EOF, but we still need to return 'true' for this final
                // iteration so that the caller can query the Current property once more.

                _current = new SpanLine(_remaining, []);
                _remaining = default;
                _isEnumeratorActive = false;
            }

            return true;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly ref struct SpanLine
    {
        public SpanLine(ReadOnlySpan<char> line, ReadOnlySpan<char> endOfLine)
        {
            Line = line;
            EndOfLine = endOfLine;
        }

        public ReadOnlySpan<char> Line { get; }
        public ReadOnlySpan<char> EndOfLine { get; }

        public static implicit operator ReadOnlySpan<char>(SpanLine spanLine) => spanLine.Line;

        public void Deconstruct(out ReadOnlySpan<char> line, out ReadOnlySpan<char> endofLine)
        {
            line = Line;
            endofLine = EndOfLine;
        }
    }
}
