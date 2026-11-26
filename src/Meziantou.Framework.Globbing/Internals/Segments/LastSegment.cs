namespace Meziantou.Framework.Globbing.Internals;

internal sealed class LastSegment : Segment
{
    private readonly Segment _innerSegment;
    private readonly bool _rejectLeadingDot;

    /// <param name="innerSegment">The segment that matches the last segment of the path.</param>
    /// <param name="rejectLeadingDot">
    ///     Whether the '**' and the segment this segment replaces reject a path segment that starts with a dot, so
    ///     the segment rejects a path that holds one.
    /// </param>
    public LastSegment(Segment innerSegment, bool rejectLeadingDot = false)
    {
        _innerSegment = innerSegment;
        _rejectLeadingDot = rejectLeadingDot;
    }

    public Segment InnerSegment => _innerSegment;

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (_rejectLeadingDot && pathReader.HasSegmentStartingWithDot())
            return false;

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
