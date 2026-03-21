namespace Meziantou.Framework.Globbing.Internals;

internal sealed class StartsWithSegment : Segment
{
    private readonly StringComparison _stringComparison;

    public StartsWithSegment(string value, bool ignoreCase)
    {
        Value = value;
        IgnoreCase = ignoreCase;
        _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public bool IgnoreCase { get; }

    public string Value { get; }

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.CurrentText.StartsWith(Value.AsSpan(), _stringComparison))
        {
            pathReader.ConsumeInSegment(pathReader.CurrentSegmentLength);
            return true;
        }

        return false;
    }

    public override string ToString()
    {
        return Value + '*';
    }
}
