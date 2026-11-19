using System.Text;
using System.Xml.XPath;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Xslt;

/// <summary>
/// Builds a result tree, or a result tree fragment (https://www.w3.org/TR/xslt-10/#section-Creating-the-Result-Tree), in a
/// <see cref="DocumentFragment"/>. The errors of XSLT that can be recovered from are recovered from: an attribute added after
/// the children of an element, or to no element, is ignored.
/// </summary>
internal sealed class XsltResultBuilder
{
    private readonly XsltExecution _execution;
    private readonly Document _document;
    private readonly DocumentFragment _root;
    private readonly StringBuilder _pendingText = new();
    private Node _current;

    // Whether the current element has no child yet, so that attributes can be added to it
    private bool _elementOpen;

    public XsltResultBuilder(XsltExecution execution, DocumentFragment root)
    {
        _execution = execution;
        _document = root.NodeDocument;
        _root = root;
        _current = root;
    }

    public DocumentFragment Finish()
    {
        FlushText();
        return _root;
    }

    public void StartElement(string? ns, string? prefix, string localName)
    {
        FlushText();
        if (string.IsNullOrEmpty(ns) || ns == Namespaces.Xmlns)
        {
            ns = null;
            prefix = null;
        }
        else if (prefix is "xmlns" || (prefix is "xml" && ns != Namespaces.Xml) || (prefix is not "xml" && ns == Namespaces.Xml) || (prefix is not null && !XsltValues.IsNCName(prefix)))
        {
            prefix = ns == Namespaces.Xml ? "xml" : null;
        }

        _execution.CountOutputNode();
        var element = Html.ElementFactory.CreateElement(_document, localName, ns, prefix, isValue: null, synchronousCustomElements: false);
        _current.AppendNode(element);
        _current = element;
        _elementOpen = true;
    }

    public void EndElement()
    {
        FlushText();
        _current = _current.ParentNode ?? _root;
        _elementOpen = false;
    }

    public void Attribute(string? ns, string? prefix, string localName, string value)
    {
        if (!_elementOpen || _current is not Element element || !XsltValues.IsNCName(localName))
            return;

        if (string.IsNullOrEmpty(ns))
        {
            ns = null;
            prefix = null;
            if (localName == "xmlns")
                return;
        }
        else if (ns == Namespaces.Xmlns)
        {
            return;
        }
        else if (ns == Namespaces.Xml)
        {
            prefix = "xml";
        }
        else if (prefix is null or "xml" or "xmlns" || !XsltValues.IsNCName(prefix) || (element.LookupNamespaceUri(prefix) is { } bound && bound != ns))
        {
            prefix = element.LookupPrefix(ns) ?? CreatePrefix(element, ns);
        }

        _execution.CountOutputNode();
        try
        {
            element.SetAttributeNS(ns, prefix is null ? localName : prefix + ":" + localName, value);
        }
        catch (DomException)
        {
            // A name that the DOM does not allow: the attribute is ignored
        }
    }

    /// <summary>A prefix for a namespace that is not bound in the scope of the element.</summary>
    private static string CreatePrefix(Element element, string ns)
    {
        for (var i = 0; ; i++)
        {
            var prefix = "ns" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var bound = element.LookupNamespaceUri(prefix);
            if (bound is null || bound == ns)
                return prefix;
        }
    }

    /// <summary>
    /// Adds a namespace node to the element being created: a namespace declaration attribute, unless the namespace is already
    /// in scope with this prefix.
    /// </summary>
    public void NamespaceNode(string prefix, string uri)
    {
        if (!_elementOpen || _current is not Element element || uri.Length == 0 || prefix is "xml" or "xmlns" || uri == Namespaces.Xml || uri == Namespaces.Xmlns)
            return;

        if (prefix.Length > 0 && !XsltValues.IsNCName(prefix))
            return;

        if (element.LookupNamespaceUri(prefix.Length == 0 ? null : prefix) == uri)
            return;

        // The name of the element has the prefix: its namespace wins
        if ((element.Prefix ?? "") == prefix)
            return;

        _execution.CountOutputNode();
        try
        {
            element.SetAttributeNS(Namespaces.Xmlns, prefix.Length == 0 ? "xmlns" : "xmlns:" + prefix, uri);
        }
        catch (DomException)
        {
            // A name that the DOM does not allow: the namespace node is ignored
        }
    }

