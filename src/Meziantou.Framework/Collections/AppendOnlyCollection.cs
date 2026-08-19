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

    // Append-only lookup table of all segments, ordered by StartIndex. It is replaced by a new
    // array every time a segment is created, so a reader can safely use the array it has read.
    private volatile AppendOnlyCollectionSegment<T>[] _segments;
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

        _firstSegment = _lastSegment = new AppendOnlyCollectionSegment<T>(capacity, startIndex: 0);
        _segments = [_firstSegment];
    }

    public int Count => _count;

    bool ICollection<T>.IsReadOnly => false;

    public void Add(T item)
    {
        lock (_lock)
        {
            var lastSegment = _lastSegment;
            if (!lastSegment.TryAddItem(item))
            {
                var newCapacity = Math.Min(MaxSegmentSize, lastSegment.Capacity * 2);

                // A segment is only replaced once it is full, so the next segment starts right after it.
                var newSegment = new AppendOnlyCollectionSegment<T>(newCapacity, lastSegment.StartIndex + lastSegment.Capacity);

                // Add the item before publishing the segment. The volatile writes to _segments and
                // Next release-publish the item store, so a lock-free reader that observes the new
                // segment always sees a segment with Count >= 1.
                newSegment.TryAddItem(item);

                var segments = _segments;
                var newSegments = new AppendOnlyCollectionSegment<T>[segments.Length + 1];
                Array.Copy(segments, newSegments, segments.Length);
                newSegments[^1] = newSegment;
                _segments = newSegments;

                lastSegment.Next = newSegment;
                _lastSegment = newSegment;
            }

            // Volatile write: readers that observe the new count also observe the item and the
            // segment holding it, as both are published before this write.
            _count++;
        }
    }

    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            // The count is read before the segments, so the segment holding the item is present.
            var segments = _segments;

            // Find the last segment whose StartIndex is lower than or equal to index.
            var low = 0;
            var high = segments.Length - 1;
            while (low < high)
            {
                var middle = (int)(((uint)low + (uint)high + 1) >> 1);
                if (segments[middle].StartIndex <= index)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            var segment = segments[low];
            Debug.Assert(index - segment.StartIndex < segment.Count);
            return segment.Items[index - segment.StartIndex];
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
            foreach (var item in segment.Span)
            {
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
    bool ICollection<T>.Remove(T item) => throw new NotSupportedException();

    bool ICollection<T>.Contains(T item)
    {
        var comparer = EqualityComparer<T>.Default;
        var segment = _firstSegment;
        while (segment is not null)
        {
            foreach (var value in segment.Span)
            {
                if (comparer.Equals(value, item))
                    return true;
            }

            segment = segment.Next;
        }

        return false;
    }

    void ICollection<T>.CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (array.Rank is not 1)
            throw new ArgumentException("Array must be single-dimensional", nameof(array));

        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);

        var count = Count;
        if (array.Length - arrayIndex < count)
            throw new ArgumentException("The number of elements in the source collection is greater than the available space from arrayIndex to the end of the destination array.", nameof(array));

        var destination = array.AsSpan(arrayIndex);
        var segment = _firstSegment;
        while (segment is not null && count > 0)
        {
            var items = segment.Span;
            var copyCount = Math.Min(items.Length, count);
            items[..copyCount].CopyTo(destination);
            destination = destination[copyCount..];
            count -= copyCount;

            segment = segment.Next;
        }
    }

    public struct Enumerator : IEnumerator<T>
    {
        private AppendOnlyCollectionSegment<T>? _segment;
        private T[]? _items;

        // Number of items known to be initialized in _items. It can only grow, so it is safe to
        // cache it and to only re-read the volatile count of the segment once it is reached.
        private int _count;
        private int _index = -1;

        internal Enumerator(AppendOnlyCollection<T> collection)
        {
            var segment = collection._firstSegment;
            _segment = segment;
            _count = segment.Count;
            _items = segment.Items;
        }

        public readonly T Current
        {
            get
            {
                Debug.Assert(_items is not null);
                return _items[_index];
            }
        }

        readonly object? IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var index = _index + 1;
            if (index < _count)
            {
                _index = index;
                return true;
            }

            return MoveNextSlow(index);
        }

        private bool MoveNextSlow(int index)
        {
            var segment = _segment;
            if (segment is null)
                return false;

            while (true)
            {
                // The cached count may be stale as items can be appended while enumerating.
                var count = segment.Count;
                if (index < count)
                {
                    _count = count;
                    _index = index;
                    return true;
                }

                // Segment.Next is volatile. If we can observe it, re-read Count so this
                // transition decision is not made using a potentially stale count read.
                var nextSegment = segment.Next;
                if (nextSegment is null)
                {
                    _count = count;
                    _index = index - 1;
                    return false;
                }

                count = segment.Count;
                if (index < count)
                {
                    _count = count;
                    _index = index;
                    return true;
                }

                segment = nextSegment;
                _segment = nextSegment;
                _items = nextSegment.Items;
                index = 0;
            }
        }

        public readonly void Dispose()
        {
        }

        public readonly void Reset() => throw new NotSupportedException();
    }
}
