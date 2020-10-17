using System;
using System.Linq;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class LiteralSegment : Segment
    {
        private readonly string _value;
        private readonly bool _ignoreCase;

        public LiteralSegment(string value, bool ignoreCase)
        {
            _value = value;
            _ignoreCase = ignoreCase;
        }

        public override bool Match(ReadOnlySpan<char> segment)
        {
            if (_ignoreCase)
            {
                return segment.Equals(_value.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                return segment.SequenceEqual(_value.AsSpan());
            }
        }

        public override string ToString()
        {
            return _value;
        }
    }
}
