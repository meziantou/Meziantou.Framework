namespace Meziantou.Framework.Json.Internals;

/// <summary>A relative filter query starting with @ (current node).</summary>
internal sealed class RelativeFilterQuery : FilterQuery
{
    public RelativeFilterQuery(Segment[] segments)
    {
        Segments = segments;
        SingularQuery = SingularQuery.FromSegments(isRelative: true, segments);
    }

    public override FilterQueryKind Kind => FilterQueryKind.Relative;

    public override Segment[] Segments { get; }

    public override SingularQuery? SingularQuery { get; }
}
