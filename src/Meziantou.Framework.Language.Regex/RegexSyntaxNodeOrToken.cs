namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents either a syntax node, a syntax token, or nothing at all.</summary>
/// <remarks>
/// The default value represents an absent part. A node builds its text from a list of parts, and an optional part
/// that the source did not contain contributes nothing rather than being filtered out by the caller.
/// </remarks>
public readonly struct RegexSyntaxNodeOrToken : IEquatable<RegexSyntaxNodeOrToken>
{
    private readonly RegexSyntaxNode? _node;
    private readonly RegexSyntaxToken? _token;

    public RegexSyntaxNodeOrToken(RegexSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _node = node;
        _token = null;
    }

    public RegexSyntaxNodeOrToken(RegexSyntaxToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        _token = token;
        _node = null;
    }

    public bool IsNode => _node is not null;
    public bool IsToken => _token is not null;

    /// <summary>Returns <see langword="true"/> when this is the absent part.</summary>
    public bool IsNone => _node is null && _token is null;

    public RegexSyntaxNode Node => _node ?? throw new InvalidOperationException("Current value is not a node.");
    public RegexSyntaxToken Token => _token ?? throw new InvalidOperationException("Current value is not a token.");
    public RegexSyntaxKind Kind => _node?.Kind ?? _token?.Kind ?? RegexSyntaxKind.None;
    public TextSpan FullSpan => _node?.FullSpan ?? _token?.FullSpan ?? default;

    public string ToFullString() => _node?.ToFullString() ?? _token?.ToFullString() ?? string.Empty;

    public bool Equals(RegexSyntaxNodeOrToken other) => ReferenceEquals(_node, other._node) && ReferenceEquals(_token, other._token);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RegexSyntaxNodeOrToken other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_node, _token);
    public static bool operator ==(RegexSyntaxNodeOrToken left, RegexSyntaxNodeOrToken right) => left.Equals(right);
    public static bool operator !=(RegexSyntaxNodeOrToken left, RegexSyntaxNodeOrToken right) => !left.Equals(right);
}
