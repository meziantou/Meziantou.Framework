using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework;

#if PUBLIC_STRING_EXTENSIONS
public
#else
internal
#endif
static partial class StringExtensions
{
    public static LineSplitEnumerator SplitLines(this string str) => new(str.AsSpan(), LineBreakMode.Unicode);

    public static LineSplitEnumerator SplitLines(this string str, LineBreakMode lineBreakMode) => new(str.AsSpan(), lineBreakMode);


    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public ref struct LineSplitEnumerator
    {
#if NET8_0_OR_GREATER
        private static SearchValues<char> StandardLineBreakCharacters { get; } = SearchValues.Create("\r\n");
        private static SearchValues<char> UnicodeLineBreakCharacters { get; } = SearchValues.Create("\r\n\u0085\u2028\u2029");
        private static SearchValues<char> UnicodeWithLegacyControlLineBreakCharacters { get; } = SearchValues.Create("\r\n\u0085\u2028\u2029\v\f");
#else
        private static ReadOnlySpan<char> StandardLineBreakCharacters => "\r\n";
        private static ReadOnlySpan<char> UnicodeLineBreakCharacters => "\r\n\u0085\u2028\u2029";
        private static ReadOnlySpan<char> UnicodeWithLegacyControlLineBreakCharacters => "\r\n\u0085\u2028\u2029\v\f";
#endif

        private ReadOnlySpan<char> _str;
        private readonly LineBreakMode _lineBreakMode;

        public LineSplitEnumerator(ReadOnlySpan<char> str)
            : this(str, LineBreakMode.Unicode)
        {
        }

        public LineSplitEnumerator(ReadOnlySpan<char> str, LineBreakMode lineBreakMode)
        {
            _str = str;
            _lineBreakMode = lineBreakMode;
            Current = default;
        }

        public readonly LineSplitEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_str.Length is 0)
                return false;

            var span = _str;
            var newLineCharacters = GetNewLineCharacters(_lineBreakMode);
            var index = span.IndexOfAny(newLineCharacters);
            if (index == -1)
            {
                _str = [];
                Current = new LineSplitEntry(span, []);
                return true;
            }

            if (index < span.Length - 1 && span[index] == '\r')
            {
                var next = span[index + 1];
                if (next == '\n')
                {
                    Current = new LineSplitEntry(span[..index], span.Slice(index, 2));
                    _str = span[(index + 2)..];
                    return true;
                }
            }

            Current = new LineSplitEntry(span[..index], span.Slice(index, 1));
            _str = span[(index + 1)..];
            return true;
        }

#if NET8_0_OR_GREATER
        private static SearchValues<char> GetNewLineCharacters(LineBreakMode lineBreakMode)
#else
        private static ReadOnlySpan<char> GetNewLineCharacters(LineBreakMode lineBreakMode)
#endif
        {
            return lineBreakMode switch
            {
                LineBreakMode.Standard => StandardLineBreakCharacters,
                LineBreakMode.UnicodeWithLegacyControls => UnicodeWithLegacyControlLineBreakCharacters,
                _ => UnicodeLineBreakCharacters,
            };
        }

        public LineSplitEntry Current { get; private set; }
    }

    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
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

[SuppressMessage("Design", "MA0048:File name must match type name")]
#if PUBLIC_STRING_EXTENSIONS
public
#else
internal
#endif
enum LineBreakMode
{
    Standard,
    Unicode,
    UnicodeWithLegacyControls,
}
