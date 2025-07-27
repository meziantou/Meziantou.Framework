using System.Collections;
using System.Diagnostics;

namespace Meziantou.Framework.Collections;

[DebuggerDisplay("Count = {Count}")]
public sealed class AppendOnlyCollection<T> : IEnumerable<T>, IReadOnlyCollection<T>
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

    public void Add(T item)
    {
        lock (_lock)
        {
            if (_lastSegment.IsFull)
            {
                var newCapacity = Math.Min(MaxSegmentSize, _lastSegment.Items.Length * 2);
                var newSegment = new AppendOnlyCollectionSegment<T>(newCapacity);
                _lastSegment.Next = newSegment;
                _lastSegment = newSegment;
            }

            _lastSegment.AddItem(item);
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
        return Find(predicate) is not null;
    }

    public T? Find(Func<T, bool> predicate)
    {
        var segment = _firstSegment;
        while (segment is not null)
        {
            var items = segment.Items;
            for (var i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (predicate(item))
                    return item;
            }

            segment = segment.Next;
        }

        return default;
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
            if (_index >= _segment.Count)
            {
                _segment = _segment.Next;
                _index = 0;
                return _segment is not null;
            }

            return true;
        }

        public readonly void Dispose()
        {
        }

        public readonly void Reset() => throw new NotSupportedException();
    }
}
