using System.Xml;
using System.Xml.XPath;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>The base of every node in an XML tree.</summary>
/// <example>
/// <code>
/// foreach (var node in tree.GetRoot().DescendantNodes())
/// {
///     _ = node.Kind();
/// }
/// </code>
/// </example>
public abstract class XmlSyntaxNode : SyntaxNode
{
    private protected XmlSyntaxNode(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets what kind of node this is.</summary>
    public SyntaxKind Kind() => (SyntaxKind)RawKind;

    /// <summary>Gets the node this one is a child of, or <see langword="null"/> when it is the root of its tree.</summary>
    public new XmlSyntaxNode? Parent => (XmlSyntaxNode?)base.Parent;

    /// <summary>Returns an XPath view of the document this node belongs to, or of this node when it is detached.</summary>
    public virtual XPathNavigator CreateNavigator() => GetDocument().CreateNavigator();

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public IEnumerable<XPathNavigator> SelectNodes(string xpath) => SelectNodes(xpath, namespaceResolver: null);

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public virtual IEnumerable<XPathNavigator> SelectNodes(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);

        return GetDocument().SelectNodes(xpath, namespaceResolver);
    }

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public XPathNavigator? SelectSingleNode(string xpath) => SelectSingleNode(xpath, namespaceResolver: null);

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public XPathNavigator? SelectSingleNode(string xpath, IXmlNamespaceResolver? namespaceResolver) => SelectNodes(xpath, namespaceResolver).FirstOrDefault();

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public IEnumerable<XmlSyntaxNode> SelectSyntaxNodes(string xpath) => SelectSyntaxNodes(xpath, namespaceResolver: null);

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public IEnumerable<XmlSyntaxNode> SelectSyntaxNodes(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        foreach (var navigator in SelectNodes(xpath, namespaceResolver))
        {
            if (navigator.UnderlyingObject is XmlSyntaxNode node)
            {
                yield return node;
            }
        }
    }

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public XmlSyntaxNode? SelectSingleSyntaxNode(string xpath) => SelectSingleSyntaxNode(xpath, namespaceResolver: null);

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public XmlSyntaxNode? SelectSingleSyntaxNode(string xpath, IXmlNamespaceResolver? namespaceResolver) => SelectSyntaxNodes(xpath, namespaceResolver).FirstOrDefault();

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract void Accept(XmlSyntaxVisitor visitor);

    /// <summary>Calls the method of <paramref name="visitor"/> that matches this node.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="visitor"/> is <see langword="null"/>.</exception>
    public abstract TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor);

    /// <summary>
    /// Returns the document this node belongs to, re-reading its text as a document of its own when it belongs to
    /// none. XPath is a view of a whole document, so a detached node has to become one to be queried.
    /// </summary>
    private protected XmlDocumentSyntax GetDocument()
    {
        foreach (var ancestor in AncestorsAndSelf())
        {
            if (ancestor is XmlDocumentSyntax document)
                return document;
        }

        return XmlSyntaxTree.ParseText(ToFullString()).GetRoot();
    }
}
