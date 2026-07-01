using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.Language.Xml;

/// <summary>
/// <see cref="XPathNavigator"/> implementation backed directly by <see cref="XmlSyntaxNode"/> instances.
/// </summary>
/// <example>
/// <code>
/// var navigator = new XmlSyntaxNavigator(document);
/// var result = navigator.SelectSingleNode("/root/item");
/// </code>
/// </example>
internal sealed class XmlSyntaxNavigator : XPathNavigator
{
    private const string XmlNamespaceUri = "http://www.w3.org/XML/1998/namespace";
    private const string XmlnsNamespaceUri = "http://www.w3.org/2000/xmlns/";

    private readonly XmlNameTable _nameTable;
    private readonly NavigatorNode _root;
    private NavigatorNode _current;

    public XmlSyntaxNavigator(XmlDocumentSyntax document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _nameTable = new NameTable();
        _root = CreateTree(document, _nameTable);
        _current = _root;
    }

    private XmlSyntaxNavigator(XmlNameTable nameTable, NavigatorNode root, NavigatorNode current)
    {
        _nameTable = nameTable;
        _root = root;
        _current = current;
    }

    public override string BaseURI => string.Empty;
    public override bool IsEmptyElement => _current.NodeType == XPathNodeType.Element && _current.Children.Count == 0;
    public override string LocalName => _current.LocalName;
    public override string Name => _current.Name;
    public override string NamespaceURI => _current.NamespaceUri;
    public override XmlNameTable NameTable => _nameTable;
    public override XPathNodeType NodeType => _current.NodeType;
    public override string Prefix => _current.Prefix;
    public override string Value => GetValue(_current);
    public override string XmlLang => string.Empty;
    public override object UnderlyingObject => _current.UnderlyingObject ?? this;

    public override XPathNavigator Clone() => new XmlSyntaxNavigator(_nameTable, _root, _current);

    public override bool IsSamePosition(XPathNavigator other)
    {
        return other is XmlSyntaxNavigator navigator &&
               ReferenceEquals(_root, navigator._root) &&
               ReferenceEquals(_current, navigator._current);
    }

    public override bool MoveTo(XPathNavigator other)
    {
        if (other is not XmlSyntaxNavigator navigator || !ReferenceEquals(_root, navigator._root))
            return false;

        _current = navigator._current;
        return true;
    }

    public override bool MoveToFirstAttribute()
    {
        if (_current.NodeType != XPathNodeType.Element || _current.Attributes.Count == 0)
            return false;

        _current = _current.Attributes[0];
        return true;
    }

    public override bool MoveToNextAttribute()
    {
        if (_current.NodeType != XPathNodeType.Attribute || _current.Parent is null)
            return false;

        var siblings = _current.Parent.Attributes;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (!ReferenceEquals(siblings[i], _current))
                continue;

            if (i + 1 >= siblings.Count)
                return false;

            _current = siblings[i + 1];
            return true;
        }

