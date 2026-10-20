using System.Runtime.CompilerServices;
using System.Xml;
using System.Xml.XPath;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.XPath;

/// <summary>
/// A read-only <see cref="XPathNavigator"/> over the DOM, mapping the DOM to the XPath 1.0 data model
/// (https://www.w3.org/TR/xpath-10/#data-model): documents and fragments are root nodes, adjacent Text and CDATASection
/// nodes form a single text node, document types are not represented, and xmlns attributes are namespace nodes.
/// </summary>
internal sealed class DomXPathNavigator : XPathNavigator
{
    private static readonly XmlNameTable SharedNameTable = new NameTable();

    // The current node; for attributes, the owner element and the index of the attribute
    private Node _node;
    private int _attributeIndex = -1;

    // Namespace nodes: the element and the list of the in-scope namespaces of the element, with the current index
    private List<(string Prefix, string Namespace)>? _namespaces;
    private int _namespaceIndex = -1;

    // The root of the tree of an attribute without owner element: the XPath engine moves from an attribute to its parent
    // without checking the result (the following axis loops on an attribute that has no parent)
    private bool _detachedAttributeRoot;

    public DomXPathNavigator(Node node)
    {
        if (node is Attr { OwnerElement: { } owner } attr)
        {
            _node = owner;
            _attributeIndex = owner.IndexOfAttributeNode(attr);
        }
        else if (node is Text text)
        {
            _node = GetFirstOfTextRun(text);
        }
        else
        {
            _node = node;
        }
    }

    private DomXPathNavigator(DomXPathNavigator other)
    {
        _node = other._node;
        _attributeIndex = other._attributeIndex;
        _namespaces = other._namespaces;
        _namespaceIndex = other._namespaceIndex;
        _detachedAttributeRoot = other._detachedAttributeRoot;
    }

    /// <summary>The DOM node of the current position (the Attr node for attributes, the first Text node of a text run), or null for namespace nodes.</summary>
    public Node? CurrentNode
    {
        get
        {
            if (_namespaceIndex >= 0 || _detachedAttributeRoot)
                return null;

            if (_attributeIndex >= 0)
                return ((Element)_node).GetAttributeNodeAt(_attributeIndex);

            return _node;
        }
    }

    public override object? UnderlyingObject => CurrentNode;

    public override XmlNameTable NameTable => SharedNameTable;

    public override XPathNavigator Clone() => new DomXPathNavigator(this);

    public override bool IsSamePosition(XPathNavigator other) =>
        other is DomXPathNavigator navigator && ReferenceEquals(navigator._node, _node) && navigator._attributeIndex == _attributeIndex && navigator._namespaceIndex == _namespaceIndex && navigator._detachedAttributeRoot == _detachedAttributeRoot;

    public override bool MoveTo(XPathNavigator other)
    {
        if (other is not DomXPathNavigator navigator)
            return false;

        _node = navigator._node;
        _attributeIndex = navigator._attributeIndex;
        _namespaces = navigator._namespaces;
        _namespaceIndex = navigator._namespaceIndex;
        _detachedAttributeRoot = navigator._detachedAttributeRoot;
        return true;
    }

    private bool IsOnAttribute => _attributeIndex >= 0;

    private bool IsOnNamespace => _namespaceIndex >= 0;

    public override XPathNodeType NodeType
    {
        get
        {
            if (IsOnNamespace)
                return XPathNodeType.Namespace;

            if (IsOnAttribute)
                return XPathNodeType.Attribute;

            if (_detachedAttributeRoot)
                return XPathNodeType.Root;

            return _node switch
            {
                Document or DocumentFragment => XPathNodeType.Root,
                Element => XPathNodeType.Element,
                Text => XPathNodeType.Text,
                Comment => XPathNodeType.Comment,
                ProcessingInstruction => XPathNodeType.ProcessingInstruction,
                Attr => XPathNodeType.Attribute, // An attribute without owner element
                _ => XPathNodeType.All,
            };
        }
    }

