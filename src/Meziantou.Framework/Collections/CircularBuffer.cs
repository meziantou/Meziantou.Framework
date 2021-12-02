﻿using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.Collections
{
    public sealed class CircularBuffer<T> : ICollection<T>, IReadOnlyList<T>
    {
        private T[] _items;
        private int _startIndex;
        private int _size;
        private int _version;

        public int Capacity
        {
            get => _items.Length;
            set
            {
                if (value <= 0)
                    throw new ArgumentException("Maximum count must be greater than 0.", nameof(value));

                if (value < _size)
                    throw new ArgumentOutOfRangeException(nameof(value), "Capacity was less than the current size.");

                if (value != _items.Length)
                {
                    var newItems = new T[value];
                    if (_size > 0)
                    {
                        CopyTo(newItems, 0);
                    }

                    _items = newItems;
                    _startIndex = 0;
                }
            }
        }

        public int Count => _size;

        public bool AllowOverwrite { get; set; }

        bool ICollection<T>.IsReadOnly => false;

        public CircularBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Maximum count must be greater than 0.", nameof(capacity));

            _items = new T[capacity];
        }

        public void AddFirst(T value)
        {
            if (_size == Capacity)
            {
                if (!AllowOverwrite)
                    throw new InvalidOperationException("The buffer is full");

                var index = (_startIndex - 1 + Capacity) % Capacity;
                _items[index] = value;
                _startIndex = index;
            }
            else
            {
                if (_size == 0)
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

                _size++;
            }

            _version++;
        }

        public void AddLast(T value)
        {
            if (_size == Capacity)
            {
                if (!AllowOverwrite)
                    throw new InvalidOperationException("The buffer is full");

                var index = (_startIndex + _size) % Capacity;
                _items[index] = value;
                _startIndex = (_startIndex + 1) % Capacity;
            }
            else
            {
                if (_size == 0)
                {
                    _items[0] = value;
                    _startIndex = 0;
                }
                else
                {
                    var index = (_startIndex + _size) % Capacity;
                    _items[index] = value;
                }

                _size++;
            }

            _version++;
        }

        public T RemoveFirst()
        {
            if (_size == 0)
                throw new InvalidOperationException("The buffer is empty");

            var item = _items[_startIndex];
            _size--;
            _startIndex = (_startIndex + 1) % Capacity;
            _version++;
            return item;
        }

        public T RemoveLast()
        {
            if (_size == 0)
                throw new InvalidOperationException("The buffer is empty");

            var item = _items[(_startIndex + _size) % Capacity];
            _size--;
            _version++;
            return item;
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
            if (_size > 0)
            {
                // Clear the elements so that the gc can reclaim the references.
                Array.Clear(_items, _startIndex, Math.Min(_size - _startIndex, Capacity - _startIndex));
                if (_startIndex + _size > Capacity)
                {
                    Array.Clear(_items, 0, (_startIndex + _size) % Capacity);
                }
            }

            _size = 0;
            _startIndex = 0;
            _version++;
        }

        public bool Contains(T item)
        {
            return IndexOf(item) >= 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            var length = Math.Min(_size, _items.Length - _startIndex);
            Array.Copy(_items, _startIndex, array, arrayIndex, length);
            if (_startIndex + _size > _items.Length)
            {
                Array.Copy(_items, 0, array, arrayIndex + length, (_startIndex + _size) % _items.Length);
            }
        }

        public int IndexOf(T item)
        {
            if (_size == 0)
                return -1;

            var comparer = EqualityComparer<T>.Default;
            for (var i = 0; i < _size; i++)
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

        public Enumerator GetEnumerator() => new Enumerator(this);
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
            private T? _current;

            internal Enumerator(CircularBuffer<T> list)
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
                    var actualIndex = (_list._startIndex + _index) % _list.Capacity;
                    _current = localList._items[actualIndex];
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
