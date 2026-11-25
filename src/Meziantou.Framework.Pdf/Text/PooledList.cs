using System.Buffers;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// A list whose array is rented from the shared pool and returned by <see cref="Dispose"/>. The glyphs of a page are kept
/// until its text is built: a page of text has thousands, and a list that grows by doubling allocates large object heap
/// arrays that cost full garbage collections.
/// </summary>
internal sealed class PooledList<T> : IDisposable
{
    private const int InitialCapacity = 256;

    private T[] _items = [];

    public int Count { get; private set; }

    public T this[int index] => AsSpan()[index];

    public ReadOnlySpan<T> AsSpan() => _items.AsSpan(0, Count);

    public void Add(T item)
    {
        if (Count == _items.Length)
        {
            Grow();
        }

        _items[Count++] = item;
    }

    /// <summary>Removes the items from an index to the end.</summary>
    public void RemoveFrom(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)index, (uint)Count, nameof(index));
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items.AsSpan(index, Count - index).Clear();
        }

        Count = index;
    }

    public void Dispose()
    {
        Return(_items, Count);
        _items = [];
        Count = 0;
    }

    private void Grow()
    {
        // The callers bound the count far below the largest array
        var newCapacity = (int)Math.Min(Math.Max(InitialCapacity, (long)_items.Length * 2), Array.MaxLength);
        var items = ArrayPool<T>.Shared.Rent(newCapacity);
        AsSpan().CopyTo(items);
        Return(_items, Count);
        _items = items;
    }

    private static void Return(T[] items, int count)
    {
        if (items.Length == 0)
            return;

        // The references the items hold must not be kept alive by the pool
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            items.AsSpan(0, count).Clear();
        }

        ArrayPool<T>.Shared.Return(items);
    }
}
