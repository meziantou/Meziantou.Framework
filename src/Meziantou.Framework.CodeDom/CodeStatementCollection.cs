using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class CodeStatementCollection : CodeObject, IList<CodeStatement>
    {
        private readonly List<CodeStatement> _statements = new List<CodeStatement>();

        public IEnumerator<CodeStatement> GetEnumerator()
        {
            return _statements.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_statements).GetEnumerator();
        }

        public void Add(CodeStatement item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _statements.Add(item);
            SetParent(item);
        }

        public void Clear()
        {
            foreach (var statement in _statements)
            {
                statement.Parent = null;
            }
            _statements.Clear();
        }

        public bool Contains(CodeStatement item)
        {
            return _statements.Contains(item);
        }

        public void CopyTo(CodeStatement[] array, int arrayIndex)
        {
            _statements.CopyTo(array, arrayIndex);
        }

        public bool Remove(CodeStatement item)
        {
            var remove = _statements.Remove(item);
            if (remove)
            {
                item.Parent = null;
            }
            return remove;
        }

        public int Count
        {
            get { return _statements.Count; }
        }

        public bool IsReadOnly
        {
            get { return ((IList<CodeStatement>)_statements).IsReadOnly; }
        }

        public int IndexOf(CodeStatement item)
        {
            return _statements.IndexOf(item);
        }

        public void Insert(int index, CodeStatement item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _statements.Insert(index, item);
            SetParent(item);
        }

        public void RemoveAt(int index)
        {
            var item = _statements[index];
            if (item == null)
                return;

            _statements.RemoveAt(index);
            item.Parent = null;
        }

        public CodeStatement this[int index]
        {
            get { return _statements[index]; }
            set
            {
                if(value == null) throw new ArgumentNullException(nameof(value));

                var item = _statements[index];
                item.Parent = null;
                _statements[index] = value;
                SetParent(value);
            }
        }

        public static implicit operator CodeStatementCollection(CodeStatement codeStatement)
        {
            CodeStatementCollection collection = new CodeStatementCollection();
            collection.Add(codeStatement);
            return collection;
        }
    }
}