    public void Text(string text)
    {
        if (text.Length == 0)
            return;

        _elementOpen = false;
        _pendingText.Append(text);
    }

    public void Comment(string text)
    {
        FlushText();
        _elementOpen = false;

        // A comment cannot contain "--" or end with "-" (https://www.w3.org/TR/xslt-10/#section-Creating-Comments)
        var data = text.Replace("--", "- -", StringComparison.Ordinal).Replace("--", "- -", StringComparison.Ordinal);
        if (data.Length > 0 && data[^1] == '-')
        {
            data += " ";
        }

        _execution.CountOutputNode();
        _current.AppendNode(new Comment(_document, data));
    }

    public void ProcessingInstruction(string target, string data)
    {
        FlushText();
        _elementOpen = false;

        // The data cannot contain "?>" (https://www.w3.org/TR/xslt-10/#section-Creating-Processing-Instructions)
        data = data.Replace("?>", "? >", StringComparison.Ordinal);
        _execution.CountOutputNode();
        _current.AppendNode(new ProcessingInstruction(_document, target, data, validate: false));
    }

    /// <summary>
    /// Copies a node (https://www.w3.org/TR/xslt-10/#copying): with <paramref name="deep"/>, the node with its attributes and
    /// its descendants (xsl:copy-of); otherwise the node and its namespace nodes, and an element is left open (xsl:copy).
    /// </summary>
    public void CopyNode(XPathNavigator node, bool deep)
    {
        switch (node.NodeType)
        {
            case XPathNodeType.Root:
                if (deep)
                {
                    CopyChildren(node);
                }

                break;

            case XPathNodeType.Element:
                StartCopiedElement(node);
                if (deep)
                {
                    CopyAttributes(node);
                    CopyChildren(node);
                    EndElement();
                }

                break;

            case XPathNodeType.Attribute:
                Attribute(node.NamespaceURI, node.Prefix.Length == 0 ? null : node.Prefix, node.LocalName, node.Value);
                break;

            case XPathNodeType.Text or XPathNodeType.Whitespace or XPathNodeType.SignificantWhitespace:
                Text(node.Value);
                break;

            case XPathNodeType.Comment:
                Comment(node.Value);
                break;

            case XPathNodeType.ProcessingInstruction:
                ProcessingInstruction(node.LocalName, node.Value);
                break;

            case XPathNodeType.Namespace:
                NamespaceNode(node.LocalName, node.Value);
                break;
        }
    }

    private void StartCopiedElement(XPathNavigator node)
    {
        StartElement(node.NamespaceURI, node.Prefix.Length == 0 ? null : node.Prefix, node.LocalName);
        var ns = node.Clone();
        if (ns.MoveToFirstNamespace(XPathNamespaceScope.ExcludeXml))
        {
            do
            {
                NamespaceNode(ns.LocalName, ns.Value);
            }
            while (ns.MoveToNextNamespace(XPathNamespaceScope.ExcludeXml));
        }
    }

    private void CopyAttributes(XPathNavigator node)
    {
        var attribute = node.Clone();
        if (attribute.MoveToFirstAttribute())
        {
            do
            {
                Attribute(attribute.NamespaceURI, attribute.Prefix.Length == 0 ? null : attribute.Prefix, attribute.LocalName, attribute.Value);
            }
            while (attribute.MoveToNextAttribute());
        }
    }

    /// <summary>Copies the descendants of a node, without recursion (the trees can be deep).</summary>
    private void CopyChildren(XPathNavigator parent)
    {
        var node = parent.Clone();
        if (!node.MoveToFirstChild())
            return;

        while (true)
        {
            _execution.CheckLimits();
            if (node.NodeType == XPathNodeType.Element)
            {
                StartCopiedElement(node);
                CopyAttributes(node);
                if (node.MoveToFirstChild())
                    continue;

                EndElement();
            }
            else
            {
                CopyNode(node, deep: true);
            }

            while (!node.MoveToNext())
            {
                if (!node.MoveToParent() || node.IsSamePosition(parent))
                    return;

                EndElement();
            }
        }
    }

    private void FlushText()
    {
        if (_pendingText.Length == 0)
            return;

        _execution.CountOutputNode();
        _current.AppendNode(new Text(_document, _pendingText.ToString()));
        _pendingText.Clear();
    }
}
