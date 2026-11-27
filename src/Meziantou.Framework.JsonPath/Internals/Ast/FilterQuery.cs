namespace Meziantou.Framework.Json.Internals;

/// <summary>Base class for filter queries (@-relative or $-absolute) used in filter selectors.</summary>
internal abstract class FilterQuery
{
    public abstract FilterQueryKind Kind { get; }

    public abstract Segment[] Segments { get; }

    /// <summary>
    /// Gets the query as a singular query when every segment is a child segment with a single name or index selector,
    /// such as <c>@.price</c>; otherwise, <see langword="null"/>. Such a query selects at most one node, which can be
    /// found without collecting the nodes of each segment.
    /// </summary>
    public abstract SingularQuery? SingularQuery { get; }
}
