#nullable disable
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.Html;

public abstract class HtmlNode : INotifyPropertyChanged, IXPathNavigable, IXmlNamespaceResolver
{
    private const int MaxRecursion = 300;

    public const string XmlnsPrefix = "xmlns";
    public const string XmlnsNamespaceURI = "http://www.w3.org/2000/xmlns/";

    public const string XmlPrefix = "xml";
    public const string XmlNamespaceURI = "http://www.w3.org/XML/1998/namespace";

    public const string XhtmlPrefix = "xhtml";
    public const string XhtmlNamespaceURI = "http://www.w3.org/1999/xhtml";
    private Collection<HtmlError> _errors;
    private HtmlNodeList _childNodes;
    private HtmlAttributeList _attributes;
    private HtmlNode _parentNode;
    private HtmlDocument _ownerDocument;
    private string _prefix;
    private string _localName;
    private object _tag;

    // caches
    private string _innerText;
    private string _outerHtml;
    private string _innerHtml;
    private string _outerXml;
    private string _innerXml;

    public event PropertyChangedEventHandler PropertyChanged;

    static HtmlNode()
    {
        NamespaceManager = new XmlNamespaceManager(new NameTable());
        NamespaceManager.AddNamespace(XmlPrefix, XmlNamespaceURI);
        NamespaceManager.AddNamespace(XhtmlPrefix, XhtmlNamespaceURI);
    }

    protected HtmlNode(string prefix!!, string localName!!, string namespaceURI, HtmlDocument ownerDocument)
    {
        if (ownerDocument == null && this is not HtmlDocument)
            throw new ArgumentNullException(nameof(ownerDocument));

        _prefix = prefix;
        _localName = localName;
        DeclaredNamespaceURI = namespaceURI;
        OwnerDocument = ownerDocument;
    }

    public static XmlNamespaceManager NamespaceManager { get; }

    public override string ToString()
    {
        return Name;
    }

