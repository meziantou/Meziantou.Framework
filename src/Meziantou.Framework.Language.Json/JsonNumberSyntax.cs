using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Json;

/// <summary>A number value.</summary>
/// <remarks>The text is kept exactly as written; JSON numbers do not all fit a .NET numeric type without loss.</remarks>
public sealed class JsonNumberSyntax : JsonValueSyntax
{
    internal JsonNumberSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken NumberToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));

    /// <summary>Gets the number exactly as it was written.</summary>
    public string Text => NumberToken.Text;

    /// <summary>Returns this number with a different token, or itself when nothing changed.</summary>
    public JsonNumberSyntax Update(SyntaxToken numberToken)
    {
        if (numberToken.Node == Green.GetSlot(0))
            return this;

        return SyntaxFactory.JsonNumber(numberToken).WithAnnotationsFrom(this);
    }

    public JsonNumberSyntax WithNumberToken(SyntaxToken numberToken) => Update(numberToken);

    /// <summary>Returns this number written differently.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public JsonNumberSyntax WithText(string text) => Update(SyntaxFactory.NumberToken(text).WithTriviaFrom(NumberToken));

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(JsonSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitJsonNumber(this);
    }

    public override TResult? Accept<TResult>(JsonSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitJsonNumber(this);
    }
}
