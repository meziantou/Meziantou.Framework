using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class CombinedSubSegment : SubSegment
    {
        private readonly SubSegment[] _subSegments;
        private readonly bool _inverse;

        public CombinedSubSegment(SubSegment[] subSegments, bool inverse)
        {
            _subSegments = subSegments;
            _inverse = inverse;
        }

        public override bool Match(ReadOnlySpan<char> segment, out int readCharCount)
        {
            var result = MatchCore(segment, out readCharCount);
            if (_inverse)
            {
                result = !result;
                if (result)
                {
                    readCharCount = 0;
                }
            }

            return result;
        }

        private bool MatchCore(ReadOnlySpan<char> segment, out int readCharCount)
        {
            foreach (var subsegment in _subSegments)
            {
                if (subsegment.Match(segment, out readCharCount))
                    return true;
            }

            readCharCount = 0;
            return false;
        }

        public override string ToString()
        {
            using var sb = new ValueStringBuilder();
            sb.Append('(');
            if (_inverse)
            {
                sb.Append('!');
            }

            foreach (var segment in _subSegments)
            {
                sb.Append(segment.ToString());
            }

            sb.Append(')');
            return sb.ToString();
        }
    }
}
