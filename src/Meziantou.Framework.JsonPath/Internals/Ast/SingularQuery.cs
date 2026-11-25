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

    /// <summary>Creates the singular query that selects the same node as the segments of a filter query.</summary>
    /// <param name="isRelative">Whether the query starts with <c>@</c>.</param>
    /// <param name="segments">The segments of the filter query.</param>
    /// <returns>
    /// The singular query, or <see langword="null"/> when a segment is a descendant segment, or has a selector other
    /// than a single name or index selector.
    /// </returns>
    public static SingularQuery? FromSegments(bool isRelative, Segment[] segments)
    {
        var result = new SingularQuerySegment[segments.Length];
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.Kind is not SegmentKind.Child || segment.Selectors.Length is not 1)
                return null;

            switch (segment.Selectors[0])
            {
                case NameSelector name:
                    result[i] = new SingularQuerySegment(name.Name);
                    break;

                case IndexSelector index:
                    result[i] = new SingularQuerySegment(index.Index);
                    break;

                default:
                    return null;
            }
        }

        return new SingularQuery(isRelative, result);
    }
}
