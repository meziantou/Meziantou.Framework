namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a JSON array element, including its optional comma separator.</summary>
public sealed class JsonArrayElementSyntax : JsonSyntaxNode
{
    private readonly IReadOnlyList<JsonSyntaxNode> _childNodes;

    public JsonArrayElementSyntax(JsonValueSyntax value, JsonSyntaxToken? commaToken = null)
        : base(JsonSyntaxKind.JsonArrayElement, BuildText(value, commaToken), GetFullStart(value, commaToken), BuildTokens(commaToken))
    {
        Value = value;
        CommaToken = commaToken;
        _childNodes = [value];
    }

    public JsonValueSyntax Value { get; }
    public JsonSyntaxToken? CommaToken { get; }
    public override IReadOnlyList<JsonSyntaxNode> ChildNodes => _childNodes;

    public JsonArrayElementSyntax WithValue(JsonValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ReferenceEquals(value, Value))
            return this;

        return new JsonArrayElementSyntax(value, CommaToken);
    }

    public JsonArrayElementSyntax WithCommaToken(JsonSyntaxToken? commaToken)
    {
        if (ReferenceEquals(commaToken, CommaToken))
            return this;

        return new JsonArrayElementSyntax(Value, commaToken);
    }

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitArrayElement(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitArrayElement(this);

    private static IReadOnlyList<JsonSyntaxToken> BuildTokens(JsonSyntaxToken? commaToken)
    {
        return commaToken is null ? [] : [commaToken];
    }

    private static string BuildText(JsonValueSyntax value, JsonSyntaxToken? commaToken)
    {
        return value.ToFullString() + (commaToken?.ToFullString() ?? string.Empty);
    }

    private static int GetFullStart(JsonValueSyntax value, JsonSyntaxToken? commaToken)
    {
        _ = commaToken;

        return value.FullSpan.Start;
    }
}
