namespace Meziantou.Framework.Graphviz.Internals;

internal sealed class DotEdge(DotNode tail, DotNode head, int position, DotAttributes attributes)
{
    public DotNode Tail { get; } = tail;

    public DotNode Head { get; } = head;

    public int Position { get; } = position;

    public DotAttributes Attributes { get; } = attributes;
}
