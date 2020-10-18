using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class MatchAllSegment : Segment
    {
        private MatchAllSegment()
        {
        }

        public static MatchAllSegment Instance { get; } = new MatchAllSegment();

        public override bool Match(ReadOnlySpan<char> segment) => true;

        public override string ToString()
        {
            return "*";
        }
    }
}
