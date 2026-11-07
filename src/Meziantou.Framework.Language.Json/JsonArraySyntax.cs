using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>An array: brackets around a comma-separated list of values.</summary>
public sealed class JsonArraySyntax : JsonValueSyntax
{
    private SyntaxNode? _elements;

    internal JsonArraySyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenBracketToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the values in the array. The commas between them are the separators of the list.</summary>
    public SeparatedSyntaxList<JsonValueSyntax> Elements
    {
        get
        {
            var red = GetRed(ref _elements, 1);

            return red is null ? default : new SeparatedSyntaxList<JsonValueSyntax>(new SyntaxNodeOrTokenList(red, GetChildIndex(1)));
        }
    }

    public SyntaxToken CloseBracketToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Returns this array with the given parts, or itself when nothing changed.</summary>
    public JsonArraySyntax Update(SyntaxToken openBracketToken, SeparatedSyntaxList<JsonValueSyntax> elements, SyntaxToken closeBracketToken)
    {
        if (openBracketToken.Node == Green.GetSlot(0) && elements.Green == Green.GetSlot(1) && closeBracketToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.JsonArray(openBracketToken, elements, closeBracketToken).WithAnnotationsFrom(this);
    }

    public JsonArraySyntax WithOpenBracketToken(SyntaxToken openBracketToken) => Update(openBracketToken, Elements, CloseBracketToken);
    public JsonArraySyntax WithElements(SeparatedSyntaxList<JsonValueSyntax> elements) => Update(OpenBracketToken, elements, CloseBracketToken);
    public JsonArraySyntax WithCloseBracketToken(SyntaxToken closeBracketToken) => Update(OpenBracketToken, Elements, closeBracketToken);
    public JsonArraySyntax AddElements(params JsonValueSyntax[] items) => WithElements(Elements.AddRange(items));

    internal override SyntaxNode? GetNodeSlot(int index) => index == 1 ? GetRed(ref _elements, 1) : null;
    internal override SyntaxNode? GetCachedSlot(int index) => index == 1 ? _elements : null;

    public override void Accept(JsonSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitJsonArray(this);
    }

    public override TResult? Accept<TResult>(JsonSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitJsonArray(this);
    }
}
