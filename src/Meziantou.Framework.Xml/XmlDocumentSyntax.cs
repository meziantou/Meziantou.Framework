using System.Xml;
using System.Xml.XPath;

namespace Meziantou.Framework.Xml;

/// <summary>Represents the root XML document node and provides XPath-based selection/editing helpers.</summary>
/// <example>
/// <code>
/// var document = XmlSyntaxTree.ParseText("&lt;root&gt;&lt;item/&gt;&lt;/root&gt;").Root;
/// var node = document.SelectSingleSyntaxNode("/root/item");
/// </code>
/// </example>
public sealed class XmlDocumentSyntax : XmlSyntaxNode, IXPathNavigable
{
    private readonly IReadOnlyList<XmlSyntaxNode> _childNodes;

    public XmlDocumentSyntax(IReadOnlyList<XmlSyntaxNode> childNodes, string? fullText = null)
        : base(XmlSyntaxKind.XmlDocument, fullText ?? BuildFullText(childNodes))
    {
        _childNodes = childNodes ?? [];
    }

    public override IReadOnlyList<XmlSyntaxNode> ChildNodes => _childNodes;

    public XmlDocumentSyntax WithChildNodes(IEnumerable<XmlSyntaxNode> childNodes)
    {
        var nodes = childNodes?.ToArray() ?? [];
        if (nodes.SequenceEqual(ChildNodes))
            return this;

        return XmlSyntaxTree.ParseText(BuildFullText(nodes)).Root;
    }

    public override XmlDocumentSyntax ReplaceNode(XmlSyntaxNode oldNode, XmlSyntaxNode newNode)
    {
        ArgumentNullException.ThrowIfNull(oldNode);
        ArgumentNullException.ThrowIfNull(newNode);

        if (TryGetNodeSpan(this, 0, oldNode, out var span) || TryFindUniqueTextSpan(oldNode.ToFullString(), out span))
            return ReplaceSpan(span, newNode.ToFullString());

        return this;
    }

    public override XmlDocumentSyntax ReplaceToken(XmlSyntaxToken oldToken, XmlSyntaxToken newToken)
    {
        ArgumentNullException.ThrowIfNull(oldToken);
        ArgumentNullException.ThrowIfNull(newToken);

        if (TryGetTokenSpan(oldToken, out var span) || TryFindUniqueTextSpan(oldToken.ToFullString(), out span))
            return ReplaceSpan(span, newToken.ToFullString());

        return this;
    }

    public override XmlDocumentSyntax ReplaceTrivia(XmlSyntaxTrivia oldTrivia, XmlSyntaxTrivia newTrivia)
    {
        ArgumentNullException.ThrowIfNull(oldTrivia);
        ArgumentNullException.ThrowIfNull(newTrivia);

        if (TryGetTriviaSpan(oldTrivia, out var span) || TryFindUniqueTextSpan(oldTrivia.Text, out span))
            return ReplaceSpan(span, newTrivia.Text);

        return this;
    }

    public new XmlDocumentSyntax NormalizeWhitespace() => Formatter.Format(this);

    public override XPathNavigator CreateNavigator() => new XmlSyntaxNavigator(this);

    public override IEnumerable<XPathNavigator> SelectNodes(string xpath)
    {
        return SelectNodes(xpath, namespaceResolver: null);
    }

    public override IEnumerable<XPathNavigator> SelectNodes(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);

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

    public override XPathNavigator? SelectSingleNode(string xpath)
    {
        return SelectSingleNode(xpath, namespaceResolver: null);
    }

    public override XPathNavigator? SelectSingleNode(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        return SelectNodes(xpath, namespaceResolver).FirstOrDefault();
    }

    public override IEnumerable<XmlSyntaxNode> SelectSyntaxNodes(string xpath)
    {
        return SelectSyntaxNodes(xpath, namespaceResolver: null);
    }

    public override IEnumerable<XmlSyntaxNode> SelectSyntaxNodes(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);

