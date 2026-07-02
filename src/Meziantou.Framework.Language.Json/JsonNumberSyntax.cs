namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a JSON number value.</summary>
public sealed class JsonNumberSyntax : JsonValueSyntax
{
    public JsonNumberSyntax(JsonSyntaxToken numberToken)
        : base(JsonSyntaxKind.JsonNumber, numberToken.ToFullString(), numberToken.FullSpan.Start, [numberToken])
    {
        NumberToken = numberToken;
    }

    public JsonSyntaxToken NumberToken { get; }
    public string Text => NumberToken.Text;

    public JsonNumberSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, Text, StringComparison.Ordinal))
            return this;

        return new JsonNumberSyntax(new JsonSyntaxToken(JsonSyntaxKind.NumberToken, text, text));
    }

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitNumber(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitNumber(this);
}
