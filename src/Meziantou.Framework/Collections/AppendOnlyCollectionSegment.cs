namespace Meziantou.Framework.Collections;

internal sealed class AppendOnlyCollectionSegment<T>
{
    public AppendOnlyCollectionSegment(int capacity)
    {
        Items = GC.AllocateUninitializedArray<T>(capacity);
        Count = 0;
        Next = null;
    }

    public T[] Items { get; set; }
    
    public int Count 
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }

    public AppendOnlyCollectionSegment<T>? Next
    {
        get => Volatile.Read(ref field);
        set => Volatile.Write(ref field, value);
    }
}
