namespace Meziantou.Framework.Globbing.Internals;

internal sealed class LastSegment : Segment
{
    private readonly Segment _innerSegment;

    public LastSegment(Segment innerSegment)
    {
        _innerSegment = innerSegment;
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        pathReader.ConsumeToLastSegment();
        return _innerSegment.IsMatch(ref pathReader);
    }

    public override bool IsRecursiveMatchAll => true;

    public override void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        sb.Append("**/");
        if (_innerSegment is LiteralSegment literal)
        {
            GlobPatternWriter.AppendPathSegmentLiteral(ref sb, literal.Value, dialect);
        }
        else
        {
            _innerSegment.AppendPattern(ref sb, dialect);
        }
    }

    public override string ToString() => ToString(GlobDialect.Standard);
}