        return false;
    }

    public override bool MoveToFirstChild()
    {
        if (_current.NodeType == XPathNodeType.Attribute || _current.Children.Count == 0)
            return false;

        _current = _current.Children[0];
        return true;
    }

    public override bool MoveToFirstNamespace(XPathNamespaceScope namespaceScope) => false;
    public override bool MoveToId(string id) => false;

    public override bool MoveToNext()
    {
        if (_current.Parent is null || _current.NodeType == XPathNodeType.Attribute)
            return false;

        var siblings = _current.Parent.Children;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (!ReferenceEquals(siblings[i], _current))
                continue;

            if (i + 1 >= siblings.Count)
                return false;

            _current = siblings[i + 1];
            return true;
        }

        return false;
    }

    public override bool MoveToNextNamespace(XPathNamespaceScope namespaceScope) => false;

    public override bool MoveToParent()
    {
        if (_current.Parent is null)
            return false;

        _current = _current.Parent;
        return true;
    }

    public override bool MoveToPrevious()
    {
        if (_current.Parent is null || _current.NodeType == XPathNodeType.Attribute)
            return false;

        var siblings = _current.Parent.Children;
        for (var i = 0; i < siblings.Count; i++)
        {
            if (!ReferenceEquals(siblings[i], _current))
                continue;

            if (i == 0)
                return false;

            _current = siblings[i - 1];
            return true;
        }

        return false;
    }

    public override void MoveToRoot() => _current = _root;

    private static NavigatorNode CreateTree(XmlDocumentSyntax document, XmlNameTable nameTable)
    {
        var root = new NavigatorNode(
            nodeType: XPathNodeType.Root,
            localName: string.Empty,
            prefix: string.Empty,
            namespaceUri: string.Empty,
            value: string.Empty,
            underlyingObject: document);

        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["xml"] = XmlNamespaceUri,
            ["xmlns"] = XmlnsNamespaceUri,
        };

        foreach (var child in document.ChildNodes)
        {
            AddChild(root, CreateNode(child, nameTable, namespaces));
        }

        return root;
    }

    private static NavigatorNode? CreateNode(XmlSyntaxNode node, XmlNameTable nameTable, IReadOnlyDictionary<string, string> namespaces)
    {
        return node switch
        {
            XmlElementSyntax element => CreateElementNode(element, nameTable, namespaces),
            XmlAttributeSyntax attribute => CreateAttributeNode(attribute, nameTable, namespaces),
            XmlTextSyntax text => CreateTextNode(text),
            XmlCommentSyntax comment => CreateCommentNode(comment),
            XmlCDataSectionSyntax cdataSection => CreateCDataNode(cdataSection),
            XmlProcessingInstructionSyntax processingInstruction => CreateProcessingInstructionNode(processingInstruction),
            XmlDeclarationSyntax declaration => CreateDeclarationNode(declaration),
            XmlSkippedTextSyntax skippedText => CreateSkippedTextNode(skippedText),
            _ => null,
        };
    }

    private static NavigatorNode CreateElementNode(XmlElementSyntax element, XmlNameTable nameTable, IReadOnlyDictionary<string, string> inheritedNamespaces)
    {
        var localNamespaces = new Dictionary<string, string>(inheritedNamespaces, StringComparer.Ordinal);
        foreach (var attribute in element.Attributes)
        {
            if (TryGetNamespaceDeclaration(attribute, out var namespacePrefix, out var namespaceUri))
            {
                localNamespaces[namespacePrefix] = namespaceUri;
            }
        }

        var (prefix, localName) = SplitName(element.Name);
        var result = new NavigatorNode(
            nodeType: XPathNodeType.Element,
            localName: AddName(nameTable, localName),
            prefix: AddName(nameTable, prefix),
            namespaceUri: AddName(nameTable, ResolveNamespaceUri(localNamespaces, prefix, isAttribute: false)),
            value: string.Empty,
            underlyingObject: element);

        foreach (var attribute in element.Attributes)
        {
            AddAttribute(result, CreateAttributeNode(attribute, nameTable, localNamespaces));
        }

        foreach (var contentNode in element.Content)
        {
            AddChild(result, CreateNode(contentNode, nameTable, localNamespaces));
        }

        return result;
    }

    private static NavigatorNode CreateAttributeNode(XmlAttributeSyntax attribute, XmlNameTable nameTable, IReadOnlyDictionary<string, string> namespaces)
    {
        var (prefix, localName) = SplitName(attribute.Name);
        var namespaceUri = ResolveNamespaceUri(namespaces, prefix, isAttribute: true);
        if (string.Equals(attribute.Name, "xmlns", StringComparison.Ordinal) || string.Equals(prefix, "xmlns", StringComparison.Ordinal))
        {
            namespaceUri = XmlnsNamespaceUri;
        }

        return new NavigatorNode(
            nodeType: XPathNodeType.Attribute,
            localName: AddName(nameTable, localName),
            prefix: AddName(nameTable, prefix),
            namespaceUri: AddName(nameTable, namespaceUri),
            value: attribute.Value,
            underlyingObject: attribute);
    }

    private static NavigatorNode CreateTextNode(XmlTextSyntax text)
    {
        return new NavigatorNode(
            nodeType: XPathNodeType.Text,
            localName: string.Empty,
            prefix: string.Empty,
            namespaceUri: string.Empty,
            value: text.Text,
            underlyingObject: text);
    }

    private static NavigatorNode CreateCommentNode(XmlCommentSyntax comment)
    {
        return new NavigatorNode(
            nodeType: XPathNodeType.Comment,
            localName: string.Empty,
            prefix: string.Empty,
            namespaceUri: string.Empty,
            value: comment.Text,
            underlyingObject: comment);
    }

    private static NavigatorNode CreateCDataNode(XmlCDataSectionSyntax cdataSection)
    {
        return new NavigatorNode(
            nodeType: XPathNodeType.Text,
            localName: string.Empty,
            prefix: string.Empty,
            namespaceUri: string.Empty,
            value: cdataSection.Text,
            underlyingObject: cdataSection);
    }

    private static NavigatorNode CreateProcessingInstructionNode(XmlProcessingInstructionSyntax processingInstruction)
    {
        return new NavigatorNode(
            nodeType: XPathNodeType.ProcessingInstruction,
            localName: processingInstruction.Target,
            prefix: string.Empty,
            namespaceUri: string.Empty,
            value: processingInstruction.Data ?? string.Empty,
            underlyingObject: processingInstruction);
    }

    private static NavigatorNode CreateDeclarationNode(XmlDeclarationSyntax declaration)
    {
        var builder = new StringBuilder();
        builder.Append("version=\"");
        builder.Append(declaration.Version);
        builder.Append('"');
        if (declaration.Encoding is not null)
        {
            builder.Append(" encoding=\"");
            builder.Append(declaration.Encoding);
            builder.Append('"');
        }

        if (declaration.Standalone is not null)
        {
            builder.Append(" standalone=\"");
            builder.Append(declaration.Standalone);
            builder.Append('"');
        }

        return new NavigatorNode(
            nodeType: XPathNodeType.ProcessingInstruction,
            localName: "xml",
            prefix: string.Empty,
            namespaceUri: string.Empty,
            value: builder.ToString(),
            underlyingObject: declaration);
    }

    private static NavigatorNode CreateSkippedTextNode(XmlSkippedTextSyntax skippedText)
    {
        return new NavigatorNode(
            nodeType: XPathNodeType.Text,
            localName: string.Empty,
            prefix: string.Empty,
            namespaceUri: string.Empty,
            value: skippedText.Text,
            underlyingObject: skippedText);
    }

    private static string GetValue(NavigatorNode node)
    {
        if (node.NodeType is not XPathNodeType.Root and not XPathNodeType.Element)
            return node.Value;

        var builder = new StringBuilder();
        AppendDescendantText(node, builder);
        return builder.ToString();
    }

    private static void AppendDescendantText(NavigatorNode node, StringBuilder builder)
    {
        foreach (var child in node.Children)
        {
            if (child.NodeType == XPathNodeType.Text)
            {
                builder.Append(child.Value);
                continue;
            }

            if (child.NodeType == XPathNodeType.Element)
            {
                AppendDescendantText(child, builder);
            }
        }
    }

    private static void AddChild(NavigatorNode parent, NavigatorNode? child)
    {
        if (child is null)
            return;

        child.Parent = parent;
        parent.Children.Add(child);
    }

    private static void AddAttribute(NavigatorNode parent, NavigatorNode attribute)
    {
        attribute.Parent = parent;
        parent.Attributes.Add(attribute);
    }

    private static (string Prefix, string LocalName) SplitName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return (string.Empty, string.Empty);

        var separator = name.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || separator >= name.Length - 1)
            return (string.Empty, name);

        return (name[..separator], name[(separator + 1)..]);
    }

    private static string AddName(XmlNameTable nameTable, string value)
    {
        if (value.Length == 0)
            return string.Empty;

        return nameTable.Add(value);
    }

    private static bool TryGetNamespaceDeclaration(XmlAttributeSyntax attribute, out string prefix, out string namespaceUri)
    {
        if (string.Equals(attribute.Name, "xmlns", StringComparison.Ordinal))
        {
            prefix = string.Empty;
            namespaceUri = attribute.Value;
            return true;
        }

        const string XmlnsPrefix = "xmlns:";
        if (attribute.Name.StartsWith(XmlnsPrefix, StringComparison.Ordinal) && attribute.Name.Length > XmlnsPrefix.Length)
        {
            prefix = attribute.Name[XmlnsPrefix.Length..];
            namespaceUri = attribute.Value;
            return true;
        }

        prefix = string.Empty;
        namespaceUri = string.Empty;
        return false;
    }

    private static string ResolveNamespaceUri(IReadOnlyDictionary<string, string> namespaces, string prefix, bool isAttribute)
    {
        if (prefix.Length == 0)
        {
            if (isAttribute)
                return string.Empty;

            return namespaces.TryGetValue(string.Empty, out var defaultNamespace) ? defaultNamespace : string.Empty;
        }

        return namespaces.TryGetValue(prefix, out var namespaceUri) ? namespaceUri : string.Empty;
    }

    private sealed class NavigatorNode
    {
        public NavigatorNode(XPathNodeType nodeType, string localName, string prefix, string namespaceUri, string value, object? underlyingObject)
        {
            NodeType = nodeType;
            LocalName = localName;
            Prefix = prefix;
            NamespaceUri = namespaceUri;
            Value = value;
            UnderlyingObject = underlyingObject;
        }

        public XPathNodeType NodeType { get; }
        public string LocalName { get; }
        public string Prefix { get; }
        public string NamespaceUri { get; }
        public string Value { get; }
        public object? UnderlyingObject { get; }
        public string Name => Prefix.Length == 0 ? LocalName : Prefix + ":" + LocalName;
        public NavigatorNode? Parent { get; set; }
        public List<NavigatorNode> Attributes { get; } = [];
        public List<NavigatorNode> Children { get; } = [];
    }
}
