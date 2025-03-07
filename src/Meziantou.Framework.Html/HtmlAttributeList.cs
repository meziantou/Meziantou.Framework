#nullable disable
using System.Collections;
using System.Collections.Specialized;

namespace Meziantou.Framework.Html;

#if HTML_PUBLIC
public
#else
internal
#endif
sealed class HtmlAttributeList : INotifyCollectionChanged, IList<HtmlAttribute>, IList, IReadOnlyList<HtmlAttribute>
{
    private readonly List<HtmlAttribute> _attributes = [];

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

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public HtmlAttribute Add(string prefix, string localName, string namespaceURI)
    {
        return Add(prefix, localName, namespaceURI, value: null);
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public HtmlAttribute Add(string prefix, string localName, string namespaceURI, string value)
    {
        if (prefix is null)
            throw new ArgumentNullException(nameof(prefix));

        if (localName is null)
            throw new ArgumentNullException(nameof(localName));

        if (Parent is null || Parent.OwnerDocument is null)
            throw new InvalidOperationException();

        if (string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(namespaceURI))
        {
            prefix = Parent.OwnerDocument.GetPrefixOfNamespace(namespaceURI);
        }

        var att = Parent.OwnerDocument.CreateAttribute(prefix, localName, namespaceURI);
        att.Value = value;
        Add(att);
        return att;
    }

    public HtmlAttribute Add(string name, string value)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        if (Parent is null || Parent.OwnerDocument is null)
            throw new InvalidOperationException();

        var att = Parent.OwnerDocument.CreateAttribute(string.Empty, name, string.Empty);
        att.Value = value;
        Add(att);
        return att;
    }

    public void Add(HtmlAttribute item)
    {
        Add(item, replace: true);
    }

    public void Add(HtmlAttribute attribute, bool replace)
    {
        if (attribute is null)
            throw new ArgumentNullException(nameof(attribute));

        if (attribute.ParentNode is not null)
            throw new ArgumentException(message: null, nameof(attribute));

        var att = this[attribute.LocalName, attribute.NamespaceURI];
        if (att is not null)
        {
            if (!replace)
                throw new ArgumentException("The same attribute (" + att.NamespaceURI + ":" + att.LocalName + ") has has already been added.", nameof(attribute));

            Remove(att);
        }

        AddNoCheck(attribute);
    }

