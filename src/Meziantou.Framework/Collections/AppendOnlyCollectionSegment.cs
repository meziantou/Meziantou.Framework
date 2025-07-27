namespace Meziantou.Framework.Collections;

internal sealed class AppendOnlyCollectionSegment<T>
{
    private volatile int _count;
    private volatile AppendOnlyCollectionSegment<T>? _next;
    private readonly T[] _items;

    public AppendOnlyCollectionSegment(int capacity)
    {
        _items = GC.AllocateUninitializedArray<T>(capacity);
    }

    public int Count => _count;

    public AppendOnlyCollectionSegment<T>? Next
    {
        get => _next;
        set => _next = value;
    }

    public bool IsFull => _count >= _items.Length;

    public ReadOnlySpan<T> Items => _count is 0 ? [] : new ReadOnlySpan<T>(_items, 0, _count);

    public bool TryGetItem(int index, out T value)
    {
        if (index < 0 || index >= _count)
        {
            value = default!;
            return false;
        }

        value = _items[index];
        return true;
    }

    public void AddItem(T value)
    {
        if (_count >= _items.Length)
            throw new InvalidOperationException("Cannot add more items to this segment, it is full.");

        _items[_count] = value;
        _count++;
    }
}
