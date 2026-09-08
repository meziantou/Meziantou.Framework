using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.Language.Xml;

/// <summary>Base type for all XML syntax nodes in the immutable syntax tree.</summary>
/// <example>
/// <code>
/// foreach (var node in tree.Root.DescendantNodes())
/// {
///     _ = node.Kind;
/// }
/// </code>
/// </example>
public abstract class XmlSyntaxNode
{
    protected XmlSyntaxNode(XmlSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<XmlSyntaxToken>? tokens = null)
    {
        Kind = kind;
        FullText = fullText ?? string.Empty;
        FullSpan = new TextSpan(fullStart, FullText.Length);
        Tokens = tokens ?? [];
        foreach (var token in Tokens)
        {
            token.Parent = this;
        }
    }

    protected string FullText { get; }
    public XmlSyntaxKind Kind { get; }
    public virtual IReadOnlyList<XmlSyntaxNode> ChildNodes => [];
    public IReadOnlyList<XmlSyntaxToken> Tokens { get; }
    public XmlSyntaxTree? SyntaxTree { get; internal set; }
    public XmlSyntaxNode? Parent => ParentNode;

    /// <summary>The absolute span of this node in the source text.</summary>
    /// <remarks>
    /// Equal to <see cref="FullSpan"/>. The two differ elsewhere only to exclude a node's leading and trailing
    /// trivia, and the XML parser attaches no trivia to nodes: whitespace stays inside the node's own text.
    /// </remarks>
    public TextSpan Span => FullSpan;

    /// <summary>The absolute span of this node in the source text, including everything it round-trips.</summary>
    public TextSpan FullSpan { get; }
    public bool ContainsDiagnostics => SyntaxTree is not null && SyntaxTree.Diagnostics.Count > 0;
    public bool ContainsSkippedText => Kind == XmlSyntaxKind.XmlSkippedText || DescendantNodes().Any(node => node.Kind == XmlSyntaxKind.XmlSkippedText);
    internal XmlSyntaxNode? ParentNode { get; set; }

    public virtual string ToFullString() => FullText;

    public IEnumerable<XmlSyntaxNodeOrToken> ChildNodesAndTokens()
    {
        foreach (var child in ChildNodes)
        {
            yield return new XmlSyntaxNodeOrToken(child);
        }

        foreach (var token in Tokens)
        {
            yield return new XmlSyntaxNodeOrToken(token);
        }
    }

    public IEnumerable<XmlSyntaxNode> DescendantNodes()
    {
        foreach (var child in ChildNodes)
        {
            yield return child;
            foreach (var descendant in child.DescendantNodes())
            {
                yield return descendant;
            }
        }
    }

    public IEnumerable<XmlSyntaxNodeOrToken> DescendantNodesAndTokens()
    {
        foreach (var token in Tokens)
        {
            yield return new XmlSyntaxNodeOrToken(token);
        }

        foreach (var child in ChildNodes)
        {
            yield return new XmlSyntaxNodeOrToken(child);
            foreach (var descendant in child.DescendantNodesAndTokens())
            {
                yield return descendant;
            }
        }
    }

    public IEnumerable<XmlSyntaxNode> Ancestors()
    {
        var parent = ParentNode;
        while (parent is not null)
        {
            yield return parent;
            parent = parent.ParentNode;
        }
    }

    public IEnumerable<XmlSyntaxNode> AncestorsAndSelf()
    {
        var node = this;
        while (node is not null)
        {
            yield return node;
            node = node.ParentNode;
        }
    }

    public IEnumerable<XmlSyntaxToken> DescendantTokens()
    {
        foreach (var token in Tokens)
        {
            yield return token;
        }

        foreach (var child in ChildNodes)
        {
            foreach (var token in child.DescendantTokens())
            {
                yield return token;
            }
        }
    }

