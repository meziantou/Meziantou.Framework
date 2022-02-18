using System.Runtime.InteropServices;

namespace Meziantou.Framework.Globbing
{
    [StructLayout(LayoutKind.Auto)]
    internal ref struct SplitEnumerator
    {
        private ReadOnlySpan<char> _str;
        private readonly char _separator;

        public SplitEnumerator(ReadOnlySpan<char> str, char separator)
        {
            _str = str;
            _separator = separator;
            Current = default;
        }

        // Needed to be compatible with the foreach operator
        public readonly SplitEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            var span = _str;
            if (span.Length == 0) // Reach the end of the string
                return false;

            var index = span.IndexOf(_separator);
            if (index == -1) // The string is composed of only one line
            {
                _str = ReadOnlySpan<char>.Empty; // The remaining string is an empty string
                Current = span;
                return true;
            }

            Current = span[..index];
            _str = span[(index + 1)..];
            return true;
        }

        public ReadOnlySpan<char> Current { get; private set; }
    }
}
