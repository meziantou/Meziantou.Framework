namespace Meziantou.Framework.Globbing.Internals;

/// <summary>
///     A <see cref="GlobDialect.Standard"/> brace expression, such as <c>{*.cs,[ab]?,x{y,z}}</c>: it matches one of its
///     alternatives, each of which is a sequence of subsegments that can hold wildcards and nested brace expressions.
/// </summary>
internal sealed class AlternationSegment : Segment
{
    public AlternationSegment(Segment[][] alternatives)
    {
        Alternatives = alternatives;
    }

    public Segment[][] Alternatives { get; }

    // An alternation is a branch point: '{a,ab}' must try 'ab' when the rest of the pattern does not match after
    // 'a'. Only the matcher in RaggedSegment can do that, and GlobParser always wraps an alternation in a
    // RaggedSegment, so an alternation is never matched through this method.
    public override bool IsMatch(ref PathReader pathReader) => throw new NotSupportedException();

    // Only the Standard dialect supports brace expressions, so the alternatives always use its escape syntax
    public override void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        sb.Append('{');

        var first = true;
        foreach (var alternative in Alternatives)
        {
            if (!first)
            {
                sb.Append(',');
            }

            foreach (var part in alternative)
            {
                if (part is LiteralSegment literal)
                {
                    GlobPatternWriter.AppendAlternativeLiteral(ref sb, literal.Value);
                }
                else
                {
                    part.AppendPattern(ref sb, GlobDialect.Standard);
                }
            }

            first = false;
        }

        sb.Append('}');
    }

    public override string ToString() => ToString(GlobDialect.Standard);
}
