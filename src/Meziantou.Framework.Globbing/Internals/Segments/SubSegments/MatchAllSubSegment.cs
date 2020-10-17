using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class MatchAllSubSegment : SubSegment
    {
        private MatchAllSubSegment()
        {
        }

        public static MatchAllSubSegment Instance { get; } = new MatchAllSubSegment();

        public override bool Match(ReadOnlySpan<char> segment, out int readCharCount)
        {
            throw new NotSupportedException();
        }

        public override string ToString()
        {
            return "*";
        }
    }
}
