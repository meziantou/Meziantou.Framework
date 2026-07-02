namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a JSON object member.</summary>
public sealed class JsonMemberSyntax : JsonSyntaxNode
{
    private readonly IReadOnlyList<JsonSyntaxNode> _childNodes;

    public JsonMemberSyntax(JsonSyntaxToken nameToken, JsonSyntaxToken colonToken, JsonValueSyntax value, JsonSyntaxToken? commaToken = null)
        : base(JsonSyntaxKind.JsonMember, BuildText(nameToken, colonToken, value, commaToken), GetFullStart(nameToken, colonToken, value, commaToken), BuildTokens(nameToken, colonToken, commaToken))
    {
        NameToken = nameToken;
        ColonToken = colonToken;
        Value = value;
        CommaToken = commaToken;
        _childNodes = [value];
    }

    public JsonSyntaxToken NameToken { get; }
    public string Name => NameToken.ValueText;
    public JsonSyntaxToken ColonToken { get; }
    public JsonValueSyntax Value { get; }
    public JsonSyntaxToken? CommaToken { get; }
    public override IReadOnlyList<JsonSyntaxNode> ChildNodes => _childNodes;

    public JsonMemberSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.Equals(name, Name, StringComparison.Ordinal))
            return this;

        return new JsonMemberSyntax(SyntaxFactory.StringToken(name), ColonToken, Value, CommaToken);
    }

    public JsonMemberSyntax WithValue(JsonValueSyntax value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (ReferenceEquals(value, Value))
            return this;

        return new JsonMemberSyntax(NameToken, ColonToken, value, CommaToken);
    }

    public JsonMemberSyntax WithCommaToken(JsonSyntaxToken? commaToken)
    {
        if (ReferenceEquals(commaToken, CommaToken))
            return this;

        return new JsonMemberSyntax(NameToken, ColonToken, Value, commaToken);
    }

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitMember(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitMember(this);

    private static IReadOnlyList<JsonSyntaxToken> BuildTokens(JsonSyntaxToken nameToken, JsonSyntaxToken colonToken, JsonSyntaxToken? commaToken)
    {
        return commaToken is null ? [nameToken, colonToken] : [nameToken, colonToken, commaToken];
    }

    private static string BuildText(JsonSyntaxToken nameToken, JsonSyntaxToken colonToken, JsonValueSyntax value, JsonSyntaxToken? commaToken)
    {
        return nameToken.ToFullString() + colonToken.ToFullString() + value.ToFullString() + (commaToken?.ToFullString() ?? string.Empty);
    }

    private static int GetFullStart(JsonSyntaxToken nameToken, JsonSyntaxToken colonToken, JsonValueSyntax value, JsonSyntaxToken? commaToken)
    {
        _ = value;
        _ = commaToken;

        if (!nameToken.IsMissing || nameToken.FullSpan.Length > 0)
            return nameToken.FullSpan.Start;

        if (!colonToken.IsMissing || colonToken.FullSpan.Length > 0)
            return colonToken.FullSpan.Start;

        return value.FullSpan.Start;
    }
}
