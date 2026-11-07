using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary><c>true</c> or <c>false</c>.</summary>
public sealed class TomlBooleanSyntax : TomlValueSyntax
{
    internal TomlBooleanSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken BooleanToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    public bool Value => BooleanToken.IsKind(SyntaxKind.TrueKeyword);

    /// <summary>Returns this value with a different token, or itself when nothing changed.</summary>
    public TomlBooleanSyntax Update(SyntaxToken booleanToken)
    {
        if (booleanToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.TomlBoolean(booleanToken).WithAnnotationsFrom(this);
    }

    public TomlBooleanSyntax WithBooleanToken(SyntaxToken booleanToken) => Update(booleanToken);

    public TomlBooleanSyntax WithValue(bool value) => Update(SyntaxFactory.Literal(value).WithTriviaFrom(BooleanToken));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlBoolean(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlBoolean(this);
    }
}
