using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class CodeTypeParameterConstraintCollection : CodeObject, IList<CodeTypeParameterConstraint>
    {
        private readonly List<CodeTypeParameterConstraint> _statements = new List<CodeTypeParameterConstraint>();
        
        public IEnumerator<CodeTypeParameterConstraint> GetEnumerator()
        {
            return _statements.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_statements).GetEnumerator();
        }

        public void Add(CodeTypeParameterConstraint item)
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

        public bool Contains(CodeTypeParameterConstraint item)
        {
            return _statements.Contains(item);
        }

        public void CopyTo(CodeTypeParameterConstraint[] array, int arrayIndex)
        {
            _statements.CopyTo(array, arrayIndex);
        }

        public bool Remove(CodeTypeParameterConstraint item)
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

        public int IndexOf(CodeTypeParameterConstraint item)
        {
            return _statements.IndexOf(item);
        }

        public void Insert(int index, CodeTypeParameterConstraint item)
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

        public CodeTypeParameterConstraint this[int index]
        {
            get { return _statements[index]; }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));

                var item = _statements[index];
                item.Parent = null;
                _statements[index] = value;
                SetParent(value);
            }
        }

        public static implicit operator CodeTypeParameterConstraintCollection(CodeTypeParameterConstraint codeConstraint)
        {
            CodeTypeParameterConstraintCollection collection = new CodeTypeParameterConstraintCollection();
            collection.Add(codeConstraint);
            return collection;
        }
    }
}