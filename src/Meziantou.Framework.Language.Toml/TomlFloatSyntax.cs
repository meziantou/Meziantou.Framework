using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A float, such as <c>3.14</c>, <c>6.626e-34</c>, <c>inf</c>, or <c>nan</c>.</summary>
public sealed class TomlFloatSyntax : TomlValueSyntax
{
    internal TomlFloatSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken FloatToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the value of the float.</summary>
    /// <remarks>
    /// The value is the nearest <see cref="double"/>: a number too close to zero for one, such as <c>1e-400</c>, is
    /// read as zero with its sign, as IEEE 754 rounding gives. A number too large for one is an error instead
    /// (<c>TOML0006</c>), because the nearest <see cref="double"/> would be an infinity, which is another value.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The token was built by hand and does not spell a float.</exception>
    public double Value => FloatToken.Value is double value ? value : Internals.TokenValues.ParseFloat(FloatToken.Text);

    /// <summary>Returns this value with a different token, or itself when nothing changed.</summary>
    public TomlFloatSyntax Update(SyntaxToken floatToken)
    {
        if (floatToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.TomlFloat(floatToken).WithAnnotationsFrom(this);
    }

    public TomlFloatSyntax WithFloatToken(SyntaxToken floatToken) => Update(floatToken);

    /// <summary>Returns this float with a new value.</summary>
    public TomlFloatSyntax WithValue(double value) => Update(SyntaxFactory.Literal(value).WithTriviaFrom(FloatToken));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlFloat(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlFloat(this);
    }
}
