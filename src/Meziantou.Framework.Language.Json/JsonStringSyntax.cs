namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a JSON string value.</summary>
public sealed class JsonStringSyntax : JsonValueSyntax
{
    public JsonStringSyntax(JsonSyntaxToken stringToken)
        : base(JsonSyntaxKind.JsonString, stringToken.ToFullString(), stringToken.FullSpan.Start, [stringToken])
    {
        StringToken = stringToken;
    }

    public JsonSyntaxToken StringToken { get; }
    public string Value => StringToken.ValueText;

    public JsonStringSyntax WithValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.Equals(value, Value, StringComparison.Ordinal))
            return this;

        return new JsonStringSyntax(SyntaxFactory.StringToken(value));
    }

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitString(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitString(this);
}
