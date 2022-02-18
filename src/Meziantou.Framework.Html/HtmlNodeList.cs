#nullable disable
using System.Collections;
using System.Collections.Specialized;

namespace Meziantou.Framework.Html;

public sealed class HtmlNodeList : IList<HtmlNode>, INotifyCollectionChanged, IList, IReadOnlyList<HtmlNode>
{
    private readonly List<HtmlNode> _list = new();
    private readonly HtmlNode _parent;

    public event NotifyCollectionChangedEventHandler CollectionChanged;

    internal HtmlNodeList(HtmlNode parent)
    {
        _parent = parent;
    }

    private void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        _parent.ClearCaches();
        CollectionChanged?.Invoke(this, e);
    }

    public HtmlNode this[string name]
    {
        get
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            return _list.Find(n => n.Name.EqualsIgnoreCase(name));
        }
    }

    public HtmlNode this[string localName, string namespaceURI]
    {
        get
        {
            if (localName == null)
                throw new ArgumentNullException(nameof(localName));

            if (namespaceURI == null)
                throw new ArgumentNullException(nameof(namespaceURI));

            return _list.Find(a =>
                localName.EqualsIgnoreCase(a.LocalName) &&
                a.NamespaceURI != null && string.Equals(namespaceURI, a.NamespaceURI, StringComparison.Ordinal));
        }
    }

    public HtmlNode this[int index]
    {
        get => _list[index];
        set
        {
            if (value == _list[index])
                return;

            var oldItem = _list[index];
            _list[index] = value;
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, value, oldItem));
        }
    }

    public void Replace(HtmlNode newChild!!, HtmlNode oldChild!!)
    {
        if (newChild.ParentNode != null)
            throw new ArgumentException(message: null, nameof(newChild));

        var index = _list.IndexOf(oldChild);
        if (index >= 0)
        {
            if (oldChild.ParentNode != _parent)
                throw new ArgumentException(message: null, nameof(oldChild));

            HtmlDocument.RemoveIntrinsicElement(oldChild.OwnerDocument, oldChild as HtmlElement);
            oldChild.ParentNode = null;
            _list.RemoveAt(index);
        }
        else
        {
            throw new ArgumentException(message: null, nameof(oldChild));
        }

        _list.Insert(index, newChild);
        newChild.ParentNode = _parent;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newChild, oldChild));
    }

    internal void RemoveAllNoCheck()
    {
        _list.Clear();
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void RemoveAll()
    {
        foreach (var node in _list)
        {
            HtmlDocument.RemoveIntrinsicElement(node.OwnerDocument, node as HtmlElement);
            if (node.ParentNode != _parent)
                throw new InvalidOperationException();

            node.ParentNode = null;
        }
        RemoveAllNoCheck();
    }

    public void Insert(int index, HtmlNode item!!)
    {
        if (item.ParentNode != null)
            throw new ArgumentException(message: null, nameof(item));

        HtmlDocument.RemoveIntrinsicElement(item.OwnerDocument, item as HtmlElement);
        _list.Insert(index, item);
        item.ParentNode = _parent;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, item, index));
    }

    public void AddRange(IEnumerable<HtmlNode> nodes)
    {
        if (nodes == null)
            return;

        foreach (var node in nodes)
        {
            Add(node);
        }
    }

    internal void AddNoCheck(HtmlNode node)
    {
        _list.Add(node);
        node.ParentNode = _parent;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, node));
    }

    public void Add(HtmlNode item!!)
    {
        if (item.ParentNode != null)
            throw new ArgumentException(message: null, nameof(item));

        AddNoCheck(item);
    }

    public bool RemoveAt(int index)
    {
        if (index < 0 || index >= _list.Count)
            return false;

        var node = _list[index];
        if (node.ParentNode != _parent)
            throw new ArgumentException(message: null, nameof(index));

        HtmlDocument.RemoveIntrinsicElement(node.OwnerDocument, node as HtmlElement);
        node.ParentNode = null;
        _list.RemoveAt(index);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, node, index));
        return true;
    }

    public bool Remove(HtmlNode item!!)
    {
        var index = _list.IndexOf(item);
        if (index < 0)
            return false;

        var existing = _list[index];
        if (existing.ParentNode != _parent)
            throw new ArgumentException(message: null, nameof(item));

        _list.RemoveAt(index);
        HtmlDocument.RemoveIntrinsicElement(item.OwnerDocument, item as HtmlElement);
        item.ParentNode = null;
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, item));
        return true;
    }

    public int Count => _list.Count;

    public int IndexOf(HtmlNode item) => _list.IndexOf(item);

    public bool Contains(HtmlNode item) => IndexOf(item) >= 0;

    public void CopyTo(HtmlNode[] array, int arrayIndex) => _list.CopyTo(array, arrayIndex);

    public IEnumerator<HtmlNode> GetEnumerator() => _list.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    bool ICollection<HtmlNode>.IsReadOnly => false;

    void ICollection<HtmlNode>.Clear() => RemoveAll();

    int IList.Add(object value)
    {
        var count = Count;
        Add((HtmlNode)value);
        return count;
    }

    void IList.Clear() => RemoveAll();

    bool IList.Contains(object value) => Contains((HtmlNode)value);

    int IList.IndexOf(object value) => IndexOf((HtmlNode)value);

    void IList.Insert(int index, object value) => Insert(index, (HtmlNode)value);

    bool IList.IsFixedSize => false;

    bool IList.IsReadOnly => false;

    void IList.Remove(object value) => Remove((HtmlNode)value);

    void IList.RemoveAt(int index) => RemoveAt(index);

    void IList<HtmlNode>.RemoveAt(int index) => RemoveAt(index);

    void ICollection.CopyTo(Array array, int index) => ((ICollection)_list).CopyTo(array, index);

    bool ICollection.IsSynchronized => ((ICollection)_list).IsSynchronized;

    object ICollection.SyncRoot => ((ICollection)_list).SyncRoot;

    object IList.this[int index]
    {
        get => this[index];
        set => this[index] = (HtmlNode)value;
    }
}
