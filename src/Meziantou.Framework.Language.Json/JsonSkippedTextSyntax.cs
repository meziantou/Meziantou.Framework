using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>Text the parser could not use, kept so the document still round-trips.</summary>
/// <remarks>
/// It stands where a value was expected, which is why it is a value: a member whose value is nonsense still needs
/// something in that place.
/// </remarks>
public sealed class JsonSkippedTextSyntax : JsonValueSyntax
{
    internal JsonSkippedTextSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the tokens that were skipped.</summary>
    public SyntaxTokenList Tokens => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Returns this skipped text with different tokens, or itself when nothing changed.</summary>
    public JsonSkippedTextSyntax Update(SyntaxTokenList tokens)
    {
        if (tokens.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.JsonSkippedText(tokens).WithAnnotationsFrom(this);
    }

    public JsonSkippedTextSyntax WithTokens(SyntaxTokenList tokens) => Update(tokens);
    public JsonSkippedTextSyntax AddTokens(params SyntaxToken[] items) => WithTokens(Tokens.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(JsonSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitJsonSkippedText(this);
    }

    public override TResult? Accept<TResult>(JsonSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitJsonSkippedText(this);
    }
}
