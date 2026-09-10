using System.Xml;
using System.Xml.XPath;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>Everything a document is made of, in the order it was written.</summary>
public sealed class XmlDocumentSyntax : XmlSyntaxNode, IXPathNavigable
{
    private SyntaxNode? _nodes;

    internal XmlDocumentSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxList<XmlNodeSyntax> Nodes => new(GetRed(ref _nodes, 0));
    public SyntaxToken EndOfFileToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Returns an XPath view of this document, backed by its own nodes.</summary>
    public override XPathNavigator CreateNavigator() => new XmlSyntaxNavigator(this);

    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> is <see langword="null"/>.</exception>
    public override IEnumerable<XPathNavigator> SelectNodes(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);

        return Select(xpath, namespaceResolver);
    }

    /// <summary>Returns this document with the node <paramref name="xpath"/> selects put through <paramref name="updateNode"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> or <paramref name="updateNode"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Nothing matches <paramref name="xpath"/>.</exception>
    public XmlDocumentSyntax ReplaceNode(string xpath, Func<XmlSyntaxNode, XmlSyntaxNode> updateNode) => ReplaceNode(xpath, namespaceResolver: null, updateNode);

    /// <summary>Returns this document with the node <paramref name="xpath"/> selects put through <paramref name="updateNode"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="xpath"/> or <paramref name="updateNode"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Nothing matches <paramref name="xpath"/>.</exception>
    public XmlDocumentSyntax ReplaceNode(string xpath, IXmlNamespaceResolver? namespaceResolver, Func<XmlSyntaxNode, XmlSyntaxNode> updateNode)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        ArgumentNullException.ThrowIfNull(updateNode);

        var node = SelectSingleSyntaxNode(xpath, namespaceResolver) ?? throw new InvalidOperationException($"Cannot find XML node matching XPath '{xpath}'.");

        return this.ReplaceNode(node, updateNode(node));
    }

    /// <summary>Returns this document laid out again by <see cref="Formatter"/>.</summary>
    public XmlDocumentSyntax NormalizeWhitespace() => Formatter.Format(this);

    private IEnumerable<XPathNavigator> Select(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        var navigator = CreateNavigator();
        var expression = navigator.Compile(xpath);
        if (namespaceResolver is not null)
        {
            expression.SetContext(namespaceResolver);
        }

        var iterator = navigator.Select(expression);
        while (iterator.MoveNext())
        {
            if (iterator.Current is not null)
                yield return iterator.Current.Clone();
        }
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlDocumentSyntax Update(SyntaxList<XmlNodeSyntax> nodes, SyntaxToken endOfFileToken)
    {
        if (nodes.Green == Green.GetSlot(0) && endOfFileToken.Node == Green.GetSlot(1))
            return this;

        return SyntaxFactory.XmlDocument(nodes, endOfFileToken).WithAnnotationsFrom(this);
    }

    public XmlDocumentSyntax WithNodes(SyntaxList<XmlNodeSyntax> nodes) => Update(nodes, EndOfFileToken);
    public XmlDocumentSyntax WithEndOfFileToken(SyntaxToken endOfFileToken) => Update(Nodes, endOfFileToken);

    internal override SyntaxNode? GetNodeSlot(int index) => index switch
{
        0 => GetRed(ref _nodes, 0),
        _ => null,
    };

    internal override SyntaxNode? GetCachedSlot(int index) => index switch
{
        0 => _nodes,
        _ => null,
    };

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitDocument(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitDocument(this);
    }
}
