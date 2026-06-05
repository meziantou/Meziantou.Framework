namespace Meziantou.Framework.Xml;

/// <summary>Represents either a syntax node or a syntax token in traversal APIs.</summary>
/// <example>
/// <code>
/// foreach (var item in tree.Root.DescendantNodesAndTokens())
/// {
///     var kind = item.Kind;
/// }
/// </code>
/// </example>
public readonly struct XmlSyntaxNodeOrToken
{
    private readonly XmlSyntaxNode? _node;
    private readonly XmlSyntaxToken? _token;

    public XmlSyntaxNodeOrToken(XmlSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        _token = null;
    }

    public XmlSyntaxNodeOrToken(XmlSyntaxToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _token = token;
        _node = null;
    }

    public bool IsNode => _node is not null;
    public bool IsToken => _token is not null;
    public XmlSyntaxNode Node => _node ?? throw new InvalidOperationException("Current value is not a node.");
    public XmlSyntaxToken Token => _token ?? throw new InvalidOperationException("Current value is not a token.");
    public XmlSyntaxKind Kind => IsNode ? Node.Kind : Token.Kind;
}
