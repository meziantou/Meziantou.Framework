using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class SingleSubSegment : Segment
    {
        private readonly SubSegment _segment;

        public SingleSubSegment(SubSegment segment)
        {
            if (segment is MatchAllSubSegment)
                throw new ArgumentException("MatchAllSegment is not valid here", nameof(segment));

            _segment = segment;
        }

        public override bool Match(ReadOnlySpan<char> segment)
        {
            return _segment.Match(segment, out var readCharCount) && readCharCount == segment.Length;
        }

        public override string? ToString()
        {
            return _segment.ToString();
        }
    }
}
