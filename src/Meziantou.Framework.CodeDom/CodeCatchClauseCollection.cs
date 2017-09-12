using System;
using System.Collections;
using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class CodeCatchClauseCollection : CodeObject, IList<CodeCatchClause>
    {
        private List<CodeCatchClause> _list = new List<CodeCatchClause>();

        public CodeCatchClause this[int index]
        {
            get => ((IList<CodeCatchClause>)_list)[index];
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                ((IList<CodeCatchClause>)_list)[index] = value;
            }
        }

        public int Count => ((IList<CodeCatchClause>)_list).Count;

        public bool IsReadOnly => ((IList<CodeCatchClause>)_list).IsReadOnly;

        public void Add(CodeCatchClause item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            ((IList<CodeCatchClause>)_list).Add(item);
        }

        public void Clear()
        {
            ((IList<CodeCatchClause>)_list).Clear();
        }

        public bool Contains(CodeCatchClause item)
        {
            return ((IList<CodeCatchClause>)_list).Contains(item);
        }

        public void CopyTo(CodeCatchClause[] array, int arrayIndex)
        {
            ((IList<CodeCatchClause>)_list).CopyTo(array, arrayIndex);
        }

        public IEnumerator<CodeCatchClause> GetEnumerator()
        {
            return ((IList<CodeCatchClause>)_list).GetEnumerator();
        }

        public int IndexOf(CodeCatchClause item)
        {
            return ((IList<CodeCatchClause>)_list).IndexOf(item);
        }

        public void Insert(int index, CodeCatchClause item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            ((IList<CodeCatchClause>)_list).Insert(index, item);
        }

        public bool Remove(CodeCatchClause item)
        {
            return ((IList<CodeCatchClause>)_list).Remove(item);
        }

        public void RemoveAt(int index)
        {
            ((IList<CodeCatchClause>)_list).RemoveAt(index);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IList<CodeCatchClause>)_list).GetEnumerator();
        }
    }
}
