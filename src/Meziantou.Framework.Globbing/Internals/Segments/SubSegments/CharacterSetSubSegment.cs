using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class CharacterSetSubSegment : SubSegment
    {
        private readonly string _set;
        private readonly bool _inverse;
        private readonly StringComparison _stringComparison;

        public CharacterSetSubSegment(string set, bool inverse, bool ignoreCase)
        {
            _set = set;
            _inverse = inverse;
            _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
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
                var c = segment[0];
#if NET472
                return _set.IndexOf(new string(c, 1), _stringComparison) >= 0;
#else
                return _set.Contains(c, _stringComparison);
#endif
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

            sb.Append(_set);
            sb.Append(']');
            return sb.ToString();
        }
    }
}
