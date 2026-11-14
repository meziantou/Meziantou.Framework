namespace Meziantou.Framework.Json.Internals;

/// <summary>Represents a child segment [selectors] or descendant segment ..[selectors].</summary>
internal sealed class Segment
{
    public Segment(SegmentKind kind, Selector[] selectors)
    {
        Kind = kind;
        Selectors = selectors;
        if (kind is SegmentKind.Descendant)
        {
            DescendantNames = SegmentNameSlots.Create(selectors);
        }
    }

    public SegmentKind Kind { get; }

    public Selector[] Selectors { get; }

    /// <summary>
    /// Gets the names of the name selectors of a descendant segment, or <see langword="null"/> when the segment is a
    /// child segment or has no name selector.
    /// </summary>
    public SegmentNameSlots? DescendantNames { get; }
}
