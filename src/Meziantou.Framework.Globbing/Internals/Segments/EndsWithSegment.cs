using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class EndsWithSegment : Segment
    {
        private readonly string _value;
        private readonly StringComparison _stringComparison;

        public EndsWithSegment(string value, bool ignoreCase)
        {
            _value = value;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public override bool Match(ReadOnlySpan<char> segment)
        {
            return segment.EndsWith(_value.AsSpan(), _stringComparison);
        }

        public override string ToString()
        {
            return _value + '*';
        }
    }
}
