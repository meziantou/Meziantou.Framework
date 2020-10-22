namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class MatchAllEndOfSegment : Segment
    {
        private MatchAllEndOfSegment()
        {
        }

        public static MatchAllEndOfSegment Instance { get; } = new MatchAllEndOfSegment();

        public override bool IsMatch(ref PathReader pathReader)
        {
            if (pathReader.CurrentSegmentLength > 0)
            {
                pathReader.ConsumeInSegment(pathReader.CurrentSegmentLength);
            }

            return true;
        }
    }
}
