using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>An array value, including its punctuation and trivia.</summary>
public sealed class TomlArraySyntax : TomlSyntaxNode
{
    internal TomlArraySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenBracketToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken CloseBracketToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));
    public SyntaxTokenList Contents => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    public TomlArraySyntax Update(SyntaxToken openBracketToken, SyntaxTokenList contents, SyntaxToken closeBracketToken)
    {
        if (openBracketToken.Node == Green.GetSlot(0) && contents.Node == Green.GetSlot(1) && closeBracketToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.TomlArray(openBracketToken, contents, closeBracketToken).WithAnnotationsFrom(this);
    }

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.VisitTomlArray(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);
        return visitor.VisitTomlArray(this);
    }
}
