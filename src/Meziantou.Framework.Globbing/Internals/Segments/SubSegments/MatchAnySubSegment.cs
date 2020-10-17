using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class MatchAnySubSegment : SubSegment
    {
        private MatchAnySubSegment()
        {
        }

        public static MatchAnySubSegment Instance { get; } = new MatchAnySubSegment();

        public override bool Match(ReadOnlySpan<char> segment, out int readCharCount)
        {
            if (segment.IsEmpty)
            {
                readCharCount = 0;
                return false;
            }

            readCharCount = 1;
            return true;
        }

        public override string ToString()
        {
            return "?";
        }
    }
}
