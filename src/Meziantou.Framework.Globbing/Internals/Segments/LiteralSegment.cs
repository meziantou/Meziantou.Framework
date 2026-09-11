namespace Meziantou.Framework.Globbing.Internals;

internal sealed class LiteralSegment : Segment
{
    private readonly StringComparison _stringComparison;

    public LiteralSegment(string value, bool ignoreCase)
    {
        Value = value;
        IgnoreCase = ignoreCase;
        _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public bool IgnoreCase { get; }

    public string Value { get; }

    public override bool IsMatch(ref PathReader pathReader)
    {
        // The literal must fit in the current path segment: a '\' escaped in the pattern is an ordinary character,
        // but a path separator on Windows.
        if (pathReader.CurrentSegment.StartsWith(Value.AsSpan(), _stringComparison))
        {
            pathReader.ConsumeInSegment(Value.Length);
            return true;
        }

        return false;
    }

    public override string ToString()
    {
        return Value;
    }
}
