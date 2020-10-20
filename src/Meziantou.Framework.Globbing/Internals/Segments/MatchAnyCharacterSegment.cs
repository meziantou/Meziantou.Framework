namespace Meziantou.Framework.Globbing.Internals
{
    internal sealed class MatchAnyCharacterSegment : Segment
    {
        private MatchAnyCharacterSegment()
        {
        }

        public static MatchAnyCharacterSegment Instance { get; } = new MatchAnyCharacterSegment();

        public override bool IsMatch(ref PathReader pathReader)
        {
            if (pathReader.IsPathSeparator())
                return false;

            pathReader.ConsumeInSegment(1);
            return true;
        }

        public override string ToString()
        {
            return "?";
        }
    }
}
