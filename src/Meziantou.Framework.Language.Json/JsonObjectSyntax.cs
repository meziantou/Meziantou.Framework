namespace Meziantou.Framework.Language.Json;

/// <summary>Represents a JSON object value.</summary>
public sealed class JsonObjectSyntax : JsonValueSyntax
{
    private readonly IReadOnlyList<JsonSyntaxNode> _childNodes;

    public JsonObjectSyntax(JsonSyntaxToken openBraceToken, IReadOnlyList<JsonSyntaxNode> childNodes, JsonSyntaxToken closeBraceToken)
        : base(JsonSyntaxKind.JsonObject, BuildText(openBraceToken, childNodes, closeBraceToken), openBraceToken.FullSpan.Start, [openBraceToken, closeBraceToken])
    {
        OpenBraceToken = openBraceToken;
        CloseBraceToken = closeBraceToken;
        _childNodes = childNodes ?? [];
        Members = _childNodes.OfType<JsonMemberSyntax>().ToArray();
    }

    public JsonSyntaxToken OpenBraceToken { get; }
    public IReadOnlyList<JsonMemberSyntax> Members { get; }
    public JsonSyntaxToken CloseBraceToken { get; }
    public override IReadOnlyList<JsonSyntaxNode> ChildNodes => _childNodes;

    public JsonMemberSyntax? GetMember(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Members.FirstOrDefault(member => string.Equals(member.Name, name, StringComparison.Ordinal));
    }

    public JsonObjectSyntax WithMembers(IEnumerable<JsonMemberSyntax>? members)
    {
        var updatedMembers = members?.ToArray() ?? [];
        if (updatedMembers.SequenceEqual(Members))
            return this;

        return SyntaxFactory.Object(updatedMembers);
    }

    public JsonObjectSyntax WithChildNodes(IEnumerable<JsonSyntaxNode>? childNodes)
    {
        var nodes = childNodes?.ToArray() ?? [];
        if (nodes.SequenceEqual(ChildNodes))
            return this;

        return new JsonObjectSyntax(OpenBraceToken, nodes, CloseBraceToken);
    }

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitObject(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitObject(this);

    private static string BuildText(JsonSyntaxToken openBraceToken, IReadOnlyList<JsonSyntaxNode>? childNodes, JsonSyntaxToken closeBraceToken)
    {
        var builder = new StringBuilder();
        builder.Append(openBraceToken.ToFullString());
        foreach (var child in childNodes ?? [])
        {
            builder.Append(child.ToFullString());
        }

        builder.Append(closeBraceToken.ToFullString());

        return builder.ToString();
    }
}
