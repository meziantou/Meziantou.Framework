using System;

namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class RecursiveMatchAllSegment : Segment
    {
        private RecursiveMatchAllSegment()
        {
        }

        public static RecursiveMatchAllSegment Instance { get; } = new RecursiveMatchAllSegment();

        public override bool Match(ReadOnlySpan<char> segment) => true;

        public override string ToString()
        {
            return "**";
        }
    }
}
