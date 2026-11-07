using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A key, such as <c>name</c>, <c>"quoted key"</c>, or the dotted key <c>server.port</c>.</summary>
/// <remarks>
/// The parts of a dotted key and the dots between them are the tokens of one list, so the key keeps the whitespace
/// around each dot exactly as it was written.
/// </remarks>
public sealed class TomlKeySyntax : TomlSyntaxNode
{
    internal TomlKeySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the parts of the key and the dots between them.</summary>
    public SyntaxTokenList Tokens => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the parts of the key, without the dots between them.</summary>
    public IReadOnlyList<SyntaxToken> Parts => [.. Tokens.Where(token => !token.IsKind(SyntaxKind.DotToken))];

    /// <summary>Gets the name of each part of the key, with quotes removed and escape sequences resolved.</summary>
    /// <example><c>site."google.com"</c> has the two names <c>site</c> and <c>google.com</c>.</example>
    public IReadOnlyList<string> Names => [.. Tokens.Where(token => !token.IsKind(SyntaxKind.DotToken)).Select(token => token.ValueText)];

    /// <summary>Gets a value indicating whether the key has more than one part.</summary>
    public bool IsDotted => Tokens.Any(token => token.IsKind(SyntaxKind.DotToken));

    /// <summary>Returns this key with different tokens, or itself when nothing changed.</summary>
    public TomlKeySyntax Update(SyntaxTokenList tokens)
    {
        if (tokens.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.Key(tokens).WithAnnotationsFrom(this);
    }

    public TomlKeySyntax WithTokens(SyntaxTokenList tokens) => Update(tokens);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlKey(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlKey(this);
    }
}