    public override string LocalName
    {
        get
        {
            if (IsOnNamespace)
                return _namespaces![_namespaceIndex].Prefix;

            if (IsOnAttribute)
                return ((Element)_node).GetAttributeLocalName(_attributeIndex);

            return _node switch
            {
                Element element => element.LocalName,
                ProcessingInstruction pi => pi.Target,
                Attr attr when !_detachedAttributeRoot => attr.LocalName,
                _ => "",
            };
        }
    }

    public override string Name
    {
        get
        {
            if (IsOnNamespace)
                return _namespaces![_namespaceIndex].Prefix;

            if (IsOnAttribute)
            {
                var element = (Element)_node;
                var prefix = element.GetAttributePrefix(_attributeIndex);
                var localName = element.GetAttributeLocalName(_attributeIndex);
                return prefix is null ? localName : prefix + ":" + localName;
            }

            return _node switch
            {
                Element element => element.Prefix is null ? element.LocalName : element.Prefix + ":" + element.LocalName,
                ProcessingInstruction pi => pi.Target,
                Attr attr when !_detachedAttributeRoot => attr.Name,
                _ => "",
            };
        }
    }

    public override string NamespaceURI
    {
        get
        {
            if (IsOnNamespace)
                return "";

            if (IsOnAttribute)
                return ((Element)_node).GetAttributeNamespace(_attributeIndex) ?? "";

            return _node switch
            {
                Element element => element.NamespaceUri ?? "",
                Attr attr when !_detachedAttributeRoot => attr.NamespaceUri ?? "",
                _ => "",
            };
        }
    }

    public override string Prefix
    {
        get
        {
            if (IsOnNamespace)
                return "";

            if (IsOnAttribute)
                return ((Element)_node).GetAttributePrefix(_attributeIndex) ?? "";

            return _node switch
            {
                Element element => element.Prefix ?? "",
                Attr attr when !_detachedAttributeRoot => attr.Prefix ?? "",
                _ => "",
            };
        }
    }

    public override string BaseURI => _node.BaseUri;

    public override bool IsEmptyElement => _node is Element && !IsOnAttribute && !IsOnNamespace && _node.FirstChild is null;

