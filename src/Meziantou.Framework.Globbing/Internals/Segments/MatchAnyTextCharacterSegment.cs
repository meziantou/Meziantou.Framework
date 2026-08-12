namespace Meziantou.Framework.Globbing.Internals;

internal sealed class MatchAnyTextCharacterSegment : Segment
{
    private MatchAnyTextCharacterSegment()
    {
    }

    public static MatchAnyTextCharacterSegment Instance { get; } = new MatchAnyTextCharacterSegment();

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.IsEndOfPath)
            return false;

        pathReader.ConsumeInSegment(1);
        return true;
    }

    public override string ToString()
    {
        return "?";
    }
}