    internal void AddNoCheck(HtmlAttribute attribute)
    {
        if (attribute is null)
            throw new ArgumentNullException(nameof(attribute));

        _attributes.Add(attribute);
        attribute.ParentNode = Parent;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, attribute));
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public string GetNamespacePrefixIfDefined(string namespaceURI)
    {
        if (namespaceURI is null)
            throw new ArgumentNullException(nameof(namespaceURI));

        foreach (var att in _attributes)
        {
            if ((string.Equals(att.Name, HtmlNode.XmlnsPrefix, StringComparison.Ordinal) ||
                string.Equals(att.Prefix, HtmlNode.XmlnsPrefix, StringComparison.Ordinal)) &&
                string.Equals(att.Value, namespaceURI, StringComparison.Ordinal))
            {
                return att.LocalName;
            }
        }

        return null;
    }

    public void RemoveAll()
    {
        foreach (var att in _attributes)
        {
            if (att.ParentNode != Parent)
                throw new InvalidOperationException();

            att.ParentNode = null;
        }

        _attributes.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void Insert(int index, HtmlAttribute item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        if (item.ParentNode is not null)
            throw new ArgumentException(message: null, nameof(item));

        _attributes.Insert(index, item);
        item.ParentNode = Parent;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    public bool Contains(HtmlAttribute item)
    {
        return IndexOf(item) >= 0;
    }

    public void CopyTo(HtmlAttribute[] array, int arrayIndex)
    {
        _attributes.CopyTo(array, arrayIndex);
    }

    public int IndexOf(HtmlAttribute item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        return _attributes.IndexOf(item);
    }

    public int IndexOf(string name)
    {
        if (name is null)
            return -1;

        return _attributes.FindIndex(a => name.EqualsIgnoreCase(a.Name));
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public int IndexOf(string localName, string namespaceURI)
    {
        if (localName is null || namespaceURI is null)
            return -1;

        return _attributes.FindIndex(a =>
            localName.EqualsIgnoreCase(a.LocalName) &&
            a.NamespaceURI is not null && string.Equals(namespaceURI, a.NamespaceURI, StringComparison.Ordinal));
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _attributes.Count)
            return false;

        var att = _attributes[index];
        if (att.ParentNode != Parent)
            throw new ArgumentException(message: null, nameof(index));

        _attributes.RemoveAt(index);
        att.ParentNode = null;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, att, index));
        return true;
    }

    public void RemoveRange(IEnumerable<HtmlAttribute> attributes)
    {
        if (attributes is null)
            return;

        foreach (var att in attributes)
        {
            Remove(att);
        }
    }

    public bool RemoveByPrefix(string prefix, string localName)
    {
        if (prefix is null)
            throw new ArgumentNullException(nameof(prefix));
        if (localName is null)
            throw new ArgumentNullException(nameof(localName));

        var att = _attributes.Find(a => localName.EqualsIgnoreCase(a.LocalName) && string.Equals(prefix, a.Prefix, StringComparison.Ordinal));
        if (att is null)
            return false;

        return Remove(att);
    }

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public bool Remove(string localName, string namespaceURI)
    {
        if (localName is null)
            throw new ArgumentNullException(nameof(localName));

        if (namespaceURI is null)
            throw new ArgumentNullException(nameof(namespaceURI));

        var att = this[localName, namespaceURI];
        if (att is null)
            return false;

        return Remove(att);
    }

    public bool Remove(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        var att = this[name];
        if (att is null)
            return false;

        return Remove(att);
    }

    public bool Remove(HtmlAttribute item)
    {
        if (item is null)
            throw new ArgumentNullException(nameof(item));

        if (item.ParentNode != Parent)
            throw new ArgumentException(message: null, nameof(item));

        if (!_attributes.Remove(item))
            throw new ArgumentException(message: null, nameof(item));

        item.ParentNode = null;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
        return true;
    }

    public HtmlAttribute this[string name]
    {
        get
        {
            if (name is null)
                throw new ArgumentNullException(nameof(name));

            return _attributes.Find(a => name.EqualsIgnoreCase(a.Name));
        }
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            if (value.ParentNode is not null)
                throw new ArgumentException(message: null, nameof(value));

            var index = IndexOf(name);
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

    [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Breaking change")]
    public HtmlAttribute this[string localName, string namespaceURI]
    {
        get
        {
            if (localName is null)
                throw new ArgumentNullException(nameof(localName));

            if (namespaceURI is null)
                throw new ArgumentNullException(nameof(namespaceURI));

            return _attributes.Find(a =>
                localName.EqualsIgnoreCase(a.LocalName) &&
                a.NamespaceURI is not null && string.Equals(namespaceURI, a.NamespaceURI, StringComparison.Ordinal));
        }
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            if (value.ParentNode is not null)
                throw new ArgumentException(message: null, nameof(value));

            var index = IndexOf(localName, namespaceURI);
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
        get => _attributes[index];
        set
        {
            if (value == _attributes[index])
                return;

            var oldItem = _attributes[index];
            _attributes[index] = value;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, oldItem));
        }
    }

    public int Count => _attributes.Count;

    public IEnumerator<HtmlAttribute> GetEnumerator() => _attributes.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection<HtmlAttribute>.IsReadOnly => false;

    void ICollection<HtmlAttribute>.Clear() => RemoveAll();

    int IList.Add(object value)
    {
        var count = Count;
        Add((HtmlAttribute)value);
        return count;
    }

    void IList.Clear() => RemoveAll();

    bool IList.Contains(object value) => Contains((HtmlAttribute)value);

    int IList.IndexOf(object value) => IndexOf((HtmlAttribute)value);

    void IList.Insert(int index, object value) => Insert(index, (HtmlAttribute)value);

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    void IList.Remove(object value) => Remove((HtmlAttribute)value);

    void IList.RemoveAt(int index) => RemoveAt(index);

    void IList<HtmlAttribute>.RemoveAt(int index) => RemoveAt(index);

    object IList.this[int index]
    {
        get => this[index];
        set
        {
            if (value is null)
                throw new ArgumentNullException(nameof(value));

            this[index] = (HtmlAttribute)value;
        }
    }

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_attributes).CopyTo(array, index);

    bool ICollection.IsSynchronized => ((ICollection)_attributes).IsSynchronized;

    object ICollection.SyncRoot => ((ICollection)_attributes).SyncRoot;
}
