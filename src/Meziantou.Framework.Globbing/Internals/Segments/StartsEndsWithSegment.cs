using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class StartsEndsWithSegment : Segment
    {
        private readonly string _start;
        private readonly string _end;
        private readonly StringComparison _stringComparison;

        public StartsEndsWithSegment(string start, string end, bool ignoreCase)
        {
            _start = start;
            _end = end;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public override bool Match(ReadOnlySpan<char> segment)
        {
            return segment.StartsWith(_start.AsSpan(), _stringComparison) && segment.EndsWith(_end.AsSpan(), _stringComparison);
        }

        public override string ToString()
        {
            return _start + '*' + _end;
        }
    }
}
