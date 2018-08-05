using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class ExpressionCollectionStatement : Statement, IEnumerable<Expression>
    {
        private readonly List<Expression> _expressions = new List<Expression>();

        public ExpressionCollectionStatement()
        {
        }

        public ExpressionCollectionStatement(params Expression[] expressions)
        {
            foreach (var expression in expressions)
            {
                Add(expression);
            }
        }

        public IEnumerator<Expression> GetEnumerator()
        {
            return _expressions.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_expressions).GetEnumerator();
        }

        public void Add(Expression item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
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

        public bool Contains(Expression item)
        {
            return _expressions.Contains(item);
        }

        public void CopyTo(Expression[] array, int arrayIndex)
        {
            _expressions.CopyTo(array, arrayIndex);
        }

        public bool Remove(Expression item)
        {
            var remove = _expressions.Remove(item);
            if (remove)
            {
                item.Parent = null;
            }
            return remove;
        }

        public int Count => _expressions.Count;

        public bool IsReadOnly => ((IList<Expression>)_expressions).IsReadOnly;

        public int IndexOf(Expression item)
        {
            return _expressions.IndexOf(item);
        }

        public void Insert(int index, Expression item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));
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

        public Expression this[int index]
        {
            get => _expressions[index];
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                var item = _expressions[index];
                item.Parent = null;
                _expressions[index] = value;
                SetParent(value);
            }
        }

        public static implicit operator ExpressionCollectionStatement(Expression expression) => new ExpressionCollectionStatement { expression };
    }
}
