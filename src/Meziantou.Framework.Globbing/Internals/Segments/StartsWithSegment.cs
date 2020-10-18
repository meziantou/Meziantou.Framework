using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class StartsWithSegment : Segment
    {
        private readonly string _value;
        private readonly StringComparison _stringComparison;

        public StartsWithSegment(string value, bool ignoreCase)
        {
            _value = value;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public override bool Match(ReadOnlySpan<char> segment)
        {
            return segment.StartsWith(_value.AsSpan(), _stringComparison);
        }

        public override string ToString()
        {
            return _value + '*';
        }
    }
}
