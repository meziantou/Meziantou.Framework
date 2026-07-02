namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a JSON array value.</summary>
public sealed class JsonArraySyntax : JsonValueSyntax
{
    private readonly IReadOnlyList<JsonSyntaxNode> _childNodes;

    public JsonArraySyntax(JsonSyntaxToken openBracketToken, IReadOnlyList<JsonSyntaxNode> childNodes, JsonSyntaxToken closeBracketToken)
        : base(JsonSyntaxKind.JsonArray, BuildText(openBracketToken, childNodes, closeBracketToken), openBracketToken.FullSpan.Start, [openBracketToken, closeBracketToken])
    {
        OpenBracketToken = openBracketToken;
        CloseBracketToken = closeBracketToken;
        _childNodes = childNodes ?? [];
        Elements = _childNodes.OfType<JsonArrayElementSyntax>().ToArray();
    }

    public JsonSyntaxToken OpenBracketToken { get; }
    public IReadOnlyList<JsonArrayElementSyntax> Elements { get; }
    public JsonSyntaxToken CloseBracketToken { get; }
    public override IReadOnlyList<JsonSyntaxNode> ChildNodes => _childNodes;

    public JsonArraySyntax WithElements(IEnumerable<JsonArrayElementSyntax>? elements)
    {
        var updatedElements = elements?.ToArray() ?? [];
        if (updatedElements.SequenceEqual(Elements))
            return this;

        return SyntaxFactory.Array(updatedElements);
    }

    public JsonArraySyntax WithChildNodes(IEnumerable<JsonSyntaxNode>? childNodes)
    {
        var nodes = childNodes?.ToArray() ?? [];
        if (nodes.SequenceEqual(ChildNodes))
            return this;

        return new JsonArraySyntax(OpenBracketToken, nodes, CloseBracketToken);
    }

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitArray(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitArray(this);

    private static string BuildText(JsonSyntaxToken openBracketToken, IReadOnlyList<JsonSyntaxNode>? childNodes, JsonSyntaxToken closeBracketToken)
    {
        var builder = new StringBuilder();
        builder.Append(openBracketToken.ToFullString());
        foreach (var child in childNodes ?? [])
        {
            builder.Append(child.ToFullString());
        }

        builder.Append(closeBracketToken.ToFullString());

        return builder.ToString();
    }
}
