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
        if (pathReader.CurrentSegment.StartsWith(Value.AsSpan(), _stringComparison))
        {
            pathReader.ConsumeInSegment(pathReader.CurrentSegmentLength);
            return true;
        }

        return false;
    }

    public override void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        GlobPatternWriter.AppendLiteral(ref sb, Value, dialect);
        sb.Append('*');
    }

    public override string ToString() => ToString(GlobDialect.Standard);
}
