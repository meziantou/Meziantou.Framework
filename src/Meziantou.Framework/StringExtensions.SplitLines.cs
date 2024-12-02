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
    public static LineSplitEnumerator SplitLines(this string str) => new(str.AsSpan());

    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public ref struct LineSplitEnumerator
    {
        private static SearchValues<char> NewLineCharacters { get; } = SearchValues.Create(['\r', '\n']);

        private ReadOnlySpan<char> _str;

        public LineSplitEnumerator(ReadOnlySpan<char> str)
        {
            _str = str;
            Current = default;
        }

        public readonly LineSplitEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (_str.Length == 0)
                return false;

            var span = _str;
            var index = span.IndexOfAny(NewLineCharacters);
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
