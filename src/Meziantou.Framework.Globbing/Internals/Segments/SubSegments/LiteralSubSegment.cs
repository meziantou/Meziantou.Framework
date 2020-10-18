using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class LiteralSubSegment : SubSegment
    {
        private readonly StringComparison _stringComparison;

        public LiteralSubSegment(string value, bool ignoreCase)
        {
            Value = value;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public string Value { get; }

        public override bool Match(ReadOnlySpan<char> segment, out int readCharCount)
        {
            if (segment.StartsWith(Value.AsSpan(), _stringComparison))
            {
                readCharCount = Value.Length;
                return true;
            }

            readCharCount = 0;
            return false;
        }

        public override string ToString()
        {
            return Value;
        }
    }
}
