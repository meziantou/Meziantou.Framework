using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Collections;

/// <summary>Represents a growable double-ended queue.</summary>
public sealed class DoubleEndedQueue<T> : ICollection<T>, IReadOnlyList<T>
{
    private readonly int _initialCapacity;
    private T[]? _items;
    private int _startIndex;
    private int _version;

    /// <summary>Initializes a new instance of <see cref="DoubleEndedQueue{T}"/> with the specified initial capacity.</summary>
    public DoubleEndedQueue(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Capacity must be greater than 0.", nameof(capacity));

        _initialCapacity = capacity;
    }

    /// <summary>Gets the number of elements contained in the collection.</summary>
    public int Count { get; private set; }

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Adds an item at the beginning of the collection.</summary>
    public void AddFirst(T value)
    {
        EnsureCapacityForOneMore();

        if (Count is 0)
        {
            _startIndex = 0;
            _items[0] = value;
        }
        else
        {
            _startIndex = (_startIndex - 1 + _items.Length) % _items.Length;
            _items[_startIndex] = value;
        }

        Count++;
        _version++;
    }

    /// <summary>Adds an item at the end of the collection.</summary>
    public void AddLast(T value)
    {
        EnsureCapacityForOneMore();

        var index = (_startIndex + Count) % _items.Length;
        _items[index] = value;
        Count++;
        _version++;
    }

    /// <summary>Removes and returns the first item.</summary>
    public T RemoveFirst()
    {
        if (Count is 0)
            throw new InvalidOperationException("The collection is empty");

        Debug.Assert(_items is not null);
        ref var item = ref _items[_startIndex];
        var result = item;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            item = default;
        }

        Count--;
        _startIndex = Count is 0 ? 0 : (_startIndex + 1) % _items.Length;
        _version++;
        return result;
    }

    /// <summary>Removes and returns the last item.</summary>
    public T RemoveLast()
    {
        if (Count is 0)
            throw new InvalidOperationException("The collection is empty");

        Debug.Assert(_items is not null);
        var index = (_startIndex + Count - 1) % _items.Length;
        ref var item = ref _items[index];
        var result = item;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            item = default!;
        }

        Count--;
        if (Count is 0)
        {
            _startIndex = 0;
        }

        _version++;
        return result;
    }

    /// <summary>Gets the item at the specified index.</summary>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                ThrowHelper.ThrowArgumentOutOfRange_IndexException();

            Debug.Assert(_items is not null);
            return _items[(_startIndex + index) % _items.Length];
        }
    }

    public void Clear()
    {
        if (_items is not null && RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Count > 0)
        {
            var length = Math.Min(Count, _items.Length - _startIndex);
            Array.Clear(_items, _startIndex, length);
            if (_startIndex + Count > _items.Length)
            {
                Array.Clear(_items, 0, (_startIndex + Count) % _items.Length);
            }
        }

        Count = 0;
        _startIndex = 0;
        _version++;
    }

    public bool Contains(T item)
    {
        return IndexOf(item) >= 0;
    }

    public int IndexOf(T item)
    {
        if (Count is 0)
            return -1;

        Debug.Assert(_items is not null);
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < Count; i++)
        {
            var index = (_startIndex + i) % _items.Length;
            if (comparer.Equals(item, _items[index]))
                return i;
        }

        return -1;
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);

        if (array.Rank is not 1)
            throw new ArgumentException("Array must be single-dimensional", nameof(array));

        if (arrayIndex < 0 || arrayIndex > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("The number of elements in the source collection is greater than the available space from arrayIndex to the end of the destination array.", nameof(array));

        if (Count is 0)
            return;

        Debug.Assert(_items is not null);
        var length = Math.Min(Count, _items.Length - _startIndex);
        Array.Copy(_items, _startIndex, array, arrayIndex, length);
        if (_startIndex + Count > _items.Length)
        {
            Array.Copy(_items, 0, array, arrayIndex + length, (_startIndex + Count) % _items.Length);
        }
    }

    void ICollection<T>.Add(T item)
    {
        AddLast(item);
    }

    bool ICollection<T>.Remove(T item)
    {
        throw new NotSupportedException();
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    [MemberNotNull(nameof(_items))]
    private void EnsureCapacityForOneMore()
    {
        if (_items is null)
        {
            _items = new T[_initialCapacity];
            _startIndex = 0;
            return;
        }

        if (Count < _items.Length)
            return;

        var newCapacity = Count * 2;
        if (newCapacity <= Count)
        {
            newCapacity = Count + 1;
        }

        var newItems = new T[newCapacity];
        CopyTo(newItems, 0);
        _items = newItems;
        _startIndex = 0;
    }

    public struct Enumerator : IEnumerator<T>, IEnumerator
    {
        private readonly DoubleEndedQueue<T> _queue;
        private int _index;
        private readonly int _version;

        internal Enumerator(DoubleEndedQueue<T> queue)
        {
            _queue = queue;
            _index = 0;
            _version = queue._version;
            Current = default!;
        }

        public readonly void Dispose()
        {
        }

        public bool MoveNext()
        {
            var localQueue = _queue;
            if (_version == localQueue._version && ((uint)_index < (uint)localQueue.Count))
            {
                Debug.Assert(localQueue._items is not null);
                var actualIndex = (localQueue._startIndex + _index) % localQueue._items.Length;
                Current = localQueue._items[actualIndex];
                _index++;
                return true;
            }

            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            if (_version != _queue._version)
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

            _index = _queue.Count + 1;
            Current = default!;
            return false;
        }

        public T Current { readonly get => field; private set; }

        readonly object? IEnumerator.Current
        {
            get
            {
                if (_index is 0 || _index == _queue.Count + 1)
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

                return Current;
            }
        }

        void IEnumerator.Reset()
        {
            if (_version != _queue._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }

            _index = 0;
            Current = default!;
        }
    }
}
