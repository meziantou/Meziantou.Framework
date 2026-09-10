using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language;

/// <summary>Either a node or a token, as they appear side by side among the children of a node.</summary>
/// <remarks><see langword="default"/> is the token that is not there, matching <see cref="SyntaxToken.None"/>.</remarks>
[StructLayout(LayoutKind.Auto)]
[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public readonly struct SyntaxNodeOrToken : IEquatable<SyntaxNodeOrToken>
{
    private readonly SyntaxNode? _nodeOrParent;
    private readonly GreenNode? _token;
    private readonly int _position;
    private readonly int _tokenIndex;

    internal SyntaxNodeOrToken(SyntaxNode? parent, GreenNode? token, int position, int index)
    {
        Debug.Assert(token is null || token.IsToken, "Only a token can back the token half.");

        _nodeOrParent = parent;
        _token = token;
        _position = position;
        _tokenIndex = index;
    }

    private SyntaxNodeOrToken(SyntaxNode node)
    {
        _nodeOrParent = node;
        _token = null;
        _position = node.Position;
        _tokenIndex = -1;
    }

    public bool IsNode => _tokenIndex < 0;
    public bool IsToken => !IsNode;

    /// <summary>Gets the language-specific kind as a plain integer.</summary>
    public int RawKind => IsNode ? _nodeOrParent?.RawKind ?? 0 : _token?.RawKind ?? 0;

    /// <summary>Gets the node this one is a child of.</summary>
    public SyntaxNode? Parent => IsNode ? _nodeOrParent?.Parent : _nodeOrParent;

    public TextSpan FullSpan => new(_position, UnderlyingNode?.FullWidth ?? 0);
    public TextSpan Span => IsNode ? _nodeOrParent!.Span : AsToken().Span;
    public int SpanStart => IsNode ? _nodeOrParent!.SpanStart : AsToken().SpanStart;

    public bool IsMissing => UnderlyingNode?.IsMissing ?? false;
    public bool ContainsDiagnostics => UnderlyingNode?.ContainsDiagnostics ?? false;
    public bool ContainsAnnotations => UnderlyingNode?.ContainsAnnotations ?? false;
    public bool ContainsSkippedText => UnderlyingNode?.ContainsSkippedText ?? false;

    internal GreenNode? UnderlyingNode => IsNode ? _nodeOrParent?.Green : _token;

    /// <summary>Returns the node, or <see langword="null"/> when this is a token.</summary>
    public SyntaxNode? AsNode() => IsNode ? _nodeOrParent : null;

    /// <summary>Returns whether this is a node, and the node itself when it is.</summary>
    public bool AsNode([NotNullWhen(true)] out SyntaxNode? node)
    {
        node = AsNode();

        return node is not null;
    }

    /// <summary>Returns the token, or <see cref="SyntaxToken.None"/> when this is a node.</summary>
    public SyntaxToken AsToken() => IsToken ? new SyntaxToken(_nodeOrParent, _token, _position, _tokenIndex) : default;

    /// <summary>Returns whether this is a token, and the token itself when it is.</summary>
    public bool AsToken(out SyntaxToken token)
    {
        token = AsToken();

        return IsToken;
    }

    /// <summary>Gets the children of the node, or nothing when this is a token.</summary>
    public ChildSyntaxList ChildNodesAndTokens() => IsNode ? _nodeOrParent!.ChildNodesAndTokens() : default;

    public SyntaxTriviaList GetLeadingTrivia() => IsNode ? _nodeOrParent!.GetLeadingTrivia() : AsToken().LeadingTrivia;
    public SyntaxTriviaList GetTrailingTrivia() => IsNode ? _nodeOrParent!.GetTrailingTrivia() : AsToken().TrailingTrivia;

    public override string ToString() => UnderlyingNode?.ToString() ?? string.Empty;
    public string ToFullString() => UnderlyingNode?.ToFullString() ?? string.Empty;

    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        UnderlyingNode?.WriteTo(writer);
    }

    /// <summary>Determines whether the two have the same structure and text, ignoring their positions.</summary>
    public bool IsEquivalentTo(SyntaxNodeOrToken other)
    {
        var left = UnderlyingNode;
        var right = other.UnderlyingNode;
        if (left is null || right is null)
            return left is null && right is null;

        return IsNode == other.IsNode && left.IsEquivalentTo(right);
    }

    public static implicit operator SyntaxNodeOrToken(SyntaxNode node) => new(node);
    public static implicit operator SyntaxNodeOrToken(SyntaxToken token) => new(token.Parent, token.Node, token.Position, token.Index);
    public static explicit operator SyntaxNode?(SyntaxNodeOrToken nodeOrToken) => nodeOrToken.AsNode();
    public static explicit operator SyntaxToken(SyntaxNodeOrToken nodeOrToken) => nodeOrToken.AsToken();

    /// <summary>Returns this as a node or token, for callers that cannot use the implicit conversion.</summary>
    public static SyntaxNodeOrToken FromNode(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new SyntaxNodeOrToken(node);
    }

    /// <summary>Returns this as a node or token, for callers that cannot use the implicit conversion.</summary>
    public static SyntaxNodeOrToken FromToken(SyntaxToken token) => token;

    public bool Equals(SyntaxNodeOrToken other)
        => ReferenceEquals(_nodeOrParent, other._nodeOrParent) && ReferenceEquals(_token, other._token) && _position == other._position && _tokenIndex == other._tokenIndex;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SyntaxNodeOrToken other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_nodeOrParent, _token, _position, _tokenIndex);
    public static bool operator ==(SyntaxNodeOrToken left, SyntaxNodeOrToken right) => left.Equals(right);
    public static bool operator !=(SyntaxNodeOrToken left, SyntaxNodeOrToken right) => !left.Equals(right);

    private string GetDebuggerDisplay() => $"{(IsNode ? "Node" : "Token")} {UnderlyingNode?.KindText} {Span}";
}
