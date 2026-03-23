namespace Meziantou.Framework.Json.Internals;

/// <summary>Index selector: selects an element of an array by index (may be negative).</summary>
internal sealed class IndexSelector : Selector
{
    public IndexSelector(long index)
    {
        Index = index;
    }

    public override SelectorKind Kind => SelectorKind.Index;

    public long Index { get; }
}
