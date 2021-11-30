﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Collections
{
    public sealed class SortedList<T> : ICollection<T>, ICollection, IReadOnlyList<T>
    {
        private readonly IComparer<T> _comparer;
        private T[] _items;
        private int _size;
        private int _version;

        public SortedList()
        {
            _items = Array.Empty<T>();
            _size = 0;
            _comparer = Comparer<T>.Default;
        }

        public SortedList(int capacity)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Non-negative number required.");

            _items = capacity == 0 ? Array.Empty<T>() : new T[capacity];
            _comparer = Comparer<T>.Default;
        }

        public SortedList(IComparer<T>? comparer)
        {
            _items = Array.Empty<T>();
            _size = 0;
            _comparer = comparer ?? Comparer<T>.Default;
        }

        public SortedList(int capacity, IComparer<T>? comparer)
        {
            if (capacity < 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Non-negative number required.");

            _items = capacity == 0 ? Array.Empty<T>() : new T[capacity];
            _size = 0;
            _comparer = comparer ?? Comparer<T>.Default;
        }

        bool ICollection<T>.IsReadOnly => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        public int Count => _size;

        public T this[int index] => _items[index];

        public int Capacity
        {
            get => _items.Length;
            set
            {
                if (value < _size)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Capacity was less than the current size.");
                }

                if (value != _items.Length)
                {
                    if (value > 0)
                    {
                        var newItems = new T[value];
                        if (_size > 0)
                        {
                            Array.Copy(_items, newItems, _size);
                        }
                        _items = newItems;
                    }
                    else
                    {
                        _items = Array.Empty<T>();
                    }
                }
            }
        }

        public void Add(T item)
        {
            var index = BinarySearch(item);
            if (index < 0)
            {
                index = ~index;
            }

            if (_size == _items.Length)
            {
                EnsureCapacity(_size + 1);
            }

            if (index < _size)
            {
                Array.Copy(_items, index, _items, index + 1, _size - index);
            }

            _items[index] = item;
            _size++;
            _version++;
        }

        public bool Remove(T item)
        {
            var index = IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }

            return false;
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_size)
            {
                ThrowHelper.ThrowArgumentOutOfRange_IndexException();
            }

            _size--;
            if (index < _size)
            {
                Array.Copy(_items, index + 1, _items, index, _size - index);
            }

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                _items[_size] = default!;
            }

            _version++;
        }

        public void Clear()
        {
            _version++;
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                var size = _size;
                _size = 0;
                if (size > 0)
                {
                    Array.Clear(_items, 0, size); // Clear the elements so that the gc can reclaim the references.
                }
            }
            else
            {
                _size = 0;
            }
        }

        public void CopyTo(T[] array) => CopyTo(array, 0);

        void ICollection.CopyTo(Array array, int arrayIndex)
        {
            if (array != null && array.Rank != 1)
                throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", nameof(array));

            try
            {
                // Array.Copy will check for NULL.
                Array.Copy(_items, 0, array!, arrayIndex, _size);
            }
            catch (ArrayTypeMismatchException)
            {
                throw new ArgumentException("Target array type is not compatible with the type of items in the collection.", nameof(array));
            }
        }

        [SuppressMessage("Usage", "MA0015:Specify the parameter name in ArgumentException", Justification = "Multiple parameters are not supported by the exception")]
        public void CopyTo(int index, T[] array, int arrayIndex, int count)
        {
            if (_size - index < count)
                throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");

            // Delegate rest of error checking to Array.Copy.
            Array.Copy(_items, index, array, arrayIndex, count);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            // Delegate rest of error checking to Array.Copy.
            Array.Copy(_items, 0, array, arrayIndex, _size);
        }

        public bool Contains(T item)
        {
            return BinarySearch(item) >= 0;
        }

        public int IndexOf(T item)
        {
            var index = BinarySearch(item);
            if (index < 0)
                return -1;

            return index;
        }

        public int FirstIndexOf(T item)
        {
            var index = BinarySearch(item);
            if (index < 0)
                return -1;

            while (index > 0)
            {
                if (_comparer.Compare(_items[index - 1], item) == 0)
                {
                    index--;
                }
                else
                {
                    break;
                }
            }

            return index;
        }

        public int LastIndexOf(T item)
        {
            var index = BinarySearch(item);
            if (index < 0)
                return -1;

            while (index < _size - 1)
            {
                if (_comparer.Compare(_items[index + 1], item) == 0)
                {
                    index++;
                }
                else
                {
                    break;
                }
            }

            return index;
        }

        public int BinarySearch(T item)
        {
            return Array.BinarySearch(_items, index: 0, length: _size, item, _comparer);
        }

        private void EnsureCapacity(int min)
        {
            var newCapacity = _items.Length == 0 ? 4 : _items.Length * 2;

            // Allow the list to grow to maximum possible capacity (~2G elements) before encountering overflow.
            // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
            if ((uint)newCapacity > Array.MaxLength)
            {
                newCapacity = Array.MaxLength;
            }

            if (newCapacity < min)
            {
                newCapacity = min;
            }

            Capacity = newCapacity;
        }

        public ReadOnlySpan<T> UnsafeAsReadOnlySpan()
        {
            return _items.AsSpan(0, _size);
        }

        public Enumerator GetEnumerator() => new(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IEnumerator<T>, IEnumerator
        {
            private readonly SortedList<T> _list;
            private int _index;
            private readonly int _version;
            private T? _current;

            internal Enumerator(SortedList<T> list)
            {
                _list = list;
                _index = 0;
                _version = list._version;
                _current = default;
            }

            public void Dispose()
            {
            }

            public bool MoveNext()
            {
                var localList = _list;
                if (_version == localList._version && ((uint)_index < (uint)localList._size))
                {
                    _current = localList._items[_index];
                    _index++;
                    return true;
                }
                return MoveNextRare();
            }

            private bool MoveNextRare()
            {
                if (_version != _list._version)
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

                _index = _list._size + 1;
                _current = default;
                return false;
            }

            public T Current => _current!;

            object? IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || _index == _list._size + 1)
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
                _current = default;
            }
        }
    }
}
