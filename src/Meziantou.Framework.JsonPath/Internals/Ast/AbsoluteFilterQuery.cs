namespace Meziantou.Framework.Json.Internals;

/// <summary>An absolute filter query starting with $ (root node).</summary>
internal sealed class AbsoluteFilterQuery : FilterQuery
{
    public AbsoluteFilterQuery(Segment[] segments)
    {
        Segments = segments;
    }

    public override FilterQueryKind Kind => FilterQueryKind.Absolute;

    public override Segment[] Segments { get; }
}
