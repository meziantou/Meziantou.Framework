using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace Meziantou.Framework.Html
{
    public sealed class HtmlAttributeList : INotifyCollectionChanged, IList<HtmlAttribute>, IList, IReadOnlyList<HtmlAttribute>
    {
        private readonly List<HtmlAttribute> _list = new List<HtmlAttribute>();

        public event NotifyCollectionChangedEventHandler CollectionChanged;

        internal HtmlAttributeList(HtmlNode parent)
        {
            Parent = parent;
        }

        public HtmlNode Parent { get; }

        private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            Parent.ClearCaches();
            CollectionChanged?.Invoke(this, e);
        }

        public HtmlAttribute Add(string prefix, string localName, string namespaceURI)
        {
            return Add(prefix, localName, namespaceURI, null);
        }

        public HtmlAttribute Add(string prefix, string localName, string namespaceURI, string value)
        {
            if (prefix == null)
                throw new ArgumentNullException(nameof(prefix));

            if (localName == null)
                throw new ArgumentNullException(nameof(localName));

            if (Parent == null || Parent.OwnerDocument == null)
                throw new InvalidOperationException();

            if (string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(namespaceURI))
            {
                prefix = Parent.OwnerDocument.GetPrefixOfNamespace(namespaceURI);
            }

            HtmlAttribute att = Parent.OwnerDocument.CreateAttribute(prefix, localName, namespaceURI);
            att.Value = value;
            Add(att);
            return att;
        }

        public HtmlAttribute Add(string name, string value)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            if (Parent == null || Parent.OwnerDocument == null)
                throw new InvalidOperationException();

            HtmlAttribute att = Parent.OwnerDocument.CreateAttribute(string.Empty, name, string.Empty);
            att.Value = value;
            Add(att);
            return att;
        }

        public void Add(HtmlAttribute attribute)
        {
            Add(attribute, true);
        }

        public void Add(HtmlAttribute attribute, bool replace)
        {
            if (attribute == null)
                throw new ArgumentNullException(nameof(attribute));

            if (attribute.ParentNode != null)
                throw new ArgumentException(null, nameof(attribute));

            HtmlAttribute att = this[attribute.LocalName, attribute.NamespaceURI];
            if (att != null)
            {
                if (!replace)
                    throw new ArgumentException("The same attribute (" + att.NamespaceURI + ":" + att.LocalName + ") has has already been added.", nameof(attribute));

                Remove(att);
            }

            AddNoCheck(attribute);
        }

        internal void AddNoCheck(HtmlAttribute attribute)
        {
            if (attribute == null)
                throw new ArgumentNullException(nameof(attribute));

            _list.Add(attribute);
            attribute.ParentNode = Parent;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, attribute));
        }

        public string GetNamespacePrefixIfDefined(string namespaceURI)
        {
            if (namespaceURI == null)
                throw new ArgumentNullException(nameof(namespaceURI));

            foreach (HtmlAttribute att in _list)
            {
                if ((att.Name == HtmlNode.XmlnsPrefix || att.Prefix == HtmlNode.XmlnsPrefix) && att.Value == namespaceURI)
                    return att.LocalName;
            }

            return null;
        }

        public void RemoveAll()
        {
            foreach (HtmlAttribute att in _list)
            {
                if (att.ParentNode != Parent)
                    throw new ArgumentException();

                att.ParentNode = null;
            }
            _list.Clear();
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void Insert(int index, HtmlAttribute item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (item.ParentNode != null)
                throw new ArgumentException(null, nameof(item));

            _list.Insert(index, item);
            item.ParentNode = Parent;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
        }

        public bool Contains(HtmlAttribute item)
        {
            return IndexOf(item) >= 0;
        }

        public void CopyTo(HtmlAttribute[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        public int IndexOf(HtmlAttribute item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            return _list.IndexOf(item);
        }

        public int IndexOf(string name)
        {
            if (name == null)
                return -1;

            return _list.FindIndex(a => name.EqualsIgnoreCase(a.Name));
        }

        public int IndexOf(string localName, string namespaceURI)
        {
            if (localName == null || namespaceURI == null)
                return -1;

            return _list.FindIndex(a =>
                localName.EqualsIgnoreCase(a.LocalName) &&
                a.NamespaceURI != null && namespaceURI == a.NamespaceURI);
        }

        public bool RemoveAt(int index)
        {
            if (index < 0 || index >= _list.Count)
                return false;

            HtmlAttribute att = _list[index];
            if (att.ParentNode != Parent)
                throw new ArgumentException(null, nameof(index));

            _list.RemoveAt(index);
            att.ParentNode = null;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, att, index));
            return true;
        }

        public void RemoveRange(IEnumerable<HtmlAttribute> attributes)
        {
            if (attributes == null)
                return;

            foreach (HtmlAttribute att in attributes)
            {
                Remove(att);
            }
        }

        public bool RemoveByPrefix(string prefix, string localName)
        {
            if (prefix == null)
                throw new ArgumentNullException(nameof(prefix));

            if (localName == null)
                throw new ArgumentNullException(nameof(localName));

            HtmlAttribute att = _list.Find(a => localName.EqualsIgnoreCase(a.LocalName) && prefix == a.Prefix);
            if (att == null)
                return false;

            return Remove(att);
        }

        public bool Remove(string localName, string namespaceURI)
        {
            if (localName == null)
                throw new ArgumentNullException(nameof(localName));

            if (namespaceURI == null)
                throw new ArgumentNullException(nameof(namespaceURI));

            HtmlAttribute att = this[localName, namespaceURI];
            if (att == null)
                return false;

            return Remove(att);
        }

        public bool Remove(string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            HtmlAttribute att = this[name];
            if (att == null)
                return false;

            return Remove(att);
        }

        public bool Remove(HtmlAttribute attribute)
        {
            if (attribute == null)
                throw new ArgumentNullException(nameof(attribute));

            if (attribute.ParentNode != Parent)
                throw new ArgumentException(null, nameof(attribute));

            if (!_list.Remove(attribute))
                throw new ArgumentException(null, nameof(attribute));

            attribute.ParentNode = null;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, attribute));
            return true;
        }

        public HtmlAttribute this[string name]
        {
            get
            {
                if (name == null)
                    throw new ArgumentNullException(nameof(name));

                return _list.Find(a => name.EqualsIgnoreCase(a.Name));
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (value.ParentNode != null)
                    throw new ArgumentException(null, nameof(value));

                int index = IndexOf(name);
                if (index < 0)
                {
                    AddNoCheck(value);
                }
                else
                {
                    this[index] = value;
                }
            }
        }

        public HtmlAttribute this[string localName, string namespaceURI]
        {
            get
            {
                if (localName == null)
                    throw new ArgumentNullException(nameof(localName));

                if (namespaceURI == null)
                    throw new ArgumentNullException(nameof(namespaceURI));

                return _list.Find(a =>
                    localName.EqualsIgnoreCase(a.LocalName) &&
                    a.NamespaceURI != null && namespaceURI == a.NamespaceURI);
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                if (value.ParentNode != null)
                    throw new ArgumentException(null, nameof(value));

                int index = IndexOf(localName, namespaceURI);
                if (index < 0)
                {
                    AddNoCheck(value);
                }
                else
                {
                    this[index] = value;
                }
            }
        }

        public HtmlAttribute this[int index]
        {
            get
            {
                return _list[index];
            }
            set
            {
                if (value == _list[index])
                    return;

                HtmlAttribute oldItem = _list[index];

                _list[index] = value;
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, oldItem));
            }
        }

        public int Count
        {
            get
            {
                return _list.Count;
            }
        }

        public IEnumerator<HtmlAttribute> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        bool ICollection<HtmlAttribute>.IsReadOnly
        {
            get
            {
                return false;
            }
        }

        void ICollection<HtmlAttribute>.Clear()
        {
            RemoveAll();
        }

        int IList.Add(object value)
        {
            int count = Count;
            Add((HtmlAttribute)value);
            return count;
        }

        void IList.Clear()
        {
            RemoveAll();
        }

        bool IList.Contains(object value)
        {
            return Contains((HtmlAttribute)value);
        }

        int IList.IndexOf(object value)
        {
            return IndexOf((HtmlAttribute)value);
        }

        void IList.Insert(int index, object value)
        {
            Insert(index, (HtmlAttribute)value);
        }

        bool IList.IsFixedSize
        {
            get
            {
                return false;
            }
        }

        bool IList.IsReadOnly
        {
            get
            {
                return false;
            }
        }

        void IList.Remove(object value)
        {
            Remove((HtmlAttribute)value);
        }

        void IList.RemoveAt(int index)
        {
            RemoveAt(index);
        }

        void IList<HtmlAttribute>.RemoveAt(int index)
        {
            RemoveAt(index);
        }

        object IList.this[int index]
        {
            get
            {
                return this[index];
            }
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                this[index] = (HtmlAttribute)value;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            ((ICollection)_list).CopyTo(array, index);
        }

        bool ICollection.IsSynchronized
        {
            get
            {
                return ((ICollection)_list).IsSynchronized;
            }
        }

        object ICollection.SyncRoot
        {
            get
            {
                return ((ICollection)_list).SyncRoot;
            }
        }
    }
}
