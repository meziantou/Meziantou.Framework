using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>An array: brackets around a comma-separated list of values.</summary>
public sealed class TomlArraySyntax : TomlValueSyntax
{
    private SyntaxNode? _elements;

    internal TomlArraySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenBracketToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the values in the array. The commas between them are the separators of the list.</summary>
    public SeparatedSyntaxList<TomlValueSyntax> Elements
    {
        get
        {
            var red = GetRed(ref _elements, 1);

            return red is null ? default : new SeparatedSyntaxList<TomlValueSyntax>(new SyntaxNodeOrTokenList(red, GetChildIndex(1)));
        }
    }

    public SyntaxToken CloseBracketToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Returns this array with the given parts, or itself when nothing changed.</summary>
    public TomlArraySyntax Update(SyntaxToken openBracketToken, SeparatedSyntaxList<TomlValueSyntax> elements, SyntaxToken closeBracketToken)
    {
        if (openBracketToken.Node == Green.GetSlot(0) && elements.Green == Green.GetSlot(1) && closeBracketToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.TomlArray(openBracketToken, elements, closeBracketToken).WithAnnotationsFrom(this);
    }

    public TomlArraySyntax WithOpenBracketToken(SyntaxToken openBracketToken) => Update(openBracketToken, Elements, CloseBracketToken);
    public TomlArraySyntax WithElements(SeparatedSyntaxList<TomlValueSyntax> elements) => Update(OpenBracketToken, elements, CloseBracketToken);
    public TomlArraySyntax WithCloseBracketToken(SyntaxToken closeBracketToken) => Update(OpenBracketToken, Elements, closeBracketToken);
    public TomlArraySyntax AddElements(params TomlValueSyntax[] items) => WithElements(Elements.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => index == 1 ? GetRed(ref _elements, 1) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 1 ? _elements : null;

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
