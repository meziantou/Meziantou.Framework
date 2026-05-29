using System.Collections;
using System.Diagnostics;

namespace Meziantou.Framework.Collections;

[DebuggerDisplay("Count = {Count}")]
#if PUBLIC_APPEND_ONLY_COLLECTION
public
#else
internal
#endif
sealed class AppendOnlyCollection<T> : IEnumerable<T>, IReadOnlyCollection<T>, ICollection<T>
{
    private const int MaxSegmentSize = 8000;
    private readonly Lock _lock = new();
    private readonly AppendOnlyCollectionSegment<T> _firstSegment;
    private AppendOnlyCollectionSegment<T> _lastSegment;
    private volatile int _count;

    public AppendOnlyCollection()
        : this(16)
    {
    }

    public AppendOnlyCollection(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be greater than 0", nameof(capacity));

        _firstSegment = _lastSegment = new AppendOnlyCollectionSegment<T>(capacity);
    }

    public int Count => _count;

    bool ICollection<T>.IsReadOnly => false;

    public void Add(T item)
    {
        lock (_lock)
        {
            if (_lastSegment.IsFull)
            {
                var newCapacity = Math.Min(MaxSegmentSize, _lastSegment.Items.Length * 2);
                var newSegment = new AppendOnlyCollectionSegment<T>(newCapacity);

                // Add the item before publishing the link. The volatile write to Next
                // release-publishes the item store, so a lock-free reader that observes
                // the new segment via Next always sees a segment with Count >= 1.
                newSegment.AddItem(item);
                _lastSegment.Next = newSegment;
                _lastSegment = newSegment;
            }
            else
            {
                _lastSegment.AddItem(item);
            }

            _count++;
        }
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var segment = _firstSegment;

            // The first assertion ensures that we'll find the item.
            // The collection is append-only, so items are never removed.
            // So, there is no need to check if segment is null in the loop, or if index is negative.
            while (true)
            {
                var segmentCount = segment.Count;
                if (segment.TryGetItem(index, out var item))
                    return item;

                Debug.Assert(segment.IsFull);
                Debug.Assert(segment.Next is not null);

                index -= segmentCount;
                Debug.Assert(index >= 0);

                segment = segment.Next;
            }
        }
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(Func<T, bool> predicate)
    {
        return TryFind(predicate, out _);
    }

    public T? Find(Func<T, bool> predicate)
    {
        return TryFind(predicate, out var result) ? result : default;
    }

    public bool TryFind(Func<T, bool> predicate, out T result)
    {
        var segment = _firstSegment;
        while (segment is not null)
        {
            var items = segment.Items;
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (predicate(item))
                {
                    result = item;
                    return true;
                }
            }

            segment = segment.Next;
        }

        result = default!;
        return false;
    }

    void ICollection<T>.Clear() => throw new NotSupportedException();
    bool ICollection<T>.Contains(T item) => Contains(x => EqualityComparer<T>.Default.Equals(x, item));
    bool ICollection<T>.Remove(T item) => throw new NotSupportedException();
    void ICollection<T>.CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array.Rank is not 1)
            throw new ArgumentException("Array must be single-dimensional", nameof(array));

        if (arrayIndex < 0 || arrayIndex > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        var count = Count;
        if (array.Length - arrayIndex < count)
            throw new ArgumentException("The number of elements in the source collection is greater than the available space from arrayIndex to the end of the destination array.", nameof(array));

        var segment = _firstSegment;
        while (segment is not null && count > 0)
        {
            var items = segment.Items;
            var copyCount = Math.Min(items.Length, count);
            items[..copyCount].CopyTo(array.AsSpan(arrayIndex));
            arrayIndex += copyCount;
            count -= copyCount;

            segment = segment.Next;
        }
    }

    public struct Enumerator : IEnumerator<T>
    {
        private AppendOnlyCollectionSegment<T>? _segment;
        private int _index = -1;

        internal Enumerator(AppendOnlyCollection<T> collection)
        {
            _segment = collection._firstSegment;
        }

        public readonly T Current
        {
            get
            {
                Debug.Assert(_segment is not null);
                return _segment.Items[_index];
            }
        }

        readonly object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            if (_segment is null)
                return false;

            _index++;
            while (true)
            {
                var segment = _segment;
                Debug.Assert(segment is not null);

                var count = segment.Count;
                if (_index < count)
                    return true;

                // Segment.Next is volatile. If we can observe it, re-read Count so this
                // transition decision is not made using a potentially stale count read.
                var nextSegment = segment.Next;
                if (nextSegment is null)
                    return false;

                count = segment.Count;
                if (_index < count)
                    return true;

                _segment = nextSegment;
                _index = 0;
            }
        }

        public readonly void Dispose()
        {
        }

        public readonly void Reset() => throw new NotSupportedException();
    }
}
