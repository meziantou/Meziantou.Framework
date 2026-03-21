namespace Meziantou.Framework.Globbing.Internals;

internal sealed class ContainsSegment : Segment
{
    private readonly StringComparison _stringComparison;

    public ContainsSegment(string value, bool ignoreCase)
    {
        Value = value;
        IgnoreCase = ignoreCase;
        _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public bool IgnoreCase { get; }

    public string Value { get; }

    public override bool IsMatch(ref PathReader pathReader)
    {
        var currentSegment = pathReader.CurrentSegment;
        if (currentSegment.Contains(Value.AsSpan(), _stringComparison))
        {
            pathReader.ConsumeInSegment(currentSegment.Length);
            return true;
        }

        return false;
    }

    public override string ToString()
    {
        return '*' + Value + '*';
    }
}
