namespace Meziantou.Framework.Globbing.Internals;

internal sealed class MatchAllEndsWithSegment : Segment
{
    private readonly string _suffix;
    private readonly StringComparison _stringComparison;

    public MatchAllEndsWithSegment(string suffix, bool ignoreCase)
    {
        _suffix = suffix;
        _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.EndText.EndsWith(_suffix.AsSpan(), _stringComparison))
        {
            pathReader.ConsumeToEnd();
            return true;
        }

        return false;
    }

    public override bool IsRecursiveMatchAll => true;
}
