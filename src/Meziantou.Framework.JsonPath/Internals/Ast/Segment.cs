namespace Meziantou.Framework.Json.Internals;

/// <summary>Represents a child segment [selectors] or descendant segment ..[selectors].</summary>
internal sealed class Segment
{
    public Segment(SegmentKind kind, Selector[] selectors)
    {
        Kind = kind;
        Selectors = selectors;
    }

    public SegmentKind Kind { get; }

    public Selector[] Selectors { get; }
}
