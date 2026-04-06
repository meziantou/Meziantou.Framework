using Meziantou.Framework;

namespace Meziantou.Framework.Globbing.Internals;

internal sealed class PathSuffixSegment : Segment
{
    private readonly string[] _segments;
    private readonly StringComparison _stringComparison;

    public PathSuffixSegment(string[] segments, bool ignoreCase)
    {
        _segments = segments;
        _stringComparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    public override bool IsMatch(ref PathReader pathReader)
    {
        return pathReader.TryConsumePathSuffix(_segments, _stringComparison);
    }

    public override bool IsRecursiveMatchAll => true;

    public override string ToString()
    {
        using var sb = new ValueStringBuilder();
        sb.Append("**/");

        var first = true;
        foreach (var segment in _segments)
        {
            if (!first)
            {
                sb.Append('/');
            }

            sb.Append(segment);
            first = false;
        }

        return sb.ToString();
    }
}
