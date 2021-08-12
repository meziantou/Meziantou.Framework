namespace Meziantou.Framework.Globbing.Internals;

internal sealed class MatchAllSegment : Segment
{
    private MatchAllSegment()
    {
    }

    public static MatchAllSegment Instance { get; } = new MatchAllSegment();

    public override bool IsMatch(ref PathReader pathReader)
    {
        pathReader.ConsumeInSegment(pathReader.CurrentSegmentLength);
        return true;
    }

    public override string ToString()
    {
        return "*";
    }
}
