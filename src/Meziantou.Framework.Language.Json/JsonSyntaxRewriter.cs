namespace Meziantou.Framework.Language.Json;

/// <summary>Visitor that can produce a rewritten JSON syntax tree.</summary>
public class JsonSyntaxRewriter : JsonSyntaxVisitor<JsonSyntaxNode?>
{
    public override JsonSyntaxNode? VisitDocument(JsonDocumentSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewrittenChildren = RewriteNodes(node.ChildNodes);
        if (rewrittenChildren is null)
            return node;

        return node.WithChildNodes(rewrittenChildren);
    }

    public override JsonSyntaxNode? VisitObject(JsonObjectSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewrittenChildren = RewriteNodes(node.ChildNodes);
        if (rewrittenChildren is null)
            return node;

        return node.WithChildNodes(rewrittenChildren);
    }

    public override JsonSyntaxNode? VisitMember(JsonMemberSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewrittenValue = VisitCore(node.Value);
        if (rewrittenValue is not JsonValueSyntax value || ReferenceEquals(value, node.Value))
            return node;

        return node.WithValue(value);
    }

    public override JsonSyntaxNode? VisitArray(JsonArraySyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewrittenChildren = RewriteNodes(node.ChildNodes);
        if (rewrittenChildren is null)
            return node;

        return node.WithChildNodes(rewrittenChildren);
    }

    public override JsonSyntaxNode? VisitArrayElement(JsonArrayElementSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewrittenValue = VisitCore(node.Value);
        if (rewrittenValue is not JsonValueSyntax value || ReferenceEquals(value, node.Value))
            return node;

        return node.WithValue(value);
    }

    public override JsonSyntaxNode? VisitString(JsonStringSyntax node) => node;
    public override JsonSyntaxNode? VisitNumber(JsonNumberSyntax node) => node;
    public override JsonSyntaxNode? VisitLiteral(JsonLiteralSyntax node) => node;
    public override JsonSyntaxNode? VisitSkippedText(JsonSkippedTextSyntax node) => node;

    protected virtual JsonSyntaxNode? VisitCore(JsonSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Visit(node);
    }

    private List<JsonSyntaxNode>? RewriteNodes<TNode>(IReadOnlyList<TNode> nodes)
        where TNode : JsonSyntaxNode
    {
        List<JsonSyntaxNode>? rewritten = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var current = nodes[index];
            var updated = VisitCore(current) ?? current;
            if (rewritten is null)
            {
                if (!ReferenceEquals(current, updated))
                {
                    rewritten = [];
                    for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    {
                        rewritten.Add(nodes[copyIndex]);
                    }

                    rewritten.Add(updated);
                }
            }
            else
            {
                rewritten.Add(updated);
            }
        }

        return rewritten;
    }
}