    internal static string GetValidXmlName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        return Utilities.GetValidXmlName(name);
    }

    protected internal virtual void ClearCaches()
    {
        ClearCaches(0);
    }

    private void ClearCaches(int index)
    {
        // deep recursion testing. incurred because of xslt in general
        if (index > MaxRecursion)
            throw new HtmlException($"HTML0005: Maximum recursion depth ({MaxRecursion.ToString(CultureInfo.InvariantCulture)}) exceeded. This may be caused by a recursive XSLT.");

        _innerHtml = null;
        _innerText = null;
        _innerXml = null;
        _outerHtml = null;
        _outerXml = null;

        _parentNode?.ClearCaches(index + 1);
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        if (!RaisePropertyChanged)
            return;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (!RaisePropertyChanged)
            return;

        PropertyChanged?.Invoke(this, e);
    }

    protected string DeclaredNamespaceURI { get; private set; }

    public virtual int ParentIndex
    {
        get
        {
            if (ParentNode?.HasChildNodes == true)
            {
                for (var i = 0; i < ParentNode.ChildNodes.Count; i++)
                {
                    if (ParentNode.ChildNodes[i] == this)
                        return i;
                }
            }
            return -1;
        }
    }

    public virtual IEnumerable<HtmlNode> NextSiblings
    {
        get
        {
            if (ParentNode == null || !ParentNode.HasChildNodes)
                yield break;

            var index = ParentIndex;
            if (index < 0 || (index + 1) >= ParentNode.ChildNodes.Count)
                yield break;

            foreach (var node in ParentNode.ChildNodes.Skip(index + 1))
            {
                yield return node;
            }
        }
    }

    public virtual HtmlNode NextSibling
    {
        get
        {
            if (ParentNode == null || !ParentNode.HasChildNodes)
                return null;

            var index = ParentIndex;
            if (index < 0 || (index + 1) >= ParentNode.ChildNodes.Count)
                return null;

            return ParentNode.ChildNodes[index + 1];
        }
    }

    public virtual IEnumerable<HtmlNode> PreviousSiblings
    {
        get
        {
            if (ParentNode == null || !ParentNode.HasChildNodes)
                yield break;

            var index = ParentIndex;
            if (index <= 0)
                yield break;

            foreach (var node in ParentNode.ChildNodes.Take(index))
            {
                yield return node;
            }
        }
    }

    public virtual HtmlNode PreviousSibling
    {
        get
        {
            if (ParentNode == null || !ParentNode.HasChildNodes)
                return null;

            var index = ParentIndex;
            if (index <= 0)
                return null;

            return ParentNode.ChildNodes[index - 1];
        }
    }

    public virtual HtmlNodeList ChildNodes => _childNodes ??= new HtmlNodeList(this);

    public virtual HtmlAttributeList Attributes => _attributes ??= new HtmlAttributeList(this);

    public virtual bool RaisePropertyChanged { get; set; }
    public virtual int StreamOrder { get; set; }
    public abstract HtmlNodeType NodeType { get; }

    public int Depth => ParentNode != null ? ParentNode.Depth + 1 : 0;

    public string Prefix
    {
        get => _prefix;
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (string.Equals(_prefix, value, StringComparison.Ordinal))
                return;

            ClearCaches();
            _prefix = value;
            OnPropertyChanged();
        }
    }

    protected virtual void ParseName(string name, out string prefix, out string localName)
    {
        if (name == null)
        {
            prefix = null;
            localName = null;
            return;
        }

        var pos = name.IndexOf(':', StringComparison.Ordinal);
        if (pos < 0)
        {
            prefix = string.Empty;
            localName = name;
            return;
        }

        prefix = Utilities.Nullify(name[..pos], trim: true);
        localName = Utilities.Nullify(name[(pos + 1)..], trim: true);
        if (prefix == null || localName == null)
        {
            prefix = string.Empty;
            localName = name;
        }
    }

    public virtual void ResetStreamOrder(int newOrder)
    {
        StreamOrder = newOrder;
        if (HasAttributes)
        {
            foreach (var att in Attributes)
            {
                att.ResetStreamOrder(newOrder);
            }
        }

        if (HasChildNodes)
        {
            foreach (var node in ChildNodes)
            {
                node.ResetStreamOrder(newOrder);
            }
        }
    }

    private void SetName(string name)
    {
        if (string.Equals(name, Name, StringComparison.Ordinal))
            return;

        ClearCaches();
        ParseName(name, out var prefix, out var localName);
        Prefix = prefix;
        LocalName = localName;
        OnPropertyChanged(nameof(Name));
    }

    [SuppressMessage("Style", "IDE0016:Use 'throw' expression", Justification = "Change the behavior of the method")]
    public string LocalName
    {
        get => _localName;
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            ClearCaches();
            _localName = value;
            OnPropertyChanged();
        }
    }

    public HtmlDocument OwnerDocument
    {
        get
        {
            if (NodeType == HtmlNodeType.Document)
                return (HtmlDocument)this;

            return _ownerDocument;
        }
        private set => _ownerDocument = value;
    }

    public virtual string NamespaceURI
    {
        get => GetNamespaceOfPrefix(Prefix);
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (!string.Equals(DeclaredNamespaceURI, value, StringComparison.Ordinal))
            {
                DeclaredNamespaceURI = value;
                OnPropertyChanged();
            }
        }
    }

    public virtual object Tag
    {
        get => _tag;
        set
        {
            if (Equals(_tag, value))
                return;

            _tag = value;
            OnPropertyChanged();
        }
    }

    public virtual string Value
    {
        get => null;
        set
        {
            //throw new InvalidOperationException();
        }
    }

    public virtual string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Prefix))
                return Prefix + ":" + LocalName;

            return LocalName;
        }
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            SetName(value);
        }
    }

    internal static bool IsHtmlNs(string ns)
    {
        return string.IsNullOrWhiteSpace(ns) || string.Equals(ns, XhtmlNamespaceURI, StringComparison.Ordinal);
    }

    public HtmlNode ParentNode
    {
        get => _parentNode;
        internal set
        {
            if (_parentNode != value)
            {
                if (value == null)
                {
                    // when detached, copy namespaces if it was computed
                    var nss = _parentNode.GetAllNamespaces();
                    foreach (var ns in nss)
                    {
                        if (IsHtmlNs(ns.Value))
                            continue;

                        Attributes.Add(XmlnsPrefix, ns.Key, string.Empty, ns.Value);
                    }
                }
            }

            _parentNode = value;
            _ownerDocument = _parentNode?.OwnerDocument;
        }
    }

    public HtmlElement ParentElement
    {
        get
        {
            for (var node = ParentNode; node != null; node = node.ParentNode)
            {
                if (node is HtmlElement parentElement)
                    return parentElement;
            }

            return null;
        }
    }

    public string Id => GetAttributeValue("id");

    protected internal virtual void AddError(HtmlError error!!)
    {
        _errors ??= new Collection<HtmlError>();
        _errors.Add(error);
    }

    protected internal virtual void ClearErrors()
    {
        if (_errors == null)
            return;

        _errors.Clear();
        _errors = null;
    }

    public virtual IEnumerable<HtmlError> Errors => _errors ??= new Collection<HtmlError>();

    public virtual string OuterHtml
    {
        get
        {
            if (_outerHtml == null)
            {
                using var w = new StringWriter(CultureInfo.InvariantCulture);
                WriteTo(w);
                _outerHtml = w.ToString();
            }
            return _outerHtml;
        }
    }

    public virtual string InnerText
    {
        get
        {
            if (_innerText == null)
            {
                var sb = new StringBuilder();
                AppendChildText(sb);
                _innerText = sb.ToString();
            }
            return _innerText;
        }
        set
        {
            if (!string.Equals(value, _innerText, StringComparison.Ordinal))
            {
                ClearCaches();
                var firstChild = FirstChild;
                if (firstChild != null && firstChild.NextSibling == null && firstChild.NodeType == HtmlNodeType.Text)
                {
                    firstChild.Value = value;
                }
                else
                {
                    if (OwnerDocument == null)
                        throw new ArgumentException("The node is not owned by a document", nameof(value));

                    RemoveAll();
                    var text = OwnerDocument.CreateText();
                    text.Value = value;
                    ChildNodes.Add(text);
                }
                OnPropertyChanged();
            }
        }
    }

    private void AppendChildText(StringBuilder builder)
    {
        if (HasChildNodes)
        {
            foreach (var node in ChildNodes)
            {
                if (node.NodeType == HtmlNodeType.Text)
                {
                    builder.Append(node.InnerText);
                }
                else
                {
                    node.AppendChildText(builder);
                }
            }
        }
    }

    public virtual string InnerHtml
    {
        get
        {
            if (_innerHtml == null)
            {
                using var w = new StringWriter(CultureInfo.InvariantCulture);
                WriteContentTo(w);
                _innerHtml = w.ToString();
            }
            return _innerHtml;
        }
        set => throw new InvalidOperationException();
    }

    public virtual string OuterXml
    {
        get
        {
            if (_outerXml == null)
            {
                using var w = new StringWriter(CultureInfo.InvariantCulture);
                using (var writer = XmlWriter.Create(w))
                {
                    WriteTo(writer);
                }
                _outerXml = w.ToString();
            }
            return _outerXml;
        }
    }

    public virtual string InnerXml
    {
        get
        {
            if (_innerXml == null)
            {
                using var w = new StringWriter(CultureInfo.InvariantCulture);
                using (var writer = XmlWriter.Create(w))
                {
                    WriteContentTo(writer);
                }
                _innerXml = w.ToString();
            }
            return _innerXml;
        }
        set => throw new InvalidOperationException();
    }

    public HtmlNode FirstChild
    {
        get
        {
            if (HasChildNodes)
                return ChildNodes[0];

            return null;
        }
    }

    public HtmlNode LastChild
    {
        get
        {
            if (HasChildNodes)
                return ChildNodes[^1];

            return null;
        }
    }

    public HtmlAttribute SetAttribute(string localName, string namespaceURI, string value)
    {
        return SetAttribute(string.Empty, localName, namespaceURI, value);
    }

    public HtmlAttribute SetAttribute(string prefix!!, string localName!!, string namespaceURI!!, string value)
    {
        var att = Attributes[localName, namespaceURI];
        if (att == null)
        {
            att = Attributes.Add(prefix, localName, namespaceURI, value);
        }
        else
        {
            att.Value = value;
        }
        return att;
    }

    public bool RemoveAttribute(string name!!)
    {
        if (_attributes == null)
            return false;

        return Attributes.Remove(name);
    }

    public bool RemoveAttribute(string localName!!, string namespaceURI!!)
    {
        if (_attributes == null)
            return false;

        return Attributes.Remove(localName, namespaceURI);
    }

    public bool RemoveAttributeByPrefix(string prefix!!, string localName!!)
    {
        if (_attributes == null)
            return false;

        return Attributes.RemoveByPrefix(prefix, localName);
    }

    public HtmlAttribute SetAttribute(string name!!, string value)
    {
        var att = Attributes[name];
        if (att == null)
        {
            att = Attributes.Add(name, value);
        }
        else
        {
            att.Value = value;
        }
        return att;
    }

    public virtual bool HasAttributes => _attributes?.Count > 0;

    public virtual bool HasChildNodes => _childNodes?.Count > 0;

    public bool HasAttribute(string localName!!, string namespaceURI!!)
    {
        if (_attributes == null)
            return false;

        return Attributes[localName, namespaceURI] != null;
    }

    public bool HasNonNullNorWhitespaceAttribute(string name!!)
    {
        return GetNullifiedAttributeValue(name) != null;
    }

    public bool HasAttribute(string name!!)
    {
        if (_attributes == null)
            return false;

        return Attributes[name] != null;
    }

    public string GetAttributeValue(string name!!)
    {
        if (_attributes == null)
            return null;

        var att = Attributes[name];
        if (att == null)
            return null;

        return att.Value;
    }

    public string GetAttributeValue(string name!!, string defaultValue)
    {
        if (_attributes == null)
            return defaultValue;

        var att = Attributes[name];
        if (att == null)
            return defaultValue;

        return att.Value;
    }

    public string GetAttributeValueByPrefix(string prefix!!, string localName!!, string defaultValue)
    {
        if (_attributes == null)
            return defaultValue;

        var att = Attributes.FirstOrDefault(a => string.Equals(a.Prefix, prefix, StringComparison.Ordinal) && a.LocalName.EqualsIgnoreCase(localName));
        if (att == null)
            return defaultValue;

        return att.Value;
    }

    public string GetNullifiedAttributeValue(string name!!)
    {
        if (_attributes == null)
            return null;

        var att = Attributes[name];
        if (att == null)
            return null;

        return Utilities.Nullify(att.Value, trim: true);
    }

    public string GetNullifiedAttributeValue(string localName!!, string namespaceURI!!)
    {
        if (_attributes == null)
            return null;

        var att = Attributes[localName, namespaceURI];
        if (att == null)
            return null;

        return Utilities.Nullify(att.Value, trim: true);
    }

    public string GetAttributeValue(string localName!!, string namespaceURI!!, string defaultValue)
    {
        if (_attributes == null)
            return defaultValue;

        var att = Attributes[localName, namespaceURI];
        if (att == null)
            return defaultValue;

        return att.Value;
    }

    public virtual void AppendChild(HtmlNode newChild)
    {
        if (newChild is HtmlAttribute)
            throw new ArgumentException("Cannot append an attribute", nameof(newChild));

        ChildNodes.Add(newChild);
    }

    public virtual void InsertAfter(HtmlNode newChild!!, HtmlNode refChild)
    {
        if (newChild is HtmlAttribute)
            throw new ArgumentException("Cannot insert an attribute", nameof(newChild));

        if (this == newChild || IsAncestor(newChild))
            throw new ArgumentException(message: null, nameof(newChild));

        if (newChild.NodeType == HtmlNodeType.Document)
            throw new ArgumentException(message: null, nameof(newChild));

        if (OwnerDocument == null)
            throw new InvalidOperationException();

        if (refChild == null)
        {
            PrependChild(newChild);
            return;
        }

        if (newChild == refChild)
            return;

        var index = -1;
        if (HasChildNodes)
        {
            for (var i = 0; i < ChildNodes.Count; i++)
            {
                var node = ChildNodes[i];
                if (node == refChild)
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
            throw new ArgumentException(message: null, nameof(refChild));

        ChildNodes.Insert(index + 1, newChild);
    }

    public bool IsAncestor(HtmlNode node!!)
    {
        for (var n = ParentNode; (n != null) && (n != this); n = n.ParentNode)
        {
            if (n == node)
                return true;
        }
        return false;
    }

    public virtual void InsertBefore(HtmlNode newChild!!, HtmlNode refChild)
    {
        if (newChild is HtmlAttribute)
            throw new ArgumentException(message: null, nameof(newChild));

        if (this == newChild || IsAncestor(newChild))
            throw new ArgumentException(message: null, nameof(newChild));

        if (newChild.NodeType == HtmlNodeType.Document)
            throw new ArgumentException(message: null, nameof(newChild));

        if (refChild == null)
        {
            AppendChild(newChild);
            return;
        }

        if (newChild == refChild)
            return;

        var index = -1;
        if (HasChildNodes)
        {
            for (var i = 0; i < ChildNodes.Count; i++)
            {
                var node = ChildNodes[i];
                if (node == refChild)
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
            throw new ArgumentException(message: null, nameof(refChild));

        ChildNodes.Insert(index, newChild);
    }

    public virtual void PrependChild(HtmlNode newChild)
    {
        if (newChild is HtmlAttribute)
            throw new ArgumentException(message: null, nameof(newChild));

        ChildNodes.Insert(0, newChild);
    }

    public bool Remove()
    {
        return Remove(keepChildren: false);
    }

    public virtual bool Remove(bool keepChildren)
    {
        if (ParentNode != null)
            return ParentNode.RemoveChild(this, keepChildren);

        return false;
    }

    public virtual void RemoveAll()
    {
        if (_childNodes == null)
            return;

        ChildNodes.RemoveAll();
    }

    public bool RemoveChild(HtmlNode oldChild)
    {
        return RemoveChild(oldChild, keepGrandChildren: false);
    }

    public virtual bool RemoveChild(HtmlNode oldChild, bool keepGrandChildren)
    {
        if (oldChild is HtmlAttribute att)
        {
            if (string.IsNullOrWhiteSpace(att.NamespaceURI))
                return RemoveAttribute(att.Name);

            return RemoveAttribute(att.LocalName, att.NamespaceURI);
        }

        if (_childNodes == null)
            return false;

        var index = ChildNodes.IndexOf(oldChild);
        if (index < 0)
            return false;

        if (keepGrandChildren)
        {
            var oldIndex = oldChild.ParentIndex;
            foreach (var child in oldChild.ChildNodes)
            {
                child._parentNode = null;
                ChildNodes.Insert(oldIndex++, child);
            }
            oldChild.ChildNodes.RemoveAllNoCheck();
        }

        return ChildNodes.Remove(oldChild);
    }

    public virtual void ReplaceChild(HtmlNode newChild, HtmlNode oldChild)
    {
        if (newChild is HtmlAttribute)
            throw new ArgumentException(message: "newChild must not be an HtmlAttribute", nameof(newChild));

        if (oldChild is HtmlAttribute)
            throw new ArgumentException(message: "oldChild must not be an HtmlAttribute", nameof(oldChild));

        ChildNodes.Replace(newChild, oldChild);
    }

    public virtual string GetNamespaceOfPrefix(string prefix!!)
    {
        if (prefix.EqualsIgnoreCase(Prefix) && DeclaredNamespaceURI != null)
            return DeclaredNamespaceURI;

        foreach (var att in Attributes)
        {
            if (att.Prefix.EqualsIgnoreCase(XmlnsPrefix) && att.LocalName.EqualsIgnoreCase(prefix))
                return att.Value;
        }

        if (ParentNode != null && ParentNode != this)
            return ParentNode.GetNamespaceOfPrefix(prefix);

        if (OwnerDocument != null && OwnerDocument != this)
            return OwnerDocument.GetNamespaceOfPrefix(prefix);

        return string.Empty;
    }

    public virtual string GetPrefixOfNamespace(string namespaceURI!!)
    {
        if (namespaceURI.EqualsIgnoreCase(NamespaceURI))
            return Prefix;

        foreach (var att in Attributes)
        {
            if (att.Prefix.EqualsIgnoreCase(XmlnsPrefix) && att.Value.EqualsIgnoreCase(namespaceURI))
                return att.LocalName;
        }

        if (ParentNode != null && ParentNode != this)
            return ParentNode.GetPrefixOfNamespace(namespaceURI);

        if (OwnerDocument != null && OwnerDocument != this)
            return OwnerDocument.GetPrefixOfNamespace(namespaceURI);

        return string.Empty;
    }

    public virtual HtmlNode GetParent(Func<HtmlNode, bool> func!!)
    {
        if (ParentNode == null)
            return null;

        if (func(ParentNode))
            return ParentNode;

        return ParentNode.GetParent(func);
    }

    public IReadOnlyDictionary<string, string> GetAllNamespaces()
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        GetNamespaceAttributes(namespaces);
        return namespaces;
    }

    protected virtual void GetNamespaceAttributes(IDictionary<string, string> namespaces!!)
    {
        foreach (var att in Attributes)
        {
            if (att.Prefix.EqualsIgnoreCase(XmlnsPrefix))
            {
                namespaces[att.LocalName] = att.Value;
            }
        }

        ParentNode?.GetNamespaceAttributes(namespaces);
    }

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "By design")]
    public Uri BaseAddress => null;

    public abstract void WriteTo(TextWriter writer);
    public abstract void WriteContentTo(TextWriter writer);
    public abstract void WriteTo(XmlWriter writer);
    public abstract void WriteContentTo(XmlWriter writer);

    public HtmlNode Clone()
    {
        return Clone(HtmlCloneOptions.All);
    }

    public HtmlNode Clone(HtmlCloneOptions options)
    {
        var clone = CreateNew();
        CopyTo(clone, options);
        return clone;
    }

    public virtual void CopyTo(HtmlNode target!!, HtmlCloneOptions options)
    {
        target.Value = Value;

        if ((options & HtmlCloneOptions.StreamOrder) == HtmlCloneOptions.StreamOrder)
        {
            target.StreamOrder = StreamOrder;
        }

        if (!IsHtmlNs(DeclaredNamespaceURI))
        {
            target.DeclaredNamespaceURI = DeclaredNamespaceURI;
        }
        else
        {
            var ns = NamespaceURI;
            if (!IsHtmlNs(ns))
            {
                target.DeclaredNamespaceURI = ns;
            }
        }

        if ((options & HtmlCloneOptions.Attributes) == HtmlCloneOptions.Attributes)
        {
            foreach (var att in Attributes)
            {
#if DEBUG_HTML_ID
                if (att.Name == HtmlElement.DebugIdAttributeName)
                    continue;
#endif
                var cloneAtt = (HtmlAttribute)att.Clone(options);

                if ((options & HtmlCloneOptions.OverwriteAttributes) == HtmlCloneOptions.OverwriteAttributes)
                {
                    target.Attributes[cloneAtt.Name] = cloneAtt;
                }
                else
                {
                    target.Attributes.Add(cloneAtt);
                }
            }
        }

        if ((options & HtmlCloneOptions.Deep) == HtmlCloneOptions.Deep)
        {
            foreach (var node in ChildNodes)
            {
                var cloneNode = node.Clone(options);
                target.AppendChild(cloneNode);
            }
        }

        if ((options & HtmlCloneOptions.Tag) == HtmlCloneOptions.Tag)
        {
            target._tag = _tag;
        }
    }

    // NOTE: the doc must have been loaded as an HtmlXPathDocument for this to be valid
    public virtual string XPathExpression => null;

    [Conditional("DEBUG")]
    internal virtual void CheckParenting()
    {
        if (HasAttributes)
        {
            foreach (var att in Attributes)
            {
                if (att.ParentNode != this)
                    throw new HtmlException("Internal error: node parenting is wrong. Attribute: " + att.Name);
            }
        }

        if (HasChildNodes)
        {
            foreach (var node in ChildNodes)
            {
                if (node.ParentNode != this)
                    throw new HtmlException("Internal error: node parenting is wrong. Node: " + node.Clone(HtmlCloneOptions.Attributes).OuterHtml);

                node.CheckParenting();
            }
        }
    }

    public virtual HtmlNode CreateNew()
    {
        if (OwnerDocument == null)
            throw new InvalidOperationException();

        switch (NodeType)
        {
            case HtmlNodeType.Attribute:
                return OwnerDocument.CreateAttribute(Prefix, LocalName, NamespaceURI);

            case HtmlNodeType.Comment:
                return OwnerDocument.CreateComment();

            case HtmlNodeType.Document:
                return OwnerDocument.CreateDocument();

            case HtmlNodeType.Element:
            case HtmlNodeType.ProcessingInstruction:
            case HtmlNodeType.DocumentType:
                return OwnerDocument.CreateElement(Prefix, LocalName, NamespaceURI);

            case HtmlNodeType.Text:
                return OwnerDocument.CreateText();

            case HtmlNodeType.XPathResult:
                var result = (HtmlXPathResult)this;
                return new HtmlXPathResult(OwnerDocument, result.Result);

            default:
                throw new NotSupportedException();
        }
    }

    protected virtual void AddNamespacesInScope(XmlNamespaceScope scope, IDictionary<string, string> dictionary!!)
    {
        if (!string.IsNullOrWhiteSpace(NamespaceURI))
        {
            if (Prefix != null && (scope != XmlNamespaceScope.ExcludeXml || !string.Equals(NamespaceURI, XmlnsNamespaceURI, StringComparison.Ordinal)))
            {
                dictionary[Prefix] = NamespaceURI;
            }
        }

        if (ParentNode != null && scope != XmlNamespaceScope.Local)
        {
            ParentNode.AddNamespacesInScope(scope, dictionary);
        }
    }

    public virtual IXmlNamespaceResolver ParentNamespaceResolver
    {
        get
        {
            if (OwnerDocument != null)
                return OwnerDocument;

            if (ParentNode != null)
                return ParentNode.ParentNamespaceResolver;

            return this;
        }
    }

    public virtual IDictionary<string, string> GetNamespacesInScope(XmlNamespaceScope scope)
    {
        var dic = new Dictionary<string, string>(StringComparer.Ordinal);
        AddNamespacesInScope(scope, dic);
        return dic;
    }

    string IXmlNamespaceResolver.LookupNamespace(string prefix)
    {
        return GetNamespaceOfPrefix(prefix);
    }

    string IXmlNamespaceResolver.LookupPrefix(string namespaceName)
    {
        return GetPrefixOfNamespace(namespaceName);
    }

    public XmlNode ImportAsXml(XmlDocument owner)
    {
        return ImportAsXml(owner, deep: true);
    }

    public virtual XmlNode ImportAsXml(XmlDocument owner!!, bool deep)
    {
        using var s = new StringWriter();
        using var writer = XmlWriter.Create(s);
        WriteTo(writer);
        var nodeDoc = new XmlDocument();
        using var txtReader = new StringReader(s.ToString());
        nodeDoc.Load(txtReader);
        return owner.ImportNode(nodeDoc.DocumentElement, deep);
    }

    public HtmlNode SelectSingleNode(string xpath)
    {
        return SelectSingleNode(xpath, nsmgr: null);
    }

    public HtmlNode SelectSingleNode(string xpath, XmlNamespaceManager nsmgr)
    {
        return SelectSingleNode(xpath, nsmgr, HtmlNodeNavigatorOptions.None);
    }

    public HtmlNode SelectSingleNode(string xpath, HtmlNodeNavigatorOptions options)
    {
        return SelectSingleNode(xpath, nsmgr: null, options);
    }

    public HtmlNode SelectSingleNode(string xpath, XmlNamespaceManager nsmgr, HtmlNodeNavigatorOptions options)
    {
        return SelectNodes(xpath, nsmgr, options).FirstOrDefault();
    }

    public IEnumerable<HtmlNode> SelectNodes(string xpath)
    {
        return SelectNodes(xpath, nsmgr: null);
    }

    public IEnumerable<HtmlNode> SelectNodes(string xpath, XmlNamespaceManager nsmgr)
    {
        return SelectNodes(xpath, nsmgr, HtmlNodeNavigatorOptions.None);
    }

    public IEnumerable<HtmlNode> SelectNodes(string xpath, HtmlNodeNavigatorOptions options)
    {
        return SelectNodes(xpath, namespaceManager: null, options);
    }

    public virtual IXPathNavigable CreateNavigable(HtmlNodeNavigatorOptions options)
    {
        return new Navigable(OwnerDocument, this, options);
    }

    public XPathNavigator CreateNavigator()
    {
        return CreateNavigator(HtmlNodeNavigatorOptions.None);
    }

    public virtual XPathNavigator CreateNavigator(HtmlNodeNavigatorOptions options)
    {
        return new HtmlNodeNavigator(OwnerDocument, this, options);
    }

    public virtual IEnumerable<HtmlNode> SelectNodes(string xpath!!, XmlNamespaceManager namespaceManager, HtmlNodeNavigatorOptions options)
    {
        if ((options & HtmlNodeNavigatorOptions.Dynamic) == HtmlNodeNavigatorOptions.Dynamic)
            return DoSelectNodes(xpath, namespaceManager, options);

        var list = DoSelectNodes(xpath, namespaceManager, options).ToList();

        if ((options & HtmlNodeNavigatorOptions.DepthFirst) == HtmlNodeNavigatorOptions.DepthFirst)
        {
            list.Sort(new HtmlNodeDepthComparer { Direction = ListSortDirection.Descending });
        }
        return list;
    }

    protected virtual IEnumerable<HtmlNode> DoSelectNodes(string xpath, XmlNamespaceManager nsmgr, HtmlNodeNavigatorOptions options)
    {
        var navigator = CreateNavigator(options);
        if (navigator == null)
            yield break;

        var expr = navigator.Compile(xpath);
        if (nsmgr != null)
        {
            expr.SetContext(nsmgr);
        }

        var eval = navigator.Evaluate(expr);
        if (eval is XPathNodeIterator it)
        {
            while (it.MoveNext())
            {
                var n = it.Current as HtmlNodeNavigator;
                if (n?.CurrentNode != null)
                    yield return n.CurrentNode;
            }
        }
        else
        {
            yield return new HtmlXPathResult(OwnerDocument, eval);
        }
    }

    private sealed class Navigable : IXPathNavigable
    {
        private readonly HtmlDocument _ownerDocument;
        private readonly HtmlNode _node;
        private readonly HtmlNodeNavigatorOptions _options;

        public Navigable(HtmlDocument ownerDocument, HtmlNode node, HtmlNodeNavigatorOptions options)
        {
            _ownerDocument = ownerDocument;
            _node = node;
            _options = options;
        }

        public XPathNavigator CreateNavigator()
        {
            return new HtmlNodeNavigator(_ownerDocument, _node, _options);
        }
    }
}
