using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>Text the parser could not use, kept so the document still round-trips.</summary>
public sealed class TomlSkippedTextSyntax : TomlEntrySyntax
{
    internal TomlSkippedTextSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the tokens that were skipped.</summary>
    public SyntaxTokenList Tokens => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Returns this skipped text with different tokens, or itself when nothing changed.</summary>
    public TomlSkippedTextSyntax Update(SyntaxTokenList tokens)
    {
        if (tokens.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.TomlSkippedText(tokens).WithAnnotationsFrom(this);
    }

    public TomlSkippedTextSyntax WithTokens(SyntaxTokenList tokens) => Update(tokens);
    public TomlSkippedTextSyntax AddTokens(params SyntaxToken[] items) => WithTokens(Tokens.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlSkippedText(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlSkippedText(this);
    }
}
