namespace Meziantou.Framework.Json.Internals;

/// <summary>A singular query (selects at most one node) used in comparisons.</summary>
internal sealed class SingularQuery
{
    public SingularQuery(bool isRelative, SingularQuerySegment[] segments)
    {
        IsRelative = isRelative;
        Segments = segments;
    }

    public bool IsRelative { get; }

    public SingularQuerySegment[] Segments { get; }
}
