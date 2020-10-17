using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class RangeSubSegment : SubSegment
    {
        private readonly CharacterRange _range;
        private readonly bool _inverse;
        private readonly bool _ignoreCase;

        public RangeSubSegment(CharacterRange range, bool inverse, bool ignoreCase)
        {
            if (ignoreCase && IsAsciiUpper(range.Min) && IsAsciiUpper(range.Max))
            {
                _range = new CharacterRange(char.ToLowerInvariant(range.Min), char.ToLowerInvariant(range.Max));
            }
            else
            {
                _range = range;
                _ignoreCase = ignoreCase;
            }

            _inverse = inverse;
        }

        private static bool IsAsciiUpper(int c)
        {
            return c >= 'A' && c <= 'Z';
        }

        public override bool Match(ReadOnlySpan<char> segment, out int readCharCount)
        {
            var result = MatchCore(segment);
            if (_inverse)
            {
                result = !result;
            }

            readCharCount = result ? 1 : 0;
            return result;

            bool MatchCore(ReadOnlySpan<char> segment)
            {
                var range = _range;
                var c = segment[0];
                if (_ignoreCase && IsAsciiUpper(c))
                {
                    c = char.ToLowerInvariant(c);
                }

                if (c >= range.Min && c <= range.Max)
                    return true;

                return false;
            }
        }

        public override string ToString()
        {
            using var sb = new ValueStringBuilder();
            sb.Append('[');
            if (_inverse)
            {
                sb.Append('!');
            }

            sb.Append(_range.Min);
            sb.Append('-');
            sb.Append(_range.Max);
            sb.Append(']');
            return sb.ToString();
        }
    }
}