    public IEnumerable<XmlSyntaxTrivia> DescendantTrivia()
    {
        foreach (var token in DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                yield return trivia;
            }

            foreach (var trivia in token.TrailingTrivia)
            {
                yield return trivia;
            }
        }
    }

    public virtual XmlDocumentSyntax ReplaceNode(XmlSyntaxNode oldNode, XmlSyntaxNode newNode) => GetDocument().ReplaceNode(oldNode, newNode);
    public virtual XmlDocumentSyntax ReplaceToken(XmlSyntaxToken oldToken, XmlSyntaxToken newToken) => GetDocument().ReplaceToken(oldToken, newToken);
    public virtual XmlDocumentSyntax ReplaceTrivia(XmlSyntaxTrivia oldTrivia, XmlSyntaxTrivia newTrivia) => GetDocument().ReplaceTrivia(oldTrivia, newTrivia);

    public virtual XmlSyntaxNode NormalizeWhitespace()
    {
        if (this is XmlDocumentSyntax document)
            return Formatter.Format(document);

        var parsed = XmlSyntaxTree.ParseText(ToFullString());
        if (parsed.Root.ChildNodes.Count == 1)
            return parsed.Root.ChildNodes[0];

        return parsed.Root;
    }

    public virtual XPathNavigator CreateNavigator()
    {
        if (this is XmlDocumentSyntax document)
            return document.CreateNavigator();

        return XmlSyntaxTree.ParseText(ToFullString()).Root.CreateNavigator();
    }

    public virtual IEnumerable<XPathNavigator> SelectNodes(string xpath)
    {
        return SelectNodes(xpath, namespaceResolver: null);
    }

    public virtual IEnumerable<XPathNavigator> SelectNodes(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        if (this is XmlDocumentSyntax document)
            return document.SelectNodes(xpath, namespaceResolver);

        return XmlSyntaxTree.ParseText(ToFullString()).Root.SelectNodes(xpath, namespaceResolver);
    }

    public virtual XPathNavigator? SelectSingleNode(string xpath)
    {
        return SelectSingleNode(xpath, namespaceResolver: null);
    }

    public virtual XPathNavigator? SelectSingleNode(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        if (this is XmlDocumentSyntax document)
            return document.SelectSingleNode(xpath, namespaceResolver);

        return XmlSyntaxTree.ParseText(ToFullString()).Root.SelectSingleNode(xpath, namespaceResolver);
    }

    public virtual IEnumerable<XmlSyntaxNode> SelectSyntaxNodes(string xpath)
    {
        return SelectSyntaxNodes(xpath, namespaceResolver: null);
    }

    public virtual IEnumerable<XmlSyntaxNode> SelectSyntaxNodes(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        if (this is XmlDocumentSyntax document)
            return document.SelectSyntaxNodes(xpath, namespaceResolver);

        return XmlSyntaxTree.ParseText(ToFullString()).Root.SelectSyntaxNodes(xpath, namespaceResolver);
    }

    public virtual XmlSyntaxNode? SelectSingleSyntaxNode(string xpath)
    {
        return SelectSingleSyntaxNode(xpath, namespaceResolver: null);
    }

    public virtual XmlSyntaxNode? SelectSingleSyntaxNode(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        if (this is XmlDocumentSyntax document)
            return document.SelectSingleSyntaxNode(xpath, namespaceResolver);

        return XmlSyntaxTree.ParseText(ToFullString()).Root.SelectSingleSyntaxNode(xpath, namespaceResolver);
    }

    internal void SetParentAndTree(XmlSyntaxNode? parent, XmlSyntaxTree tree)
    {
        ParentNode = parent;
        SyntaxTree = tree;
        foreach (var child in ChildNodes)
        {
            child.SetParentAndTree(this, tree);
        }

        foreach (var token in Tokens)
        {
            token.Parent = this;
        }
    }

    private XmlDocumentSyntax GetDocument()
    {
        if (this is XmlDocumentSyntax document)
            return document;

        if (SyntaxTree is not null)
            return SyntaxTree.Root;

        var parent = ParentNode;
        while (parent is not null)
        {
            if (parent is XmlDocumentSyntax parentDocument)
                return parentDocument;

            parent = parent.ParentNode;
        }

        return XmlSyntaxTree.ParseText(ToFullString()).Root;
    }

    internal static string BuildFullText(IEnumerable<XmlSyntaxNode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            builder.Append(node.ToFullString());
        }

        return builder.ToString();
    }

    public abstract void Accept(XmlSyntaxVisitor visitor);
    public abstract TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor);
}
