using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class CodeExpressionCollectionStatement : CodeStatement, IEnumerable<CodeExpression>
    {
        private readonly List<CodeExpression> _expressions = new List<CodeExpression>();

        public IEnumerator<CodeExpression> GetEnumerator()
        {
            return _expressions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_expressions).GetEnumerator();
        }

        public void Add(CodeExpression item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _expressions.Add(item);
            SetParent(item);
        }

        public void Clear()
        {
            foreach (var statement in _expressions)
            {
                statement.Parent = null;
            }
            _expressions.Clear();
        }

        public bool Contains(CodeExpression item)
        {
            return _expressions.Contains(item);
        }

        public void CopyTo(CodeExpression[] array, int arrayIndex)
        {
            _expressions.CopyTo(array, arrayIndex);
        }

        public bool Remove(CodeExpression item)
        {
            var remove = _expressions.Remove(item);
            if (remove)
            {
                item.Parent = null;
            }
            return remove;
        }

        public int Count
        {
            get { return _expressions.Count; }
        }

        public bool IsReadOnly
        {
            get { return ((IList<CodeExpression>)_expressions).IsReadOnly; }
        }

        public int IndexOf(CodeExpression item)
        {
            return _expressions.IndexOf(item);
        }

        public void Insert(int index, CodeExpression item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            _expressions.Insert(index, item);
            SetParent(item);
        }

        public void RemoveAt(int index)
        {
            var item = _expressions[index];
            if (item == null)
                return;

            _expressions.RemoveAt(index);
            item.Parent = null;
        }

        public CodeExpression this[int index]
        {
            get { return _expressions[index]; }
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));

                var item = _expressions[index];
                item.Parent = null;
                _expressions[index] = value;
                SetParent(value);
            }
        }

        public static implicit operator CodeExpressionCollectionStatement(CodeExpression expression)
        {
            var collection = new CodeExpressionCollectionStatement();
            collection.Add(expression);
            return collection;
        }
    }
}