        foreach (var navigator in SelectNodes(xpath, namespaceResolver))
        {
            if (navigator.UnderlyingObject is XmlSyntaxNode node)
            {
                yield return node;
            }
        }
    }

    public override XmlSyntaxNode? SelectSingleSyntaxNode(string xpath)
    {
        return SelectSingleSyntaxNode(xpath, namespaceResolver: null);
    }

    public override XmlSyntaxNode? SelectSingleSyntaxNode(string xpath, IXmlNamespaceResolver? namespaceResolver)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        return SelectSyntaxNodes(xpath, namespaceResolver).FirstOrDefault();
    }

    public XmlDocumentSyntax ReplaceNode(string xpath, Func<XmlSyntaxNode, XmlSyntaxNode> updateNode)
    {
        return ReplaceNode(xpath, namespaceResolver: null, updateNode);
    }

    public XmlDocumentSyntax ReplaceNode(string xpath, IXmlNamespaceResolver? namespaceResolver, Func<XmlSyntaxNode, XmlSyntaxNode> updateNode)
    {
        ArgumentNullException.ThrowIfNull(xpath);
        ArgumentNullException.ThrowIfNull(updateNode);

        var node = SelectSingleSyntaxNode(xpath, namespaceResolver) ?? throw new InvalidOperationException($"Cannot find XML node matching XPath '{xpath}'.");
        return ReplaceNode(node, updateNode(node));
    }

    public override void Accept(XmlSyntaxVisitor visitor) => visitor.VisitDocument(this);
    public override TResult Accept<TResult>(XmlSyntaxVisitor<TResult> visitor) => visitor.VisitDocument(this);

    private XmlDocumentSyntax ReplaceSpan(TextSpan span, string newText)
    {
        if (span.Length == 0)
            return this;

        var source = ToFullString();
        var builder = new StringBuilder(source.Length - span.Length + newText.Length);
        builder.Append(source.AsSpan(0, span.Start));
        builder.Append(newText);
        builder.Append(source.AsSpan(span.End));
        return XmlSyntaxTree.ParseText(builder.ToString()).Root;
    }

    private static bool TryGetNodeSpan(XmlSyntaxNode current, int currentStart, XmlSyntaxNode targetNode, out TextSpan span)
    {
        if (ReferenceEquals(current, targetNode))
        {
            span = new TextSpan(currentStart, current.ToFullString().Length);
            return true;
        }

        var currentText = current.ToFullString();
        var searchStart = 0;
        foreach (var child in current.ChildNodes)
        {
            var childText = child.ToFullString();
            var childIndex = currentText.IndexOf(childText, searchStart, StringComparison.Ordinal);
            if (childIndex < 0)
                break;

            if (TryGetNodeSpan(child, currentStart + childIndex, targetNode, out span))
                return true;

            searchStart = childIndex + childText.Length;
        }

        span = default;
        return false;
    }

    private bool TryGetTokenSpan(XmlSyntaxToken token, out TextSpan span)
    {
        if (token.Parent is not XmlSyntaxNode parentNode)
        {
            span = default;
            return false;
        }

        if (!TryGetNodeSpan(this, 0, parentNode, out var parentSpan))
        {
            span = default;
            return false;
        }

        var parentText = parentNode.ToFullString();
        var searchStart = 0;
        foreach (var currentToken in parentNode.Tokens)
        {
            var tokenText = currentToken.ToFullString();
            var tokenIndex = parentText.IndexOf(tokenText, searchStart, StringComparison.Ordinal);
            if (tokenIndex < 0)
                break;

            if (ReferenceEquals(currentToken, token))
            {
                span = TextSpan.FromBounds(parentSpan.Start + tokenIndex, parentSpan.Start + tokenIndex + tokenText.Length);
                return true;
            }

            searchStart = tokenIndex + tokenText.Length;
        }

        span = default;
        return false;
    }

    private bool TryGetTriviaSpan(XmlSyntaxTrivia trivia, out TextSpan span)
    {
        foreach (var token in DescendantTokens())
        {
            if (!TryGetTokenSpan(token, out var tokenSpan))
                continue;

            var currentOffset = tokenSpan.Start;
            foreach (var currentTrivia in token.LeadingTrivia)
            {
                if (ReferenceEquals(currentTrivia, trivia))
                {
                    span = new TextSpan(currentOffset, currentTrivia.Text.Length);
                    return true;
                }

                currentOffset += currentTrivia.Text.Length;
            }

            currentOffset += token.Text.Length;
            foreach (var currentTrivia in token.TrailingTrivia)
            {
                if (ReferenceEquals(currentTrivia, trivia))
                {
                    span = new TextSpan(currentOffset, currentTrivia.Text.Length);
                    return true;
                }

                currentOffset += currentTrivia.Text.Length;
            }
        }

        span = default;
        return false;
    }

    private bool TryFindUniqueTextSpan(string text, out TextSpan span)
    {
        if (text.Length == 0)
        {
            span = default;
            return false;
        }

        var source = ToFullString();
        var firstIndex = source.IndexOf(text, StringComparison.Ordinal);
        if (firstIndex < 0)
        {
            span = default;
            return false;
        }

        var secondIndex = source.IndexOf(text, firstIndex + text.Length, StringComparison.Ordinal);
        if (secondIndex >= 0)
        {
            span = default;
            return false;
        }

        span = TextSpan.FromBounds(firstIndex, firstIndex + text.Length);
        return true;
    }
}
