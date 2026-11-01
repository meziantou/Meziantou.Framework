using System.Collections;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Collections;

/// <summary>A circular buffer (ring buffer) collection that maintains a fixed-size buffer and overwrites old items when full.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Tests/Collections/CircularBufferTests.cs" ref:region="CircularBuffer_AllowOverwrite" ref:dedent="true">
/// var buffer = new CircularBuffer&lt;int&gt;(3) { AllowOverwrite = true };
/// buffer.AddLast(1);
/// buffer.AddLast(2);
/// buffer.AddLast(3);
/// Assert.Equal([1, 2, 3], buffer);
///
/// buffer.AddLast(4); // overwrites 1
/// Assert.Equal([2, 3, 4], buffer);
/// </code>
/// </example>
#if PUBLIC_CIRCULARBUFFER
public
#else
internal
#endif
sealed class CircularBuffer<T> : ICollection<T>, IReadOnlyList<T>
{
    private T[] _items;
    private int _startIndex;
    private int _version;

    /// <summary>Gets or sets the maximum number of items the buffer can hold.</summary>
    public int Capacity
    {
        get => _items.Length;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            ArgumentOutOfRangeException.ThrowIfLessThan(value, Count);

            if (value != _items.Length)
            {
                var newItems = new T[value];
                if (Count > 0)
                {
                    CopyTo(newItems, 0);
                }

                _items = newItems;
                _startIndex = 0;
            }
        }
    }

    /// <summary>Gets the current number of items in the buffer.</summary>
    public int Count { get; private set; }

    /// <summary>Gets or sets a value indicating whether old items should be overwritten when the buffer is full.</summary>
    public bool AllowOverwrite { get; set; }

    bool ICollection<T>.IsReadOnly => false;

    /// <summary>Initializes a new instance of the <see cref="CircularBuffer{T}"/> class with the specified capacity.</summary>
    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Maximum count must be greater than 0.", nameof(capacity));

        _items = new T[capacity];
    }

    /// <summary>Adds an item to the beginning of the buffer.</summary>
    public void AddFirst(T value)
    {
        if (Count == Capacity)
        {
            if (!AllowOverwrite)
                throw new InvalidOperationException("The buffer is full");

            var index = (_startIndex - 1 + Capacity) % Capacity;
            _items[index] = value;
            _startIndex = index;
        }
        else
        {
            if (Count is 0)
            {
                _items[0] = value;
                _startIndex = 0;
            }
            else
            {
                var index = (_startIndex - 1 + Capacity) % Capacity;
                _items[index] = value;
                _startIndex = index;
            }

            Count++;
        }

        _version++;
    }

    public void AddLast(T value)
    {
        if (Count == Capacity)
        {
            if (!AllowOverwrite)
                throw new InvalidOperationException("The buffer is full");

            var index = (_startIndex + Count) % Capacity;
            _items[index] = value;
            _startIndex = (_startIndex + 1) % Capacity;
        }
        else
        {
            if (Count is 0)
            {
                _items[0] = value;
                _startIndex = 0;
            }
            else
            {
                var index = (_startIndex + Count) % Capacity;
                _items[index] = value;
            }

            Count++;
        }

        _version++;
    }

    public T RemoveFirst()
    {
        if (Count is 0)
            throw new InvalidOperationException("The buffer is empty");

        ref var item = ref _items[_startIndex];
        var result = item;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            item = default;
        }

        Count--;
        _startIndex = (_startIndex + 1) % Capacity;
        _version++;
        return result;
    }

    public T RemoveLast()
    {
        if (Count is 0)
            throw new InvalidOperationException("The buffer is empty");

        var index = (_startIndex + Count - 1) % Capacity;
        ref var item = ref _items[index];
        var result = item;
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            item = default;
        }

        Count--;
        _version++;
        return result;
    }

    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            return _items[(_startIndex + index) % Capacity];
        }
    }

    /// <summary>Replaces the item at the specified position.</summary>
    internal void SetItem(int index, T value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        _items[(_startIndex + index) % Capacity] = value;
        _version++;
    }

    /// <summary>Inserts an item at the specified position. The buffer must not be full.</summary>
    internal void Insert(int index, T value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count);
        if (Count == Capacity)
            throw new InvalidOperationException("The buffer is full");

        if (index < Count - index)
        {
            // Shift the items before the insertion point one slot to the left
            _startIndex = (_startIndex - 1 + Capacity) % Capacity;
            for (var i = 0; i < index; i++)
            {
                _items[(_startIndex + i) % Capacity] = _items[(_startIndex + i + 1) % Capacity];
            }
        }
        else
        {
            // Shift the items after the insertion point one slot to the right
            for (var i = Count; i > index; i--)
            {
                _items[(_startIndex + i) % Capacity] = _items[(_startIndex + i - 1) % Capacity];
            }
        }

        _items[(_startIndex + index) % Capacity] = value;
        Count++;
        _version++;
    }

    /// <summary>Removes the item at the specified position.</summary>
    internal void RemoveAt(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

        int emptiedSlot;
        if (index < Count - index - 1)
        {
            // Shift the items before the removed item one slot to the right
            for (var i = index; i > 0; i--)
            {
                _items[(_startIndex + i) % Capacity] = _items[(_startIndex + i - 1) % Capacity];
            }

            emptiedSlot = _startIndex;
            _startIndex = (_startIndex + 1) % Capacity;
        }
        else
        {
            // Shift the items after the removed item one slot to the left
            for (var i = index; i < Count - 1; i++)
            {
                _items[(_startIndex + i) % Capacity] = _items[(_startIndex + i + 1) % Capacity];
            }

            emptiedSlot = (_startIndex + Count - 1) % Capacity;
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items[emptiedSlot] = default!;
        }

        Count--;
        _version++;
    }

    public void Clear()
    {
        // Clear the elements so that the gc can reclaim the references.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Count > 0)
        {
            Array.Clear(_items, _startIndex, Math.Min(Count, Capacity - _startIndex));
            if (_startIndex + Count > Capacity)
            {
                Array.Clear(_items, 0, (_startIndex + Count) % Capacity);
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

    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("Destination array is not long enough to copy all the items in the collection. Check array index and length.", nameof(array));

        if (Count is 0)
            return;

        var length = Math.Min(Count, _items.Length - _startIndex);
        Array.Copy(_items, _startIndex, array, arrayIndex, length);
        if (_startIndex + Count > _items.Length)
        {
            Array.Copy(_items, 0, array, arrayIndex + length, (_startIndex + Count) % _items.Length);
        }
    }

    public int IndexOf(T item)
    {
        if (Count is 0)
            return -1;

        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < Count; i++)
        {
            var index = (_startIndex + i) % _items.Length;
            if (comparer.Equals(item, _items[index]))
                return i;
        }

        return -1;
    }

    void ICollection<T>.Add(T item)
    {
        AddLast(item);
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    bool ICollection<T>.Remove(T item)
    {
        throw new NotSupportedException();
    }

    public struct Enumerator : IEnumerator<T>, IEnumerator
    {
        private readonly CircularBuffer<T> _list;
        private int _index;
        private readonly int _version;

        internal Enumerator(CircularBuffer<T> list)
        {
            _list = list;
            _index = 0;
            _version = list._version;
            Current = default!;
        }

        public readonly void Dispose()
        {
        }

        public bool MoveNext()
        {
            var localList = _list;
            if (_version == localList._version && ((uint)_index < (uint)localList.Count))
            {
                var actualIndex = (_list._startIndex + _index) % _list.Capacity;
                Current = localList._items[actualIndex];
                _index++;
                return true;
            }

            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            if (_version != _list._version)
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

            _index = _list.Count + 1;
            Current = default!;
            return false;
        }

        public T Current { readonly get => field; private set; }

        readonly object? IEnumerator.Current
        {
            get
            {
                if (_index is 0 || _index == _list.Count + 1)
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

                return Current;
            }
        }

        void IEnumerator.Reset() => Reset();

        internal void Reset()
        {
            if (_version != _list._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }

            _index = 0;
            Current = default!;
        }
    }
}