    /// <summary>The string-value of the node (https://www.w3.org/TR/xpath-10/#data-model).</summary>
    public override string Value
    {
        get
        {
            if (IsOnNamespace)
                return _namespaces![_namespaceIndex].Namespace;

            if (IsOnAttribute)
                return ((Element)_node).GetAttributeValue(_attributeIndex);

            switch (_node)
            {
                case Text text:
                {
                    // The string-value of a text node is the data of the whole run of adjacent text nodes
                    if (text.NextSibling is not Text)
                        return text.Data;

                    var builder = new System.Text.StringBuilder();
                    for (Node? node = text; node is Text current; node = node.NextSibling)
                    {
                        builder.Append(current.Data);
                    }

                    return builder.ToString();
                }

                case CharacterData characterData:
                    // Comments and processing instructions
                    return characterData.Data;

                case Attr attr when !_detachedAttributeRoot:
                    // An attribute without owner element
                    return attr.Value;

                default:
                {
                    // The concatenation of the descendant text nodes, in document order
                    var builder = new System.Text.StringBuilder();
                    for (var node = _node.FirstChild; node is not null; node = node.GetNextInTreeOrder(_node))
                    {
                        if (node is Text current)
                        {
                            builder.Append(current.Data);
                        }
                    }

                    return builder.ToString();
                }
            }
        }
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Tree navigation
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns whether the node is represented in the XPath data model (doctypes and empty text runs are not).</summary>
    private static bool IsVisible(Node node) => node is Element or Comment or ProcessingInstruction || (node is Text && !IsEmptyTextRun((Text)node));

    private static bool IsEmptyTextRun(Text text)
    {
        for (Node? node = text; node is Text current; node = node.NextSibling)
        {
            if (current.Length > 0)
                return false;
        }

        return true;
    }

    private static Node GetFirstOfTextRun(Text text)
    {
        Node node = text;
        while (node.PreviousSibling is Text previous)
        {
            node = previous;
        }

        return node;
    }

    private static Node? NextVisibleSibling(Node node)
    {
        var sibling = node.NextSibling;
        if (node is Text)
        {
            // Skip the rest of the text run
            while (sibling is Text)
            {
                sibling = sibling.NextSibling;
            }
        }

        while (sibling is not null && !IsVisible(sibling))
        {
            sibling = sibling.NextSibling;
        }

        return sibling;
    }

    private static Node? PreviousVisibleSibling(Node node)
    {
        var sibling = node.PreviousSibling;
        while (sibling is not null)
        {
            if (sibling is Text text)
            {
                var first = (Text)GetFirstOfTextRun(text);
                if (!IsEmptyTextRun(first))
                    return first;

                sibling = first.PreviousSibling;
                continue;
            }

            if (IsVisible(sibling))
                return sibling;

            sibling = sibling.PreviousSibling;
        }

        return null;
    }

    private static Node? FirstVisibleChild(Node node)
    {
        var child = node.FirstChild;
        while (child is not null && !IsVisible(child))
        {
            child = child.NextSibling;
        }

        return child;
    }

    public override bool MoveToFirstChild()
    {
        if (IsOnAttribute || IsOnNamespace)
            return false;

        var child = FirstVisibleChild(_node);
        if (child is null)
            return false;

        _node = child;
        return true;
    }

    public override bool MoveToNext()
    {
        if (IsOnAttribute || IsOnNamespace || _node.ParentNode is null || _node is Document or DocumentFragment)
            return false;

        var sibling = NextVisibleSibling(_node);
        if (sibling is null)
            return false;

        _node = sibling;
        return true;
    }

    public override bool MoveToPrevious()
    {
        if (IsOnAttribute || IsOnNamespace || _node.ParentNode is null || _node is Document or DocumentFragment)
            return false;

        var sibling = PreviousVisibleSibling(_node);
        if (sibling is null)
            return false;

        _node = sibling;
        return true;
    }

    public override bool MoveToParent()
    {
        if (IsOnAttribute || IsOnNamespace)
        {
            _attributeIndex = -1;
            _namespaceIndex = -1;
            _namespaces = null;
            return true;
        }

        if (_node is Attr && !_detachedAttributeRoot)
        {
            _detachedAttributeRoot = true;
            return true;
        }

        var parent = _node.ParentNode;
        if (parent is null)
            return false;

        _node = parent;
        return true;
    }

    public override void MoveToRoot()
    {
        _attributeIndex = -1;
        _namespaceIndex = -1;
        _namespaces = null;
        _node = _node.GetRoot();
        _detachedAttributeRoot = _node is Attr;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Attributes (xmlns attributes are namespace nodes, not attributes)
    // ----------------------------------------------------------------------------------------------------------------

    private static bool IsNamespaceDeclaration(Element element, int index) => ReferenceEquals(element.GetAttributeNamespace(index), Namespaces.Xmlns);

    public override bool MoveToFirstAttribute()
    {
        if (_node is not Element element || IsOnAttribute || IsOnNamespace)
            return false;

        for (var i = 0; i < element.AttributeCount; i++)
        {
            if (!IsNamespaceDeclaration(element, i))
            {
                _attributeIndex = i;
                return true;
            }
        }

        return false;
    }

    public override bool MoveToNextAttribute()
    {
        if (!IsOnAttribute)
            return false;

        var element = (Element)_node;
        for (var i = _attributeIndex + 1; i < element.AttributeCount; i++)
        {
            if (!IsNamespaceDeclaration(element, i))
            {
                _attributeIndex = i;
                return true;
            }
        }

        return false;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Namespaces
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the in-scope namespaces of an element (prefix "" is the default namespace), excluding "xml".</summary>
    private static List<(string Prefix, string Namespace)> GetInScopeNamespaces(Element element)
    {
        var result = new List<(string Prefix, string Namespace)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = element; current is not null; current = current.ParentElement)
        {
            Add(current.Prefix ?? "", current.NamespaceUri);
            for (var i = 0; i < current.AttributeCount; i++)
            {
                var ns = current.GetAttributeNamespace(i);
                if (ReferenceEquals(ns, Namespaces.Xmlns))
                {
                    var prefix = current.GetAttributePrefix(i) is null ? "" : current.GetAttributeLocalName(i);
                    Add(prefix, current.GetAttributeValue(i));
                }
                else if (ns is not null && current.GetAttributePrefix(i) is { } attributePrefix)
                {
                    Add(attributePrefix, ns);
                }
            }
        }

        return result;

        void Add(string prefix, string? ns)
        {
            if (!seen.Add(prefix) || prefix == "xml" || string.IsNullOrEmpty(ns))
                return;

            result.Add((prefix, ns));
        }
    }

    public override bool MoveToFirstNamespace(XPathNamespaceScope namespaceScope)
    {
        if (_node is not Element element || IsOnAttribute || IsOnNamespace)
            return false;

        var namespaces = GetInScopeNamespaces(element);
        if (namespaceScope == XPathNamespaceScope.All)
        {
            namespaces.Add(("xml", Namespaces.Xml));
        }
        else if (namespaceScope == XPathNamespaceScope.Local && element.ParentElement is { } parent)
        {
            // The namespaces declared by the element: the ones that are not in scope with the same value on the parent
            var inherited = GetInScopeNamespaces(parent);
            namespaces.RemoveAll(inherited.Contains);
        }

        if (namespaces.Count == 0)
            return false;

        _namespaces = namespaces;
        _namespaceIndex = 0;
        return true;
    }

    public override bool MoveToNextNamespace(XPathNamespaceScope namespaceScope)
    {
        if (!IsOnNamespace || _namespaceIndex + 1 >= _namespaces!.Count)
            return false;

        _namespaceIndex++;
        return true;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Identity, ids and ordering
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The id() function (https://www.w3.org/TR/xpath-10/#function-id): the elements whose ID is the given value.</summary>
    public override bool MoveToId(string id)
    {
        // The elements of the tree scope of the node (its shadow root, or else its document, as in the browsers): the id()
        // of a node of a disconnected tree selects the elements of the document
        Node root = _node.GetRoot() as ShadowRoot ?? (Node)_node.NodeDocument;
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Element element && element.IdValue == id)
            {
                _node = element;
                _attributeIndex = -1;
                _namespaceIndex = -1;
                _namespaces = null;
                _detachedAttributeRoot = false;
                return true;
            }
        }

        return false;
    }

    public override XmlNodeOrder ComparePosition(XPathNavigator? nav)
    {
        if (nav is not DomXPathNavigator other)
            return XmlNodeOrder.Unknown;

        if (IsSamePosition(other))
            return XmlNodeOrder.Same;

        if (!ReferenceEquals(_node, other._node))
        {
            // The trees are in an arbitrary but consistent order, so that the union of nodes of different trees keeps them all
            var root = _node.GetRoot();
            var otherRoot = other._node.GetRoot();
            if (!ReferenceEquals(root, otherRoot))
            {
                var hash = RuntimeHelpers.GetHashCode(root);
                var otherHash = RuntimeHelpers.GetHashCode(otherRoot);
                return hash == otherHash ? XmlNodeOrder.Unknown : hash < otherHash ? XmlNodeOrder.Before : XmlNodeOrder.After;
            }

            // An element precedes its attributes and namespaces, which precede its children
            if (_node.IsAncestorOf(other._node))
                return XmlNodeOrder.Before;

            if (other._node.IsAncestorOf(_node))
                return XmlNodeOrder.After;

            return _node.IsPreceding(other._node) ? XmlNodeOrder.Before : XmlNodeOrder.After;
        }

        // Same element: the element, then its namespace nodes, then its attributes
        static int Rank(DomXPathNavigator navigator) => navigator._detachedAttributeRoot ? -1 : navigator.IsOnNamespace ? 1 + navigator._namespaceIndex : navigator.IsOnAttribute ? 100000 + navigator._attributeIndex : 0;
        return Rank(this) < Rank(other) ? XmlNodeOrder.Before : XmlNodeOrder.After;
    }
}
