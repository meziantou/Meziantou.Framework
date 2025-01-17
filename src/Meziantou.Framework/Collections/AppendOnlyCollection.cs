using System.Collections;
using System.Diagnostics;

namespace Meziantou.Framework.Collections;

[DebuggerDisplay("Count = {Count}")]
public sealed class AppendOnlyCollection<T> : IEnumerable<T>, IReadOnlyCollection<T>
{
    private const int MaxSegmentSize = 8000;

    private readonly Lock _lock = new();
    private AppendOnlyCollectionSegment<T>? _firstSegment;
    private AppendOnlyCollectionSegment<T>? _lastSegment;

    public int Count { get; private set; }

    public void Add(T item)
    {
        lock (_lock)
        {
            if (_lastSegment is null)
            {
                _firstSegment = _lastSegment = new AppendOnlyCollectionSegment<T>(16);
            }

            if (_lastSegment.Count == _lastSegment.Items.Length)
            {
                var newCapacity = Math.Min(MaxSegmentSize, _lastSegment.Count * 2);
                var newSegment = new AppendOnlyCollectionSegment<T>(newCapacity);
                _lastSegment.Next = newSegment;
                _lastSegment = newSegment;
            }

            _lastSegment.Items[_lastSegment.Count] = item;
            _lastSegment.Count++;
            Count++;
        }
    }

    public T this[int index]
    {
        get
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var segment = _firstSegment;
            if (segment is null)
                throw new ArgumentOutOfRangeException(nameof(index));

            while (true)
            {
                if (index < segment.Count)
                    return segment.Items[index];

                index -= segment.Count;

                Debug.Assert(segment.Next is not null);
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
            for (var i = 0; i < segment.Count; i++)
            {
                var item = segment.Items[i];
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
