namespace Meziantou.Framework.Globbing.Internals;

internal sealed class MatchAllSubSegment : Segment
{
    private MatchAllSubSegment()
    {
    }

    public static MatchAllSubSegment Instance { get; } = new MatchAllSubSegment();

    public override bool IsMatch(ref PathReader pathReader)
    {
        throw new NotSupportedException();
    }

    public override string ToString()
    {
        return "*";
    }
}
