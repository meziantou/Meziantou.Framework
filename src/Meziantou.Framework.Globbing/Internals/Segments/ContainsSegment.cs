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

    public override void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        sb.Append('*');

        // MSBuild reads a file name made of "*.*" as every file, including the ones without an extension
        if (dialect is GlobDialect.MSBuild && Value is ".")
        {
            sb.Append("%2E");
        }
        else
        {
            GlobPatternWriter.AppendLiteral(ref sb, Value, dialect);
        }

        sb.Append('*');
    }

    public override string ToString() => ToString(GlobDialect.Standard);
}
