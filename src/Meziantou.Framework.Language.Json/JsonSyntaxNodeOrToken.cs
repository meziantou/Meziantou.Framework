namespace Meziantou.Framework.Language.Json;

/// <summary>Represents either a syntax node or a syntax token in traversal APIs.</summary>
public readonly struct JsonSyntaxNodeOrToken
{
    private readonly JsonSyntaxNode? _node;
    private readonly JsonSyntaxToken? _token;

    public JsonSyntaxNodeOrToken(JsonSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        _token = null;
    }

    public JsonSyntaxNodeOrToken(JsonSyntaxToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _token = token;
        _node = null;
    }

    public bool IsNode => _node is not null;
    public bool IsToken => _token is not null;
    public JsonSyntaxNode Node => _node ?? throw new InvalidOperationException("Current value is not a node.");
    public JsonSyntaxToken Token => _token ?? throw new InvalidOperationException("Current value is not a token.");
    public JsonSyntaxKind Kind => IsNode ? Node.Kind : Token.Kind;
}
