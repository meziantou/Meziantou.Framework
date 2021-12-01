using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.Collections
{
    public sealed class LimitList<T> : ICollection<T>, IReadOnlyList<T>
    {
        private readonly LinkedList<T> _list = new();

        public int Capacity { get; }

        public int Count => _list.Count;

        bool ICollection<T>.IsReadOnly => false;

        public LimitList(int maximumCount)
        {
            if (maximumCount <= 0)
                throw new ArgumentException("Maximum count must be greater than 0.", nameof(maximumCount));

            Capacity = maximumCount;
        }

        public void AddFirst(T value)
        {
            if (_list.Count == Capacity)
            {
                _list.RemoveLast();
            }

            _list.AddFirst(value);
        }

        public void AddLast(T value)
        {
            if (_list.Count == Capacity)
            {
                _list.RemoveFirst();
            }

            _list.AddLast(value);
        }

        public void RemoveFirst()
        {
            _list.RemoveFirst();
        }

        public void RemoveLast()
        {
            _list.RemoveLast();
        }

        public T this[int index]
        {
            get
            {
                if (index < 0 || index >= Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                var i = 0;
                foreach (var item in _list)
                {
                    if (i == index)
                        return item;

                    i++;
                }
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            set
            {
                if (index < 0 || index >= Capacity || index > Count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                if (index == Count)
                {
                    AddLast(value);
                    return;
                }

                var i = 0;
                var node = _list.First;
                while (node != null)
                {
                    if (i == index)
                    {
                        node.Value = value;
                        return;
                    }

                    node = node.Next;
                    i++;
                }

                throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public void Clear()
        {
            _list.Clear();
        }

        public bool Contains(T item)
        {
            return _list.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        public bool Remove(T item)
        {
            return _list.Remove(item);
        }

        public int IndexOf(T item)
        {
            var equalityComparer = EqualityComparer<T>.Default;
            var index = 0;
            foreach (var listItem in _list)
            {
                if (equalityComparer.Equals(listItem, item))
                    return index;

                index++;
            }

            return -1;
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            var node = _list.First;
            var i = 0;
            while (node != null)
            {
                if (i == index)
                {
                    _list.Remove(node);
                    return;
                }

                node = node.Next;
                i++;
            }
        }

        void ICollection<T>.Add(T item)
        {
            AddLast(item);
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
