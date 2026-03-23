namespace Meziantou.Framework.Json.Internals;

/// <summary>Base class for filter queries (@-relative or $-absolute) used in filter selectors.</summary>
internal abstract class FilterQuery
{
    public abstract FilterQueryKind Kind { get; }

    public abstract Segment[] Segments { get; }
}
