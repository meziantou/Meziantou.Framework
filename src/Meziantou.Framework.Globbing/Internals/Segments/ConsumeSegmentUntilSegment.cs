using System;

namespace Meziantou.Framework.Globbing.Internals.Segments
{
    internal sealed class ConsumeSegmentUntilSegment : Segment
    {
        private readonly char[] _characters;

        public ConsumeSegmentUntilSegment(char[] characters)
        {
            _characters = characters;
        }

        public override bool IsMatch(ref PathReader pathReader)
        {
            var index = pathReader.CurrentText.IndexOfAny(_characters);
            if (index == -1)
                return false;

            if (index > 0)
            {
                pathReader.ConsumeInSegment(index);
            }

            return true;
        }
    }
}
