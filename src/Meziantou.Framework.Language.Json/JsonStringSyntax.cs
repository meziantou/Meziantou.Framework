using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>A string value.</summary>
public sealed class JsonStringSyntax : JsonValueSyntax
{
    internal JsonStringSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken StringToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the text of the string with its quotes removed and its escapes resolved.</summary>
    public string Value => StringToken.ValueText;

    /// <summary>Returns this string with a different token, or itself when nothing changed.</summary>
    public JsonStringSyntax Update(SyntaxToken stringToken)
    {
        if (stringToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.JsonString(stringToken).WithAnnotationsFrom(this);
    }

    public JsonStringSyntax WithStringToken(SyntaxToken stringToken) => Update(stringToken);

    /// <summary>Returns this string with a new value, escaped as JSON requires.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public JsonStringSyntax WithValue(string value) => Update(SyntaxFactory.Literal(value).WithTriviaFrom(StringToken));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(JsonSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitJsonString(this);
    }

    public override TResult? Accept<TResult>(JsonSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitJsonString(this);
    }
}
