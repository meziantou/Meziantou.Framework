namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a JSON true, false, or null literal value.</summary>
public sealed class JsonLiteralSyntax : JsonValueSyntax
{
    public JsonLiteralSyntax(JsonSyntaxToken literalToken)
        : base(GetNodeKind(literalToken), literalToken.ToFullString(), literalToken.FullSpan.Start, [literalToken])
    {
        LiteralToken = literalToken;
    }

    public JsonSyntaxToken LiteralToken { get; }

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitLiteral(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitLiteral(this);

    private static JsonSyntaxKind GetNodeKind(JsonSyntaxToken literalToken)
    {
        return literalToken.Kind switch
        {
            JsonSyntaxKind.TrueKeyword => JsonSyntaxKind.JsonTrueLiteral,
            JsonSyntaxKind.FalseKeyword => JsonSyntaxKind.JsonFalseLiteral,
            JsonSyntaxKind.NullKeyword => JsonSyntaxKind.JsonNullLiteral,
            _ => throw new ArgumentException("Token must be a JSON literal token.", nameof(literalToken)),
        };
    }
}
