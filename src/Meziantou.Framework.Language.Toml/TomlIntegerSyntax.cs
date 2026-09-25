using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>An integer, such as <c>42</c>, <c>-17</c>, <c>1_000</c>, or <c>0xDEADBEEF</c>.</summary>
public sealed class TomlIntegerSyntax : TomlValueSyntax
{
    internal TomlIntegerSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken IntegerToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the value of the integer.</summary>
    /// <exception cref="InvalidOperationException">The token was built by hand and does not spell an integer.</exception>
    public long Value => IntegerToken.Value is long value ? value : Internals.TokenValues.ParseInteger(IntegerToken.Text);

    /// <summary>Returns this value with a different token, or itself when nothing changed.</summary>
    public TomlIntegerSyntax Update(SyntaxToken integerToken)
    {
        if (integerToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.TomlInteger(integerToken).WithAnnotationsFrom(this);
    }

    public TomlIntegerSyntax WithIntegerToken(SyntaxToken integerToken) => Update(integerToken);

    /// <summary>Returns this integer with a new value, written in decimal.</summary>
    public TomlIntegerSyntax WithValue(long value) => Update(SyntaxFactory.Literal(value).WithTriviaFrom(IntegerToken));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlInteger(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlInteger(this);
    }
}
