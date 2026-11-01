using System.Collections;

namespace Meziantou.Framework.Collections;

/// <summary>A list that holds at most <see cref="Capacity"/> items, and drops the item at the other end when an item is added to a full list.</summary>
public sealed class LimitList<T> : IList<T>, IReadOnlyList<T>
{
    private readonly CircularBuffer<T> _buffer;

    public int Capacity => _buffer.Capacity;

    public int Count => _buffer.Count;

    bool ICollection<T>.IsReadOnly => false;

    public LimitList(int maximumCount)
    {
        if (maximumCount <= 0)
            throw new ArgumentException("Maximum count must be greater than 0.", nameof(maximumCount));

        _buffer = new CircularBuffer<T>(maximumCount) { AllowOverwrite = true };
    }

    /// <summary>Adds an item at the beginning of the list. When the list is full, the last item is removed.</summary>
    public void AddFirst(T value) => _buffer.AddFirst(value);

    /// <summary>Adds an item at the end of the list. When the list is full, the first item is removed.</summary>
    public void AddLast(T value) => _buffer.AddLast(value);

    public void RemoveFirst() => _buffer.RemoveFirst();

    public void RemoveLast() => _buffer.RemoveLast();

    /// <summary>Gets or sets the item at the specified index. Setting the item at index <see cref="Count"/> adds the item at the end of the list.</summary>
    public T this[int index]
    {
        get => _buffer[index];
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Capacity);

            if (index == Count)
            {
                AddLast(value);
                return;
            }

            _buffer.SetItem(index, value);
        }
    }

    public void Clear() => _buffer.Clear();

    public bool Contains(T item) => _buffer.Contains(item);

    public void CopyTo(T[] array, int arrayIndex) => _buffer.CopyTo(array, arrayIndex);

    public bool Remove(T item)
    {
        var index = IndexOf(item);
        if (index < 0)
            return false;

        _buffer.RemoveAt(index);
        return true;
    }

    public int IndexOf(T item) => _buffer.IndexOf(item);

    /// <summary>Inserts an item at the specified index. When the list is full, inserting at index <see cref="Count"/> removes the first item, and inserting at any other index removes the last item.</summary>
    public void Insert(int index, T item)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count);

        if (index == Count)
        {
            AddLast(item);
            return;
        }

        if (Count == Capacity)
        {
            _buffer.RemoveLast();
        }

        _buffer.Insert(index, item);
    }

    public void RemoveAt(int index) => _buffer.RemoveAt(index);

    void ICollection<T>.Add(T item) => AddLast(item);

    public Enumerator GetEnumerator() => new(_buffer.GetEnumerator());

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<T>
    {
        private CircularBuffer<T>.Enumerator _enumerator;

        internal Enumerator(CircularBuffer<T>.Enumerator enumerator)
        {
            _enumerator = enumerator;
        }

        public readonly T Current => _enumerator.Current;

        readonly object? IEnumerator.Current => ((IEnumerator)_enumerator).Current;

        public bool MoveNext() => _enumerator.MoveNext();

        void IEnumerator.Reset() => _enumerator.Reset();

        public readonly void Dispose()
        {
        }
    }
}
