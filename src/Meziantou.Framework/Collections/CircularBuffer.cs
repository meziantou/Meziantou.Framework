using System.Collections;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Collections;

/// <summary>
/// A circular buffer (ring buffer) collection that maintains a fixed-size buffer and overwrites old items when full.
/// </summary>
/// <example>
/// <code>
/// var buffer = new CircularBuffer&lt;int&gt;(3) { AllowOverwrite = true };
/// buffer.Add(1); // [1]
/// buffer.Add(2); // [1, 2]
/// buffer.Add(3); // [1, 2, 3]
/// buffer.Add(4); // [2, 3, 4] - overwrites 1
/// </code>
/// </example>
public sealed class CircularBuffer<T> : ICollection<T>, IReadOnlyList<T>
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
            if (value <= 0)
                throw new ArgumentException("Maximum count must be greater than 0.", nameof(value));

            if (value < Count)
                throw new ArgumentOutOfRangeException(nameof(value), "Capacity was less than the current size.");

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
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _items[(_startIndex + index) % Capacity];
        }
    }

    public void Clear()
    {
        // Clear the elements so that the gc can reclaim the references.
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>() && Count > 0)
        {
            Array.Clear(_items, _startIndex, Math.Min(Count - _startIndex, Capacity - _startIndex));
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
            Current = default;
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
            Current = default;
            return false;
        }

        public T Current { readonly get => field!; private set; }

        readonly object? IEnumerator.Current
        {
            get
            {
                if (_index is 0 || _index == _list.Count + 1)
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

                return Current;
            }
        }

        void IEnumerator.Reset()
        {
            if (_version != _list._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }

            _index = 0;
            Current = default;
        }
    }
}
