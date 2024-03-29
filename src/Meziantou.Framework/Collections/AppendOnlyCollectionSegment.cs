namespace Meziantou.Framework.Collections;

internal sealed class AppendOnlyCollectionSegment<T>
{
    public AppendOnlyCollectionSegment(int capacity)
    {
        Items = GC.AllocateUninitializedArray<T>(capacity);
    }

    public T[] Items { get; set; }
    public int Count { get; set; }
    public AppendOnlyCollectionSegment<T>? Next { get; set; }
}
