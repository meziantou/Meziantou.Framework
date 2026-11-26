namespace Meziantou.Framework.Globbing.Internals;

internal sealed class MatchAllEndsWithSegment : Segment
{
    private readonly string _suffix;
    private readonly StringComparison _stringComparison;
    private readonly bool _rejectLeadingDot;

    /// <param name="suffix">The text the path must end with.</param>
    /// <param name="ignoreCase">Whether the case of the suffix is ignored.</param>
    /// <param name="rejectLeadingDot">
    ///     Whether the '**' and the '*' this segment replaces reject a path segment that starts with a dot, so the
    ///     segment rejects a path that holds one.
    /// </param>
    public MatchAllEndsWithSegment(string suffix, bool ignoreCase, bool rejectLeadingDot = false)
    {
        _suffix = suffix;
        IgnoreCase = ignoreCase;
        _rejectLeadingDot = rejectLeadingDot;
        _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public string Suffix => _suffix;

    public bool IgnoreCase { get; }

    public override bool IsMatch(ref PathReader pathReader)
    {
        if (pathReader.EndText.EndsWith(_suffix.AsSpan(), _stringComparison) && !(_rejectLeadingDot && pathReader.HasSegmentStartingWithDot()))
        {
            pathReader.ConsumeToEnd();
            return true;
        }

        return false;
    }

    public override bool IsRecursiveMatchAll => true;

    public override void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        sb.Append("**/*");
        GlobPatternWriter.AppendLiteral(ref sb, _suffix, dialect);
    }

    public override string ToString() => ToString(GlobDialect.Standard);
}
