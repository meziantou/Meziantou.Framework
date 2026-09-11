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

    public override string ToString()
    {
        using var sb = new ValueStringBuilder();
        sb.Append('{');

        var first = true;
        foreach (var value in Values)
        {
            if (!first)
            {
                sb.Append(',');
            }

            sb.Append(value);
            first = false;
        }

        sb.Append('}');
        return sb.ToString();
    }
}
