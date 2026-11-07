using System.Runtime.InteropServices;

#if MEZIANTOU_INLINE_SNAPSHOT_TESTING
namespace Meziantou.Framework.InlineSnapshotTesting;
#else
namespace Meziantou.Framework.SnapshotTesting;
#endif

internal static class ScrubberUtilities
{
    /// <summary>
    /// Enumerates the lines of a text as the line scrubbers see them. Only <c>\r\n</c>, <c>\r</c> and <c>\n</c> end a
    /// line: the other Unicode line separators (<c>\f</c>, U+0085, U+2028, U+2029) are content, which a line scrubber
    /// must not cut in two. A line break ends the line before it rather than starting a new one, so a text ending with
    /// a line break has no empty last line, and the empty text has no line at all.
    /// </summary>
    public static LineEnumerator EnumerateLines(string text) => new(text.AsSpan());

    /// <summary>
    /// Replaces the occurrences of <paramref name="value"/> that form a whole word, ignoring case. The user name
    /// <c>runner</c> is replaced in <c>/home/runner/work</c> or <c>runner@host</c>, but not in <c>xunit.runner.visualstudio</c>,
    /// <c>runners</c> or <c>test-runner</c>.
    /// </summary>
    /// <remarks>
    /// Letters, digits and <c>_</c> continue a word. <c>.</c> and <c>-</c> continue it only when a letter or a digit
    /// follows them (after the value) or precedes them (before the value), so a value at the end of a sentence or next
    /// to a path separator is still replaced.
    /// </remarks>
    public static string ReplaceWholeWord(string text, string value, string replacement)
    {
        if (string.IsNullOrEmpty(value) || text.Length < value.Length)
            return text;

        StringBuilder? result = null;
        var copiedUpTo = 0;
        var startIndex = 0;
        while (startIndex <= text.Length - value.Length)
        {
            var index = text.IndexOf(value, startIndex, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                break;

            var end = index + value.Length;
            if (IsWordBoundaryBefore(text, index) && IsWordBoundaryAfter(text, end))
            {
                result ??= new StringBuilder(text.Length);
                result.Append(text, copiedUpTo, index - copiedUpTo).Append(replacement);
                copiedUpTo = end;
                startIndex = end;
            }
            else
            {
                startIndex = index + 1;
            }
        }

        if (result is null)
            return text;

        result.Append(text, copiedUpTo, text.Length - copiedUpTo);
        return result.ToString();
    }

    private static bool IsWordBoundaryBefore(string text, int index)
    {
        if (index == 0)
            return true;

        var c = text[index - 1];
        if (IsWordCharacter(c))
            return false;

        return !(c is '.' or '-' && index >= 2 && char.IsLetterOrDigit(text[index - 2]));
    }

    private static bool IsWordBoundaryAfter(string text, int end)
    {
        if (end == text.Length)
            return true;

        var c = text[end];
        if (IsWordCharacter(c))
            return false;

        return !(c is '.' or '-' && end + 1 < text.Length && char.IsLetterOrDigit(text[end + 1]));
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c is '_';

    [StructLayout(LayoutKind.Auto)]
    public ref struct LineEnumerator
    {
        private ReadOnlySpan<char> _remaining;

        internal LineEnumerator(ReadOnlySpan<char> text)
        {
            _remaining = text;
        }

        public TextLine Current { get; private set; }

        public readonly LineEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_remaining.IsEmpty)
                return false;

            var index = _remaining.IndexOfAny('\r', '\n');
            if (index < 0)
            {
                Current = new TextLine(_remaining, []);
                _remaining = [];
                return true;
            }

            var endOfLineLength = _remaining[index] == '\r' && index + 1 < _remaining.Length && _remaining[index + 1] == '\n' ? 2 : 1;
            Current = new TextLine(_remaining[..index], _remaining.Slice(index, endOfLineLength));
            _remaining = _remaining[(index + endOfLineLength)..];
            return true;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    public readonly ref struct TextLine(ReadOnlySpan<char> line, ReadOnlySpan<char> endOfLine)
    {
        public ReadOnlySpan<char> Line { get; } = line;

        public ReadOnlySpan<char> EndOfLine { get; } = endOfLine;

        public void Deconstruct(out ReadOnlySpan<char> line, out ReadOnlySpan<char> endOfLine)
        {
            line = Line;
            endOfLine = EndOfLine;
        }
    }
}
