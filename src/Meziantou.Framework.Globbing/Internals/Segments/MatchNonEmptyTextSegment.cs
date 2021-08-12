namespace Meziantou.Framework.Globbing.Internals;

internal sealed class MatchNonEmptyTextSegment : Segment
{
    private MatchNonEmptyTextSegment()
    {
    }

    public static MatchNonEmptyTextSegment Instance { get; } = new MatchNonEmptyTextSegment();

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (!pathReader.IsEndOfPath)
        {
            pathReader.ConsumeToEnd();
            return true;
        }

        return false;
    }

    public override bool IsRecursiveMatchAll => true;
}
