using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class ContainsSegment : Segment
    {
        private readonly string _value;
        private readonly StringComparison _stringComparison;

        public ContainsSegment(string value, bool ignoreCase)
        {
            _value = value;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public override bool Match(ReadOnlySpan<char> segment)
        {
            return segment.Contains(_value.AsSpan(), _stringComparison);
        }

        public override string ToString()
        {
            return '*' + _value + '*';
        }
    }
}
