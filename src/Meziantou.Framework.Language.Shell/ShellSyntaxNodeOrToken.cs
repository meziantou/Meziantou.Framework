namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents either a syntax node or a syntax token in traversal APIs.</summary>
public readonly struct ShellSyntaxNodeOrToken : IEquatable<ShellSyntaxNodeOrToken>
{
    private readonly ShellSyntaxNode? _node;
    private readonly ShellSyntaxToken? _token;

    public ShellSyntaxNodeOrToken(ShellSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        _token = null;
    }

    public ShellSyntaxNodeOrToken(ShellSyntaxToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _token = token;
        _node = null;
    }

    public bool IsNode => _node is not null;
    public bool IsToken => _token is not null;
    public ShellSyntaxNode Node => _node ?? throw new InvalidOperationException("Current value is not a node.");
    public ShellSyntaxToken Token => _token ?? throw new InvalidOperationException("Current value is not a token.");
    public ShellSyntaxKind Kind => IsNode ? Node.Kind : Token.Kind;
    public TextSpan FullSpan => IsNode ? Node.FullSpan : Token.FullSpan;

    public string ToFullString() => IsNode ? Node.ToFullString() : Token.ToFullString();

    public bool Equals(ShellSyntaxNodeOrToken other) => ReferenceEquals(_node, other._node) && ReferenceEquals(_token, other._token);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is ShellSyntaxNodeOrToken other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_node, _token);
    public static bool operator ==(ShellSyntaxNodeOrToken left, ShellSyntaxNodeOrToken right) => left.Equals(right);
    public static bool operator !=(ShellSyntaxNodeOrToken left, ShellSyntaxNodeOrToken right) => !left.Equals(right);
}
