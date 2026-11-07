using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A value the parser could not read, or one that is missing, kept so the document still round-trips.</summary>
/// <remarks>A missing value has no tokens.</remarks>
public sealed class TomlSkippedValueSyntax : TomlValueSyntax
{
    internal TomlSkippedValueSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    /// <summary>Gets the tokens that were skipped.</summary>
    public SyntaxTokenList Tokens => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Returns this skipped value with different tokens, or itself when nothing changed.</summary>
    public TomlSkippedValueSyntax Update(SyntaxTokenList tokens)
    {
        if (tokens.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.TomlSkippedValue(tokens).WithAnnotationsFrom(this);
    }

    public TomlSkippedValueSyntax WithTokens(SyntaxTokenList tokens) => Update(tokens);
    public TomlSkippedValueSyntax AddTokens(params SyntaxToken[] items) => WithTokens(Tokens.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlSkippedValue(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlSkippedValue(this);
    }
}
