using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>One of <c>true</c>, <c>false</c>, or <c>null</c>. Which one is the node's kind.</summary>
public sealed class JsonLiteralSyntax : JsonValueSyntax
{
    internal JsonLiteralSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken LiteralToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Returns this literal with a different token, or itself when nothing changed.</summary>
    /// <remarks>The kind of the node follows the keyword, so replacing <c>true</c> with <c>null</c> changes both.</remarks>
    public JsonLiteralSyntax Update(SyntaxToken literalToken)
    {
        if (literalToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.JsonLiteral(literalToken).WithAnnotationsFrom(this);
    }

    public JsonLiteralSyntax WithLiteralToken(SyntaxToken literalToken) => Update(literalToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(JsonSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitJsonLiteral(this);
    }

    public override TResult? Accept<TResult>(JsonSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitJsonLiteral(this);
    }
}
