namespace Meziantou.Framework.Globbing.Internals;

internal sealed class LiteralSetSegment : Segment
{
    public LiteralSetSegment(string[] values, bool ignoreCase)
    {
        Values = values;
        IgnoreCase = ignoreCase;
        Comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public bool IgnoreCase { get; }

    public StringComparison Comparison { get; }

    public string[] Values { get; }

    // A literal set is a branch point: '{a,ab}' must try 'ab' when the rest of the pattern does not match after
    // 'a'. Only the backtracking matcher in RaggedSegment can do that, and GlobParser always wraps a literal set in
    // a RaggedSegment, so a set is never matched through this method.
    public override bool IsMatch(ref PathReader pathReader) => throw new NotSupportedException();

    // Only the Standard dialect supports literal sets, so the alternatives always use its escape syntax
    public override void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        sb.Append('{');

        var first = true;
        foreach (var value in Values)
        {
            if (!first)
            {
                sb.Append(',');
            }

            GlobPatternWriter.AppendLiteralSetValue(ref sb, value);
            first = false;
        }

        sb.Append('}');
    }

    public override string ToString() => ToString(GlobDialect.Standard);
}
