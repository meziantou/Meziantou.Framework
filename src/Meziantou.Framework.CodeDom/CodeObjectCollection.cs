using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class CodeObjectCollection<T> : CodeObject, IList<T>, IReadOnlyList<T> where T : CodeObject
    {
        private readonly IList<T> _list = new List<T>();

        public CodeObjectCollection()
        {
        }

        public CodeObjectCollection(CodeObject parent)
        {
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_list).GetEnumerator();
        }

        public void AddRange(IEnumerable<T> items)
        {
            foreach (var item in items)
            {
                Add(item);
            }
        }

        public void Add(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            _list.Add(item);
            item.Parent = Parent;
        }

        public void Clear()
        {
            foreach (var item in _list)
            {
                item.Parent = null;
            }
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
            var remove = _list.Remove(item);
            if (remove)
            {
                item.Parent = null;
            }
            return remove;
        }

        public int Count => _list.Count;

        public bool IsReadOnly => _list.IsReadOnly;

        public int IndexOf(T item)
        {
            return _list.IndexOf(item);
        }

        public void Insert(int index, T item)
        {
            _list.Insert(index, item);
            item.Parent = Parent;
        }

        public void RemoveAt(int index)
        {
            var item = this[index];
            if (item == null)
                return;

            _list.RemoveAt(index);
            item.Parent = null;
        }

        public T this[int index]
        {
            get { return _list[index]; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                var item = this[index];
                _list[index] = value;
                if (item != null)
                {
                    item.Parent = null;
                }
                value.Parent = Parent;
            }
        }
    }
}