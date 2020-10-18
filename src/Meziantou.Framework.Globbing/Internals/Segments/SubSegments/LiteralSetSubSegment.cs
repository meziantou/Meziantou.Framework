using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class LiteralSetSubSegment : SubSegment
    {
        private readonly string[] _values;
        private readonly StringComparison _comparison;

        public LiteralSetSubSegment(string[] values, bool ignoreCase)
        {
            _values = values;
            _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        }

        public override bool Match(ReadOnlySpan<char> segment, out int readCharCount)
        {
            foreach (var value in _values)
            {
                if (segment.StartsWith(value.AsSpan(), _comparison))
                {
                    readCharCount = value.Length;
                    return true;
                }
            }

            readCharCount = 0;
            return false;
        }

        public override string ToString()
        {
            using var sb = new ValueStringBuilder();
            sb.Append('{');

            var first = true;
            foreach (var value in _values)
            {
                if (!first)
                {
                    sb.Append(',');
                }

                sb.Append(value);
                first = false;
            }

            sb.Append('}');
            return sb.ToString();
        }
    }
}
