namespace Meziantou.Framework.Globbing.Internals;

internal sealed class ConsumeEndOfSegment : Segment
{
    private ConsumeEndOfSegment()
    {
    }

    public static ConsumeEndOfSegment Instance { get; } = new ConsumeEndOfSegment();

    public override bool IsMatch(ref PathReader pathReader)
    {
        pathReader.ConsumeInSegment(pathReader.CurrentSegmentLength);
        return true;
    }
}
