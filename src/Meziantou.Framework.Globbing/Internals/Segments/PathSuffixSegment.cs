namespace Meziantou.Framework.Globbing.Internals;

internal sealed class PathSuffixSegment : Segment
{
    private readonly string[] _segments;
    private readonly StringComparison _stringComparison;

    public PathSuffixSegment(string[] segments, bool ignoreCase)
    {
        _segments = segments;
        IgnoreCase = ignoreCase;
        _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public bool IgnoreCase { get; }

    /// <summary>The literal names that end the path, the last one matching the last segment of the path.</summary>
    public string[] Segments => _segments;

    public override bool IsMatch(ref PathReader pathReader)
    {
        return pathReader.TryConsumePathSuffix(_segments, _stringComparison);
    }

    public override bool IsRecursiveMatchAll => true;

    public override void AppendPattern(ref ValueStringBuilder sb, GlobDialect dialect)
    {
        sb.Append("**/");

        var first = true;
        foreach (var segment in _segments)
        {
            if (!first)
            {
                sb.Append('/');
            }

            GlobPatternWriter.AppendPathSegmentLiteral(ref sb, segment, dialect);
            first = false;
        }
    }

    public override string ToString() => ToString(GlobDialect.Standard);
}
