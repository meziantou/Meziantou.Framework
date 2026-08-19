namespace Meziantou.Framework.Collections;

internal sealed class AppendOnlyCollectionSegment<T>
{
    private volatile int _count;
    private volatile AppendOnlyCollectionSegment<T>? _next;

    /// <summary>
    /// The backing array of the segment. Only the first <see cref="Count"/> items are initialized.
    /// </summary>
    internal readonly T[] Items;

    /// <summary>
    /// Index of the first item of the segment in the owning collection.
    /// </summary>
    internal readonly int StartIndex;

    public AppendOnlyCollectionSegment(int capacity, int startIndex)
    {
        Items = GC.AllocateUninitializedArray<T>(capacity);
        StartIndex = startIndex;
    }

    public int Count => _count;

    public int Capacity => Items.Length;

    public AppendOnlyCollectionSegment<T>? Next
    {
        get => _next;
        set => _next = value;
    }

    public ReadOnlySpan<T> Span => new(Items, 0, _count);

    public bool TryAddItem(T value)
    {
        var count = _count;
        if (count >= Items.Length)
            return false;

        Items[count] = value;

        // Volatile write: release-publishes the item store, so a lock-free reader that
        // observes the new count also observes the item.
        _count = count + 1;
        return true;
    }
}
