namespace Meziantou.Framework.Json.Internals;

/// <summary>Slice selector: selects a range of elements from an array (start:end:step).</summary>
internal sealed class SliceSelector : Selector
{
    public SliceSelector(long? start, long? end, long? step)
    {
        Start = start;
        End = end;
        Step = step;
    }

    public override SelectorKind Kind => SelectorKind.Slice;

    public long? Start { get; }

    public long? End { get; }

    public long? Step { get; }
}
